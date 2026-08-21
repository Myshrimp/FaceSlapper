using System;
using System.Collections.Generic;

namespace FaceSlapper.FrameSync
{
    /// <summary>
    /// 乐观帧同步客户端会话（纯逻辑，与 Unity/网络库解耦，离线可测）：预测推进 + 回滚重放。
    ///
    /// 核心规则：
    ///   1. 确认输入（服务器广播）优先；缺失时预测——本地玩家用"已上行的本地采样"
    ///     （必然自洽，除非服务器拒收），远端玩家重复其最后一条确认输入；
    ///   2. 确认输入到达且与已消费的预测不一致 → 回滚到最早错误 tick，用修正后的输入
    ///      重放到当前 tick（环形快照，容量 <see cref="HistoryTicks"/>）；
    ///   3. 预测超前确认水位线超过 <see cref="MaxPredictionTicks"/> 时停帧
    ///     （退化为保守 lockstep，不丢状态）；
    ///   4. 本地玩家输入尚未采样时停帧（等本地节拍，与网络无关，保证自预测零误差）。
    ///
    /// 每 tick 模拟顺序与离线自测一致：移动 → 碰撞 → 击飞 → 攻击。
    /// </summary>
    public class FrameSyncSession
    {
        /// <summary>快照/输入环形缓冲容量（tick，2 的幂）。</summary>
        public const int HistoryTicks = 32;

        /// <summary>允许预测超前确认水位线的最大 tick 数（约 1 秒），超出则停帧等待。</summary>
        public const int MaxPredictionTicks = 32;

        private const int Mask = HistoryTicks - 1;

        private readonly FrameSyncProtocol _proto = new FrameSyncProtocol();
        private int[] _roster = new int[0];
        private int _localId = -1;

        // 当前模拟状态（已模拟完 SimTick-1）。环形历史：slot = tick & Mask。
        private PlayerSimState[] _states = new PlayerSimState[0];
        private PlayerSimState[][] _snapshots = new PlayerSimState[HistoryTicks][]; // tick 开始前的状态
        private FrameInput[][] _inputs = new FrameInput[HistoryTicks][];            // 该 tick 实际消费的输入
        private bool[][] _predicted = new bool[HistoryTicks][];                     // 该输入是否为预测
        private readonly int[] _stateHashes = new int[HistoryTicks];                // 该 tick 模拟完成后的哈希

        private readonly Dictionary<int, FrameInput> _localSent = new Dictionary<int, FrameInput>(64);
        private readonly List<int> _pruneScratch = new List<int>(16);
        private bool[] _activeScratch = new bool[0];
        private int _rollbackTarget = -1;

        public bool InSession { get; private set; }

        /// <summary>已模拟的 tick 数（下一个待模拟的 tick 序号）。</summary>
        public int SimTick { get; private set; }

        /// <summary>全员确认连续水位线（水位线之前的状态权威、不含预测）。</summary>
        public int ConfirmedWaterline => _proto.ConfirmedWaterline;

        public int RollbackCount { get; private set; }
        public int MaxRollbackDepth { get; private set; }

        /// <summary>发生不可修复分歧（回滚目标/确认输入超出窗口）。</summary>
        public bool Desynced { get; private set; }

        /// <summary>上一次 TryAdvance 停帧原因（诊断用）。</summary>
        public string LastStallReason { get; private set; } = "";

        public Action<string> LogWarning;
        public Action<string> LogError;

        public int[] Roster => _roster;

        public bool IsActive(int clientId) => _proto.IsActive(clientId);

        public int LatestConfirmedTick(int clientId) => _proto.LatestConfirmedTick(clientId);

        // ---------------- 会话生命周期 ----------------

        /// <summary>开局：roster 升序、本地 clientId、各端一致的初始状态。</summary>
        public void Begin(int[] roster, int localClientId, PlayerSimState[] initialStates, int inputDelayTicks)
        {
            _roster = roster;
            _localId = localClientId;
            _proto.BeginSession(roster, inputDelayTicks);

            int n = roster.Length;
            _states = new PlayerSimState[n];
            Array.Copy(initialStates, _states, n);
            for (int i = 0; i < HistoryTicks; i++)
            {
                _snapshots[i] = new PlayerSimState[n];
                _inputs[i] = new FrameInput[n];
                _predicted[i] = new bool[n];
            }
            _activeScratch = new bool[n];
            _localSent.Clear();

            SimTick = 0;
            RollbackCount = 0;
            MaxRollbackDepth = 0;
            Desynced = false;
            LastStallReason = "";
            _rollbackTarget = -1;
            InSession = true;
        }

