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
    /// 帧同步管理器（场景网络对象，挂在 Room 物体上）：乐观帧同步（预测 + 回滚）会话。
    /// 服务器中继模式：各端 30Hz 采集本地输入上行（ServerRpc），服务器校验后广播（ObserversRpc）。
    /// 分层职责：
    ///   - <see cref="FrameSyncProtocol"/>：输入封存（只消费服务器广播的确认输入）、服务器转发校验
    ///     （严格连续/首提优先/掉线拒收）、掉线统一生效 tick、全员确认连续水位线；
    ///   - <see cref="FrameSyncSession"/>：客户端预测推进 + 快照回滚重放（模拟状态权威数据）；
    ///   - 本类：RPC 粘合、本地采样节拍、实体（GameObject）渲染写回、确认哈希上报。
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
        public int SimTick => _session.SimTick;

        /// <summary>渲染插值系数（0~1，FrameSyncRender 使用）。</summary>
        public float RenderAlpha => Mathf.Clamp01(_simAccumulator / FrameSyncConfig.TickSeconds);

        // ---------------- 协议与会话 ----------------

        /// <summary>客户端会话：预测推进 + 回滚重放（内含协议层实例）。</summary>
        private readonly FrameSyncSession _session = new FrameSyncSession();

        /// <summary>服务器角色：输入转发校验、掉线生效 tick。</summary>
        private readonly FrameSyncProtocol _relay = new FrameSyncProtocol();

        // ---------------- 会话数据 ----------------

        private int _seed;
        private int[] _roster = new int[0];                 // 升序 clientId
        private int[] _pendingSpawnRaw = new int[0];        // 每玩家 3 个定点 raw（x,y,z）
        private bool _pendingStart;                          // 已收到开局广播，等待本地对象就位
        private readonly List<FrameSyncMovement> _entities = new List<FrameSyncMovement>(8); // 按 clientId 升序

        private float _simAccumulator;
        private float _inputAccumulator;
        private int _localInputTick;
        private bool _stallLogged;
        private int _pendingButtons;                         // 边沿按键在采样间隙累积，防丢输入
        private int _nextHashReport;                         // 下一个待上报的哈希 tick（仅确认水位线内）

        // 渲染写回缓冲（roster 下标对齐，TryBeginSimulation 时分配）。
        private FrameSyncMovement[] _rosterEntities = new FrameSyncMovement[0];
        private PlayerSimState[] _renderStates = new PlayerSimState[0];

        // 服务器端哈希校验基线：tick -> (hash, 首个上报者)
        private readonly Dictionary<int, int> _hashBaseline = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _hashReporter = new Dictionary<int, int>();

        // ---------------- 网络延迟模拟（GM 实测入口，仅影响本端收发，不进入模拟） ----------------

        private struct DelayedSend { public float DueTime; public int Tick, MoveX, MoveY, Buttons; }
        private struct DelayedRecv
        {
            public float DueTime;
            public bool IsRemoval;
            public FrameInput Input;
            public int LeftClientId, EffectiveTick;
        }

        private readonly List<DelayedSend> _delayedSends = new List<DelayedSend>(64);
        private readonly List<DelayedRecv> _delayedRecvs = new List<DelayedRecv>(64);
        private int _debugDelayMs;
        private int _debugJitterMs;
        private float _lastSendDue;   // 截止时间单调不减：抖动只改变延迟量，不破坏可靠有序假设
        private float _lastRecvDue;

        /// <summary>模拟网络延迟：本端输入上/下行消息均延迟 ms（±jitterMs），0 关闭并立即冲刷队列。</summary>
        public void SetDebugNetDelay(int ms, int jitterMs)
        {
            _debugDelayMs = Mathf.Max(0, ms);
            _debugJitterMs = Mathf.Max(0, jitterMs);
            if (_debugDelayMs == 0 && _debugJitterMs == 0)
            {
                FlushDelayedSends(float.MaxValue);
                FlushDelayedRecvs(float.MaxValue);
            }
        }

        // ---------------- 诊断窗口（FrameSyncDebug 开启，每秒一行回滚统计） ----------------

        private const float DiagWindowSeconds = 10f;
        private float _diagUntil = -1f;
        private float _diagNextLine;
        private int _diagRollbacks;
        private int _diagMaxDepth;
        private long _diagTotalBase;
        private long _diagPredictedBase;

        // ---------------- 诊断 ----------------

        /// <summary>
        /// GM 诊断：返回会话快照（水位线/回滚统计/各玩家位置与确认水位），
        /// 并开启 10 秒诊断窗口——每秒输出一行：水位滞后 / 回滚次数 / 最大回滚深度 / 预测输入占比。
        /// </summary>
        public string DumpDebugState()
        {
            var sb = new StringBuilder(256);
            sb.Append($"localId={Net.LocalClientId} running={IsRunning} pending={_pendingStart} " +
                      $"simTick={_session.SimTick} 水位线={_session.ConfirmedWaterline} " +
                      $"回滚次数={_session.RollbackCount} 最大深度={_session.MaxRollbackDepth} " +
                      $"desync={_session.Desynced} 模拟延迟={_debugDelayMs}±{_debugJitterMs}ms | ");
            for (int i = 0; i < _roster.Length; i++)
            {
                int id = _roster[i];
                string pos = _renderStates.Length > i ? _renderStates[i].Position.ToString() : "?";
                sb.Append($"玩家#{id} active={_session.IsActive(id)} pos={pos} " +
                          $"确认水位={_session.LatestConfirmedTick(id)} | ");
            }

            _diagUntil = Time.time + DiagWindowSeconds;
            _diagNextLine = Time.time + 1f;
            _diagRollbacks = 0;
            _diagMaxDepth = 0;
            _diagTotalBase = _session.TotalInputCount;
            _diagPredictedBase = _session.PredictedInputCount;
            sb.Append($" || 诊断窗口已开启（{DiagWindowSeconds:0} 秒，每秒一行回滚统计）");
            return sb.ToString();
        }

        // ---------------- 生命周期 ----------------

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            _session.LogWarning = msg => Debug.LogWarning(msg);
            _session.LogError = msg => Debug.LogError(msg);
            _session.OnRollback = depth =>
            {
                _diagRollbacks++;
                if (depth > _diagMaxDepth) _diagMaxDepth = depth;
            };
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

            // 销毁对象仅影响视觉（模拟状态权威数据在会话数组中，与对象存活无关）。
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

            IsRunning = false;
            _pendingStart = true; // 等待所有玩家对象在本端生成完毕（见 TryBeginSimulation）
        }

        [NetRpc]
        private void RpcSessionStop() => ResetLocal();

        [NetRpc]
        private void RpcPlayerLeft(int clientId, int effectiveTick)
        {
            if (DelayEnabled)
            {
                EnqueueRecv(new DelayedRecv { IsRemoval = true, LeftClientId = clientId, EffectiveTick = effectiveTick });
                return;
            }
            _session.OnPlayerLeft(clientId, effectiveTick);
            Debug.Log($"[FrameSync] 玩家 {clientId} 将于 tick {effectiveTick} 统一移除（冻结在最后状态）");
        }

        [NetRpc]
        private void RpcInput(int clientId, int tick, int moveX, int moveY, int buttons)
        {
            var fi = new FrameInput
            {
                Tick = tick,
                ClientId = clientId,
                MoveX = moveX,
                MoveY = moveY,
                Buttons = buttons,
            };
            if (DelayEnabled)
            {
                EnqueueRecv(new DelayedRecv { Input = fi });
                return;
            }
            // 只有经服务器广播回来的输入才进入确认存储（输入封存）；
            // 若该 tick 已被预测消费且不一致，会话层自动安排回滚。
            _session.OnConfirmedInput(fi);
        }

        // ---------------- 本地节拍与模拟推进 ----------------

        private void Update()
        {
            if (_pendingStart) TryBeginSimulation();

            // 延迟模拟队列冲刷（即使会话未运行也要冲刷，避免残留）。
            if (_delayedSends.Count > 0) FlushDelayedSends(Time.time);
            if (_delayedRecvs.Count > 0) FlushDelayedRecvs(Time.time);

            if (!IsRunning) return;
            TickDiagWindow();

            float dt = Time.deltaTime;

            // 本地输入采集（30Hz 节拍，与模拟推进解耦）。
            _inputAccumulator += dt;
            while (_inputAccumulator >= FrameSyncConfig.TickSeconds)
            {
                _inputAccumulator -= FrameSyncConfig.TickSeconds;
                SampleAndSendLocalInput();
            }

            // 模拟推进：乐观预测——缺确认输入时按预测推进，不回填等待；
            // 仅当预测窗口已满或本地输入未采样时停帧。
            _simAccumulator += dt;
            const int maxStepsPerFrame = 5; // 追帧上限，防止卡顿后死亡螺旋
            int steps = 0;
            while (_simAccumulator >= FrameSyncConfig.TickSeconds && steps < maxStepsPerFrame)
            {
                if (!_session.TryAdvance(out bool stalled))
                {
                    if (!_stallLogged)
                    {
                        _stallLogged = true;
                        Debug.LogWarning($"[FrameSync] 停帧于 tick {_session.SimTick}（{_session.LastStallReason}）");
                    }
                    _simAccumulator = Mathf.Min(_simAccumulator, FrameSyncConfig.TickSeconds);
                    break;
                }
                _stallLogged = false;
                _simAccumulator -= FrameSyncConfig.TickSeconds;
                PushStatesToEntities();
                ReportConfirmedHashes();
                steps++;
            }
            if (steps >= maxStepsPerFrame) _simAccumulator = 0f;
        }

        private void SampleAndSendLocalInput()
        {
            int localId = Net.LocalClientId;
            int tick = _localInputTick + FrameSyncConfig.InputDelayTicks;
            _localInputTick++;
            if (!_session.InSession || !_session.IsActive(localId)) return; // 观战/非会话成员不上行

            InputSnapshot snap = default;
            if (GameManager.HasInstance)
            {
                InputComponent input = GameManager.Instance.Get<InputComponent>();
                if (input != null) snap = input.Current;
            }

            int buttons = _pendingButtons;
            if (snap.SpeedUpHeld) buttons |= (int)FrameButtons.SpeedUp;
            _pendingButtons = 0;

            var fi = new FrameInput
            {
                Tick = tick,
                ClientId = localId,
                MoveX = InputQuantizer.Quantize(snap.MoveAxis.x),
                MoveY = InputQuantizer.Quantize(snap.MoveAxis.y),
                Buttons = buttons,
            };

            if (DelayEnabled)
            {
                float due = NextDue(_debugDelayMs, _debugJitterMs, ref _lastSendDue);
                _delayedSends.Add(new DelayedSend { DueTime = due, Tick = tick, MoveX = fi.MoveX, MoveY = fi.MoveY, Buttons = buttons });
            }
            else
            {
                SendServerRpc(nameof(CmdInput), tick, fi.MoveX, fi.MoveY, buttons);
            }
            _session.SetLocalInput(fi); // 本地自预测来源（输入封存：模拟仍以服务器确认版本为准）
        }

        // ---------------- 延迟队列与诊断窗口 ----------------

        private bool DelayEnabled => _debugDelayMs > 0 || _debugJitterMs > 0;

        /// <summary>计算下一条消息的截止时间：在基准上叠加抖动，且单调不减（保序）。</summary>
        private static float NextDue(int delayMs, int jitterMs, ref float lastDue)
        {
            float due = Time.time + delayMs / 1000f;
            if (jitterMs > 0) due += UnityEngine.Random.Range(-jitterMs, jitterMs) / 1000f;
            if (due < lastDue) due = lastDue;
            lastDue = due;
            return due;
        }

        private void EnqueueRecv(DelayedRecv msg)
        {
            msg.DueTime = NextDue(_debugDelayMs, _debugJitterMs, ref _lastRecvDue);
            _delayedRecvs.Add(msg);
        }

        private void FlushDelayedSends(float now)
        {
            for (int i = 0; i < _delayedSends.Count; i++)
            {
                DelayedSend m = _delayedSends[i];
                if (m.DueTime > now) continue;
                SendServerRpc(nameof(CmdInput), m.Tick, m.MoveX, m.MoveY, m.Buttons);
                _delayedSends.RemoveAt(i);
                i--;
            }
        }

        private void FlushDelayedRecvs(float now)
        {
            for (int i = 0; i < _delayedRecvs.Count; i++)
            {
                DelayedRecv m = _delayedRecvs[i];
                if (m.DueTime > now) continue;
                if (m.IsRemoval)
                {
                    _session.OnPlayerLeft(m.LeftClientId, m.EffectiveTick);
                    Debug.Log($"[FrameSync] 玩家 {m.LeftClientId} 将于 tick {m.EffectiveTick} 统一移除（冻结在最后状态）");
                }
                else
                {
                    _session.OnConfirmedInput(m.Input);
                }
                _delayedRecvs.RemoveAt(i);
                i--;
            }
        }

        /// <summary>诊断窗口：每秒输出一行回滚/预测统计（FrameSyncDebug 开启）。</summary>
        private void TickDiagWindow()
        {
            if (Time.time < _diagNextLine) return;
            if (Time.time >= _diagUntil) { _diagNextLine = float.MaxValue; return; }
            _diagNextLine += 1f;

            long total = _session.TotalInputCount - _diagTotalBase;
            long predicted = _session.PredictedInputCount - _diagPredictedBase;
            _diagTotalBase = _session.TotalInputCount;
            _diagPredictedBase = _session.PredictedInputCount;

            int lag = _session.SimTick - _session.ConfirmedWaterline;
            float pct = total > 0 ? predicted * 100f / total : 0f;
            Debug.Log($"[FrameSync][Diag] tick={_session.SimTick} 水位滞后={lag} " +
                      $"回滚={_diagRollbacks}次 最大深度={_diagMaxDepth} 预测占比={pct:0.0}%");
            _diagRollbacks = 0;
            _diagMaxDepth = 0;
        }

        /// <summary>把会话权威状态写回实体（仅驱动渲染插值；对象已销毁不影响逻辑）。</summary>
        private void PushStatesToEntities()
        {
            _session.CopyStates(_renderStates);
            for (int i = 0; i < _rosterEntities.Length; i++)
                if (_rosterEntities[i] != null)
                    _rosterEntities[i].ApplyTickResult(_renderStates[i]);
        }

        /// <summary>上报确认水位线内的状态哈希（预测 tick 各端天然不同，不上报）。</summary>
        private void ReportConfirmedHashes()
        {
            while (_nextHashReport <= _session.SimTick)
            {
                if (!_session.TryGetStateHash(_nextHashReport, out int hash))
                    break; // 水位线未到，等待确认
                SendServerRpc(nameof(CmdStateHash), _nextHashReport, hash);
                _nextHashReport += FrameSyncConfig.HashIntervalTicks;
            }
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

            int count = _roster.Length;
            _rosterEntities = new FrameSyncMovement[count];
            _renderStates = new PlayerSimState[count];

            var initialStates = new PlayerSimState[count];
            for (int i = 0; i < count; i++)
            {
                initialStates[i] = new PlayerSimState
                {
                    Position = new FPVec3(
                        FP.FromRaw(_pendingSpawnRaw[i * 3]),
                        FP.FromRaw(_pendingSpawnRaw[i * 3 + 1]),
                        FP.FromRaw(_pendingSpawnRaw[i * 3 + 2])),
                    Facing = new FPVec2(FP.Zero, FP.One),
                    VelY = FP.Zero,
                    Grounded = true,
                };

                FrameSyncMovement e = FindEntity(_roster[i]);
                e.InitSimState(initialStates[i]);   // 渲染载体初始姿态
                _rosterEntities[i] = e;
            }

            _session.Begin(_roster, Net.LocalClientId, initialStates, FrameSyncConfig.InputDelayTicks);

            _localInputTick = 0;
            _simAccumulator = 0f;
            _inputAccumulator = 0f;
            _pendingButtons = 0;
            _stallLogged = false;
            _nextHashReport = FrameSyncConfig.HashIntervalTicks;
            _pendingStart = false;
            IsRunning = true;
            Debug.Log($"[FrameSync] 模拟开始（乐观预测）: {_roster.Length} 名玩家, seed={_seed}");
        }

        private void ResetLocal()
        {
            IsRunning = false;
            _pendingStart = false;
            _roster = new int[0];
            _pendingSpawnRaw = new int[0];
            _session.End();
            _localInputTick = 0;
            _simAccumulator = 0f;
            _inputAccumulator = 0f;
            _pendingButtons = 0;
            _stallLogged = false;
            _nextHashReport = 0;
            _delayedSends.Clear();
            _delayedRecvs.Clear();
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
