using System.Collections.Generic;
using System.Text;
using FaceSlapper.Battle;
using FaceSlapper.Core;
using FaceSlapper.Input;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.FrameSync
{
    /// <summary>
    /// 帧同步管理器（场景网络对象，挂在 Room 物体上）：确定性 lockstep 会话。
    /// 服务器中继模式：各端 30Hz 采集本地输入上行（ServerRpc），服务器校验后广播（ObserversRpc）。
    /// 协议规则由 <see cref="FrameSyncProtocol"/> 保证：
    ///   1. 输入封存——只消费服务器广播回来的确认输入，本地采样不直接进入模拟；
    ///   2. 服务器校验——每玩家 tick 严格连续、首提优先、掉线拒收；
    ///   3. 掉线按统一生效 tick 移除，与各端推进进度无关；
    ///   4. 模拟状态权威数据在管理器数组中，实体（GameObject）仅作渲染载体。
    /// 与现有状态同步（Player.prefab 链路）完全解耦，由 GM 命令 StartFrameSync/StopFrameSync 驱动。
    /// </summary>
    public class FrameSyncManager : NetBehaviour
    {
        /// <summary>本端实例（场景唯一）。</summary>
        public static FrameSyncManager Instance { get; private set; }

        [SerializeField] private NetObject _playerPrefab;

        private const int StateIdle = 0;
        private const int StateRunning = 1;

        /// <summary>会话状态（服务器写，全员同步；仅作展示，启动以 RpcSessionStart 为准）。</summary>
        private readonly NetVar<int> _state = new NetVar<int>(StateIdle);

        /// <summary>模拟是否正在本端运行。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>已模拟的 tick 数（下一个待模拟的 tick 序号）。</summary>
        public int SimTick => _simTick;

        /// <summary>渲染插值系数（0~1，FrameSyncRender 使用）。</summary>
        public float RenderAlpha => Mathf.Clamp01(_simAccumulator / FrameSyncConfig.TickSeconds);

        // ---------------- 协议层 ----------------

        /// <summary>客户端角色：确认输入存储、成员移除调度。</summary>
        private readonly FrameSyncProtocol _session = new FrameSyncProtocol();

        /// <summary>服务器角色：输入转发校验、掉线生效 tick。</summary>
        private readonly FrameSyncProtocol _relay = new FrameSyncProtocol();

        // ---------------- 会话数据 ----------------

        private int _seed;
        private int[] _roster = new int[0];                 // 升序 clientId
        private int[] _pendingSpawnRaw = new int[0];        // 每玩家 3 个定点 raw（x,y,z）
        private bool _pendingStart;                          // 已收到开局广播，等待本地对象就位
        private readonly List<FrameSyncMovement> _entities = new List<FrameSyncMovement>(8); // 按 clientId 升序

        private int _simTick;
        private int _localInputTick;
        private float _simAccumulator;
        private float _inputAccumulator;
        private bool _stallLogged;
        private int _pendingButtons;                         // 边沿按键在采样间隙累积，防丢输入

        // 批处理缓冲（roster 下标对齐，TryBeginSimulation 时分配）。
        // ★ _batchStates 是模拟状态的权威数据：逐 tick 延续，不依赖实体对象存活。
        private FrameSyncMovement[] _rosterEntities = new FrameSyncMovement[0];
        private PlayerSimState[] _batchStates = new PlayerSimState[0];
        private FrameInput[] _batchInputs = new FrameInput[0];
        private bool[] _batchActive = new bool[0];

        // 服务器端哈希校验基线：tick -> (hash, 首个上报者)
        private readonly Dictionary<int, int> _hashBaseline = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _hashReporter = new Dictionary<int, int>();

        // ---------------- 诊断 ----------------

        private readonly Dictionary<int, FrameInput> _lastAppliedInputs = new Dictionary<int, FrameInput>();
        private float _debugLogUntil;

        /// <summary>
        /// GM 诊断：返回会话映射快照（各玩家活跃标记/位置/最近输入/确认水位），
        /// 并在接下来 5 秒内逐 tick 打印每个实体消费的输入，用于定位输入映射问题。
        /// </summary>
        public string DumpDebugState()
        {
            var sb = new StringBuilder(256);
            sb.Append($"localId={Net.LocalClientId} running={IsRunning} pending={_pendingStart} simTick={_simTick} ");
            sb.Append($"roster=[{JoinInts(_roster)}] | ");
            for (int i = 0; i < _roster.Length; i++)
            {
                int id = _roster[i];
                _lastAppliedInputs.TryGetValue(id, out FrameInput last);
                string pos = _batchStates.Length > i ? _batchStates[i].Position.ToString() : "?";
                sb.Append($"玩家#{id} active={_session.IsActive(id)} pos={pos} " +
                          $"最近输入=(mx={last.MoveX},my={last.MoveY},btn={last.Buttons},tick={last.Tick}) " +
                          $"确认水位={_session.LatestConfirmedTick(id)} | ");
            }
            _debugLogUntil = Time.time + 5f;
            return sb.ToString();
        }

        // ---------------- 生命周期 ----------------

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            EventBus.Subscribe<LocalInputEvent>(OnLocalInput);
        }

        protected override void OnDestroy()
        {
            EventBus.Unsubscribe<LocalInputEvent>(OnLocalInput);
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetSpawnServer()
        {
            Net.OnRemoteClientDisconnected += OnClientDisconnected;
        }

        public override void OnNetDespawnServer()
        {
            Net.OnRemoteClientDisconnected -= OnClientDisconnected;
            ResetLocal();
        }

        /// <summary>边沿按键在两次采样之间累积（水平轴/加速键采样时直读，不参与累积）。</summary>
        private void OnLocalInput(LocalInputEvent e)
        {
            if (e.Snapshot.AttackPressed) _pendingButtons |= (int)FrameButtons.Attack;
            if (e.Snapshot.PickupPressed) _pendingButtons |= (int)FrameButtons.Pickup;
            if (e.Snapshot.HitbackPressed) _pendingButtons |= (int)FrameButtons.Hitback;
            if (e.Snapshot.JumpPressed) _pendingButtons |= (int)FrameButtons.Jump;
        }

        // ---------------- 实体注册（FrameSyncMovement 回调） ----------------

        public void Register(FrameSyncMovement entity)
        {
            if (_entities.Contains(entity)) return;
            _entities.Add(entity);
            // 模拟遍历顺序必须确定：始终按 clientId 升序。
            _entities.Sort((x, y) => x.SimClientId.CompareTo(y.SimClientId));
        }

        public void Unregister(FrameSyncMovement entity) => _entities.Remove(entity);

        private FrameSyncMovement FindEntity(int clientId)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                FrameSyncMovement e = _entities[i];
                if (e != null && e.SimClientId == clientId) return e;
            }
            return null;
        }

        // ---------------- 对外请求（GM 命令入口） ----------------

        public void RequestStartFrameSync() => SendServerRpc(nameof(CmdStartFrameSync));

        public void RequestStopFrameSync() => SendServerRpc(nameof(CmdStopFrameSync));

        // ---------------- 服务器端：会话控制 ----------------

        [NetRpc]
        private void CmdStartFrameSync()
        {
            if (_state.Value == StateRunning) return;
            if (_playerPrefab == null)
            {
                Debug.LogError("[FrameSync] 未配置 PlayerFrameSync Prefab（_playerPrefab）。");
                return;
            }

            var roster = new List<int>(Net.Server.ClientIds);
            roster.Sort();
            if (roster.Count == 0) return;

            // 种子仅用于未来确定性随机（本版本模拟尚未消耗），广播保证各端一致。
            int seed = (int)(System.DateTime.UtcNow.Ticks & 0x7FFFFFFF);

            var posRaw = new int[roster.Count * 3];
            for (int i = 0; i < roster.Count; i++)
            {
                Vector3 pos = GetSpawnPosition(i);
                NetObject nob = Net.Server.Spawn(_playerPrefab, pos, Quaternion.identity, roster[i]);
                if (nob != null)
                {
                    var identity = nob.GetComponent<NetworkIdentity>();
                    if (identity != null)
                    {
                        identity.PlayerId.Value = roster[i];
                        identity.ColorIndex.Value = i;
                        identity.PlayerName.Value = $"Player{roster[i]}";
                    }
                }
                posRaw[i * 3] = (int)FP.FromFloat(pos.x).Raw;
                posRaw[i * 3 + 1] = (int)FP.FromFloat(pos.y).Raw;
                posRaw[i * 3 + 2] = (int)FP.FromFloat(pos.z).Raw;
            }

            _relay.ServerInitRoster(roster.ToArray(), FrameSyncConfig.InputDelayTicks);
            _hashBaseline.Clear();
            _hashReporter.Clear();
            _state.Value = StateRunning;
            SendObserversRpc(nameof(RpcSessionStart), seed, JoinInts(roster), JoinInts(posRaw));
            Debug.Log($"[FrameSync] 会话开始: {roster.Count} 名玩家, seed={seed}");
        }

        [NetRpc]
        private void CmdStopFrameSync()
        {
            if (_state.Value != StateRunning) return;
            _state.Value = StateIdle;

            FrameSyncMovement[] copy = _entities.ToArray();
            foreach (FrameSyncMovement e in copy)
                if (e != null) Net.Server.Despawn(e.NetObject);

            SendObserversRpc(nameof(RpcSessionStop));
            Debug.Log("[FrameSync] 会话结束");
        }

        [NetRpc]
        private void CmdInput(int tick, int moveX, int moveY, int buttons)
        {
            if (_state.Value != StateRunning) return;
            int sender = NetObject.RpcSenderClientId;
            if (sender < 0) sender = Net.LocalClientId; // Host 本地派发时 sender 为 -1

            // 输入封存校验：严格连续、首提优先、掉线/非成员拒收。
            if (!_relay.ServerTryRelay(sender, tick))
            {
                Debug.LogWarning($"[FrameSync] 拒绝输入: client{sender} tick={tick}（重复/跳号/已掉线/非成员）");
                return;
            }
            SendObserversRpc(nameof(RpcInput), sender, tick, moveX, moveY, buttons);
        }

        [NetRpc]
        private void CmdStateHash(int tick, int hash)
        {
            int sender = NetObject.RpcSenderClientId;
            if (sender < 0) sender = Net.LocalClientId;

            if (_hashBaseline.TryGetValue(tick, out int baseHash))
            {
                if (baseHash != hash)
                {
                    Debug.LogError($"[FrameSync] ★ 检测到不同步! tick={tick}, " +
                                   $"client{_hashReporter[tick]} hash={baseHash} vs client{sender} hash={hash}");
                }
            }
            else
            {
                _hashBaseline[tick] = hash;
                _hashReporter[tick] = sender;
            }

            // 防止基线表无限增长。
            if (_hashBaseline.Count > 64)
            {
                var stale = new List<int>();
                foreach (KeyValuePair<int, int> kvp in _hashBaseline)
                    if (kvp.Key < tick - 600) stale.Add(kvp.Key);
                foreach (int k in stale) { _hashBaseline.Remove(k); _hashReporter.Remove(k); }
            }
        }

        private void OnClientDisconnected(int clientId)
        {
            if (_state.Value != StateRunning) return;

            // 统一生效 tick：该玩家最后一条已转发输入的下一 tick。
            // 可靠有序通道保证此前的输入先于本消息到达各端。
            int effectiveTick = _relay.ServerMarkLeft(clientId);

            // 销毁对象仅影响视觉（模拟状态权威数据在管理器数组中，与对象存活无关）。
            FrameSyncMovement e = FindEntity(clientId);
            if (e != null) Net.Server.Despawn(e.NetObject);

            SendObserversRpc(nameof(RpcPlayerLeft), clientId, effectiveTick);
            Debug.Log($"[FrameSync] 玩家 {clientId} 掉线，统一于 tick {effectiveTick} 移除");
        }

        // ---------------- 客户端：会话广播接收 ----------------

        [NetRpc]
        private void RpcSessionStart(int seed, string rosterCsv, string positionsCsv)
        {
            _seed = seed;
            _roster = ParseInts(rosterCsv);
            _pendingSpawnRaw = ParseInts(positionsCsv);
            _session.BeginSession(_roster, FrameSyncConfig.InputDelayTicks);

            IsRunning = false;
            _pendingStart = true; // 等待所有玩家对象在本端生成完毕（见 TryBeginSimulation）
        }

        [NetRpc]
        private void RpcSessionStop() => ResetLocal();

        [NetRpc]
        private void RpcPlayerLeft(int clientId, int effectiveTick)
        {
            _session.OnPlayerLeft(clientId, effectiveTick);
            Debug.Log($"[FrameSync] 玩家 {clientId} 将于 tick {effectiveTick} 统一移除（冻结在最后状态）");
        }

        [NetRpc]
        private void RpcInput(int clientId, int tick, int moveX, int moveY, int buttons)
        {
            // 只有经服务器广播回来的输入才进入确认存储（输入封存）。
            _session.OnConfirmedInput(new FrameInput
            {
                Tick = tick,
                ClientId = clientId,
                MoveX = moveX,
                MoveY = moveY,
                Buttons = buttons,
            });
        }

        // ---------------- 本地节拍与模拟推进 ----------------

        private void Update()
        {
            if (_pendingStart) TryBeginSimulation();
            if (!IsRunning) return;

            float dt = Time.deltaTime;

            // 本地输入采集（30Hz 节拍，与模拟推进解耦：停帧等待期间仍持续采集上行）。
            _inputAccumulator += dt;
            while (_inputAccumulator >= FrameSyncConfig.TickSeconds)
            {
                _inputAccumulator -= FrameSyncConfig.TickSeconds;
                SampleAndSendLocalInput();
            }

            // 模拟推进：集齐全员确认输入才走 tick，否则停帧等待（lockstep 语义）。
            _simAccumulator += dt;
            const int maxStepsPerFrame = 5; // 追帧上限，防止卡顿后死亡螺旋
            int steps = 0;
            while (_simAccumulator >= FrameSyncConfig.TickSeconds && steps < maxStepsPerFrame)
            {
                // 成员移除在当前 tick 边界统一生效。
                _session.ApplyRemovals(_simTick);

                if (!_session.HasAllInputsFor(_simTick))
                {
                    if (!_stallLogged)
                    {
                        _stallLogged = true;
                        Debug.LogWarning($"[FrameSync] 等待输入，停帧于 tick {_simTick}");
                    }
                    _simAccumulator = Mathf.Min(_simAccumulator, FrameSyncConfig.TickSeconds);
                    break;
                }
                _stallLogged = false;
                _simAccumulator -= FrameSyncConfig.TickSeconds;
                StepSimulation();
                steps++;
            }
            if (steps >= maxStepsPerFrame) _simAccumulator = 0f;
        }

        private void SampleAndSendLocalInput()
        {
            int localId = Net.LocalClientId;
            int tick = _localInputTick + FrameSyncConfig.InputDelayTicks;
            _localInputTick++;
            if (!_session.IsActive(localId)) return; // 观战/非会话成员不上行

            InputSnapshot snap = default;
            if (GameManager.HasInstance)
            {
                InputComponent input = GameManager.Instance.Get<InputComponent>();
                if (input != null) snap = input.Current;
            }

            int buttons = _pendingButtons;
            if (snap.SpeedUpHeld) buttons |= (int)FrameButtons.SpeedUp;
            _pendingButtons = 0;

            // 只上行，不入本地模拟：待服务器广播回来后成为确认输入（输入封存）。
            SendServerRpc(nameof(CmdInput), tick,
                InputQuantizer.Quantize(snap.MoveAxis.x),
                InputQuantizer.Quantize(snap.MoveAxis.y),
                buttons);
        }

        private void StepSimulation()
        {
            // 收集：roster 下标对齐的输入/活跃标记（状态数组跨 tick 延续，是权威数据）。
            bool debugLog = Time.time < _debugLogUntil;
            int count = _roster.Length;
            for (int i = 0; i < count; i++)
            {
                int id = _roster[i];
                bool active = _session.IsActive(id);
                _batchActive[i] = active;
                if (!active) continue;

                FrameInput input = _session.GetInput(id, _simTick);
                _batchInputs[i] = input;
                _lastAppliedInputs[id] = input;
                if (debugLog)
                    Debug.Log($"[FrameSync] tick={_simTick} 玩家#{id} 消费确认输入: " +
                              $"mx={input.MoveX} my={input.MoveY} btn={input.Buttons}");
            }

            // 模拟：移动 → 圆形碰撞 → 击飞判定 → 攻击判定（与离线自测共用同一代码路径）。
            FrameSyncSim.SimulateAll(_batchStates, _batchInputs, _batchActive, count);
            FrameSyncSim.ResolveCollisions(_batchStates, _batchActive, count);
            FrameSyncSim.ResolveHitback(_batchStates, _batchInputs, _batchActive, count);
            FrameSyncSim.ResolveAttack(_batchStates, _batchInputs, _batchActive, count);

            // 写回实体（仅驱动渲染插值；对象已销毁不影响逻辑）。
            for (int i = 0; i < count; i++)
                if (_batchActive[i] && _rosterEntities[i] != null)
                    _rosterEntities[i].ApplyTickResult(_batchStates[i]);

            _session.ConsumeTick(_simTick);
            _simTick++;

            // 周期性上报状态哈希（不同步检测）。
            if (_simTick % FrameSyncConfig.HashIntervalTicks == 0)
                SendServerRpc(nameof(CmdStateHash), _simTick, ComputeLocalHash());
        }

        private int ComputeLocalHash()
        {
            uint h = 2166136261u;
            for (int i = 0; i < _roster.Length; i++)
            {
                int id = _roster[i];
                if (!_session.IsActive(id)) continue;
                h = FrameSyncSim.Mix(h, id);
                h = FrameSyncSim.MixState(h, _batchStates[i]);
            }
            return (int)h;
        }

        // ---------------- 开局等待与重置 ----------------

        /// <summary>开局广播可能先于玩家对象生成消息到达，等所有成员就位后以统一起始状态开跑。</summary>
        private void TryBeginSimulation()
        {
            if (_pendingSpawnRaw.Length != _roster.Length * 3)
            {
                Debug.LogError("[FrameSync] 开局数据不完整（出生点数量与名单不符）。");
                _pendingStart = false;
                return;
            }

            for (int i = 0; i < _roster.Length; i++)
                if (FindEntity(_roster[i]) == null) return;

            // 批处理缓冲按 roster 下标对齐分配。
            int count = _roster.Length;
            _rosterEntities = new FrameSyncMovement[count];
            _batchStates = new PlayerSimState[count];
            _batchInputs = new FrameInput[count];
            _batchActive = new bool[count];

            for (int i = 0; i < count; i++)
            {
                var state = new PlayerSimState
                {
                    Position = new FPVec3(
                        FP.FromRaw(_pendingSpawnRaw[i * 3]),
                        FP.FromRaw(_pendingSpawnRaw[i * 3 + 1]),
                        FP.FromRaw(_pendingSpawnRaw[i * 3 + 2])),
                    Facing = new FPVec2(FP.Zero, FP.One),
                    VelY = FP.Zero,
                    Grounded = true,
                };
                _batchStates[i] = state; // 权威状态（跨 tick 延续）

                FrameSyncMovement e = FindEntity(_roster[i]);
                e.InitSimState(state);   // 渲染载体初始姿态
                _rosterEntities[i] = e;
            }

            _simTick = 0;
            _localInputTick = 0;
            _simAccumulator = 0f;
            _inputAccumulator = 0f;
            _pendingButtons = 0;
            _stallLogged = false;
            _lastAppliedInputs.Clear();
            _pendingStart = false;
            IsRunning = true;
            Debug.Log($"[FrameSync] 模拟开始: {_roster.Length} 名玩家, seed={_seed}");
        }

        private void ResetLocal()
        {
            IsRunning = false;
            _pendingStart = false;
            _roster = new int[0];
            _pendingSpawnRaw = new int[0];
            _session.EndSession();
            _simTick = 0;
            _localInputTick = 0;
            _simAccumulator = 0f;
            _inputAccumulator = 0f;
            _pendingButtons = 0;
            _stallLogged = false;
            _lastAppliedInputs.Clear();
        }

        // ---------------- 工具 ----------------

        private static Vector3 GetSpawnPosition(int index)
        {
            List<Transform> points = PlayerSpawnPoints.Points;
            if (points.Count > 0)
                return points[index % points.Count].position;
            float angle = index * 90f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * 4f, 0.05f, Mathf.Sin(angle) * 4f);
        }

        private static string JoinInts(IList<int> values)
        {
            var sb = new StringBuilder(values.Count * 4);
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i]);
            }
            return sb.ToString();
        }

        private static int[] ParseInts(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return new int[0];
            string[] parts = csv.Split(',');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = int.Parse(parts[i]);
            return result;
        }
    }
}