        public void End()
        {
            InSession = false;
            _proto.EndSession();
            _roster = new int[0];
            _states = new PlayerSimState[0];
            _localSent.Clear();
            SimTick = 0;
            _rollbackTarget = -1;
        }

        // ---------------- 输入登记 ----------------

        /// <summary>登记一条已上行的本地输入（本地自预测来源；tick 必须与上行 RPC 一致）。</summary>
        public void SetLocalInput(FrameInput fi)
        {
            if (InSession) _localSent[fi.Tick] = fi;
        }

        /// <summary>
        /// 登记服务器广播的确认输入。若该 tick 已被预测消费且与预测不一致，
        /// 修正历史并记录回滚目标（取最早错误 tick）。
        /// </summary>
        public void OnConfirmedInput(FrameInput fi)
        {
            if (!InSession) return;
            _proto.OnConfirmedInput(fi);

            if (fi.Tick >= SimTick) return; // 未来输入，推进时自然消费
            if (fi.Tick < SimTick - HistoryTicks)
            {
                Desynced = true;
                LogError?.Invoke($"[FrameSync] 确认输入 tick {fi.Tick} 超出回滚窗口（当前 {SimTick}），无法修复");
                return;
            }

            int idx = RosterIndex(fi.ClientId);
            if (idx < 0) return;
            int slot = fi.Tick & Mask;
            if (!_predicted[slot][idx]) return; // 该 tick 消费的就是确认输入

            if (InputsEqual(_inputs[slot][idx], fi))
            {
                _predicted[slot][idx] = false; // 预测正确，仅解除标记
                return;
            }
            _inputs[slot][idx] = fi;
            _predicted[slot][idx] = false;
            if (_rollbackTarget < 0 || fi.Tick < _rollbackTarget) _rollbackTarget = fi.Tick;
        }

        /// <summary>登记成员移除（统一生效 tick）。</summary>
        public void OnPlayerLeft(int clientId, int effectiveTick)
        {
            if (InSession) _proto.OnPlayerLeft(clientId, effectiveTick);
        }

        // ---------------- 推进与回滚 ----------------

        /// <summary>
        /// 尝试推进一个 tick。返回 false 且 stalled=true 表示本 tick 无法推进
        /// （预测窗口已满 / 本地输入未采样 / 会话不可用）。
        /// </summary>
        public bool TryAdvance(out bool stalled)
        {
            stalled = false;
            if (!InSession || Desynced)
            {
                stalled = true;
                LastStallReason = Desynced ? "检测到不可修复的不同步" : "会话未运行";
                return false;
            }

            // 先处理待回滚：确认输入修正历史后，即使窗口已满也要先修复权威状态。
            if (_rollbackTarget >= 0) DoRollback();

            _proto.ApplyRemovals(SimTick);

            if (SimTick > _proto.ConfirmedWaterline + MaxPredictionTicks)
            {
                stalled = true;
                LastStallReason = $"预测窗口已满（水位线 {_proto.ConfirmedWaterline}）";
                return false;
            }

            int n = _roster.Length;
            int slot = SimTick & Mask;
            Array.Copy(_states, _snapshots[slot], n);

            for (int i = 0; i < n; i++)
            {
                int id = _roster[i];
                bool active = _proto.IsActive(id);
                _activeScratch[i] = active;
                if (!active) continue;

                if (_proto.TryGetConfirmedInput(id, SimTick, out FrameInput confirmed))
                {
                    _inputs[slot][i] = confirmed;
                    _predicted[slot][i] = false;
                }
                else if (id == _localId)
                {
                    if (_localSent.TryGetValue(SimTick, out FrameInput local))
                    {
                        _inputs[slot][i] = local;
                        _predicted[slot][i] = true;
                    }
                    else
                    {
                        // 本地输入尚未采样：停帧等本地节拍（与网络无关，保证自预测零误差）。
                        stalled = true;
                        LastStallReason = "等待本地输入采样";
                        return false;
                    }
                }
                else
                {
                    _inputs[slot][i] = _proto.GetPrediction(id, SimTick);
                    _predicted[slot][i] = true;
                }
            }

            StepSim(slot);
            _proto.ConsumeTick(SimTick);
            PruneLocalSent();
            SimTick++;
            return true;
        }

        /// <summary>回滚到最早错误 tick 并用修正后的输入重放到当前 tick。</summary>
        private void DoRollback()
        {
            int rt = _rollbackTarget;
            _rollbackTarget = -1;
            if (rt >= SimTick) return;
            if (rt < SimTick - HistoryTicks)
            {
                Desynced = true;
                LogError?.Invoke($"[FrameSync] 回滚目标 tick {rt} 超出窗口（当前 {SimTick}），判定不同步");
                return;
            }

            int n = _roster.Length;
            Array.Copy(_snapshots[rt & Mask], _states, n);

            // 重放区间不跨移除生效边界（移除要求此前输入全部确认，mispredict 只会发生在其后），
            // 因此重放期间活跃集合与当前一致。
            for (int t = rt; t < SimTick; t++)
            {
                _proto.ApplyRemovals(t);
                int slot = t & Mask;
                Array.Copy(_states, _snapshots[slot], n);
                for (int i = 0; i < n; i++)
                    _activeScratch[i] = _proto.IsActive(_roster[i]);
                StepSim(slot);
            }

            RollbackCount++;
            int depth = SimTick - rt;
            if (depth > MaxRollbackDepth) MaxRollbackDepth = depth;
            LogWarning?.Invoke($"[FrameSync] 预测错误，回滚 {depth} tick（至 {rt}）并重放");
        }

        /// <summary>模拟一个 tick（输入/快照取自指定 slot），并记录完成后哈希。</summary>
        private void StepSim(int slot)
        {
            int n = _roster.Length;
            FrameSyncSim.SimulateAll(_states, _inputs[slot], _activeScratch, n);
            FrameSyncSim.ResolveCollisions(_states, _activeScratch, n);
            FrameSyncSim.ResolveHitback(_states, _inputs[slot], _activeScratch, n);
            FrameSyncSim.ResolveAttack(_states, _inputs[slot], _activeScratch, n);
            _stateHashes[slot] = ComputeHash();
        }

        // ---------------- 状态读取 ----------------

        /// <summary>拷贝当前模拟状态（渲染写回用）。</summary>
        public void CopyStates(PlayerSimState[] dst) => Array.Copy(_states, dst, _states.Length);

        /// <summary>
        /// 读取"模拟完 afterTick 个 tick 后"的权威状态哈希：
        /// 要求该 tick 不超窗口且不含预测（afterTick-1 ≤ 确认水位线）。
        /// </summary>
        public bool TryGetStateHash(int afterTick, out int hash)
        {
            hash = 0;
            if (afterTick <= 0 || afterTick > SimTick) return false;
            if (afterTick - 1 < SimTick - HistoryTicks) return false;
            if (afterTick - 1 > _proto.ConfirmedWaterline) return false;
            hash = _stateHashes[(afterTick - 1) & Mask];
            return true;
        }

        // ---------------- 内部 ----------------

        private int ComputeHash()
        {
            uint h = 2166136261u;
            for (int i = 0; i < _roster.Length; i++)
            {
                int id = _roster[i];
                if (!_proto.IsActive(id)) continue;
                h = FrameSyncSim.Mix(h, id);
                h = FrameSyncSim.MixState(h, _states[i]);
            }
            return (int)h;
        }

        private int RosterIndex(int clientId)
        {
            for (int i = 0; i < _roster.Length; i++)
                if (_roster[i] == clientId) return i;
            return -1;
        }

        private static bool InputsEqual(FrameInput a, FrameInput b)
            => a.MoveX == b.MoveX && a.MoveY == b.MoveY && a.Buttons == b.Buttons;

        private void PruneLocalSent()
        {
            _pruneScratch.Clear();
            foreach (KeyValuePair<int, FrameInput> kvp in _localSent)
                if (kvp.Key < SimTick - HistoryTicks) _pruneScratch.Add(kvp.Key);
            foreach (int k in _pruneScratch)
                _localSent.Remove(k);
        }
    }
}
