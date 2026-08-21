using System.Collections.Generic;

namespace FaceSlapper.FrameSync
{
    /// <summary>
    /// 帧同步协议层（纯逻辑，与 Unity/网络库解耦，离线可测）。两条核心规则：
    ///
    /// 【输入封存】客户端只消费"服务器广播回来的确认输入"，本地采样不直接进入模拟；
    /// 服务器对每个玩家的输入严格校验：tick 必须严格连续（首提优先、重复/跳号拒绝）、
    /// 掉线玩家拒收。可靠有序通道 + 该校验 ⇒ 各端持有的确认输入流完全一致。
    ///
    /// 【成员移除按生效 tick】掉线不移除立即生效，而是携带统一生效 tick
    /// （= 服务器为该玩家转发的最后一条输入的下一 tick）。服务器→客户端为可靠有序通道，
    /// 该 tick 之前的输入必然先于移除消息到达各端；各端在同一 tick 边界移除，
    /// 与各自的推进进度无关。
    /// </summary>
    public class FrameSyncProtocol
    {
        // ---------------- 客户端侧状态 ----------------

        private int[] _roster = new int[0];
        private readonly HashSet<int> _active = new HashSet<int>();
        private readonly Dictionary<int, Dictionary<int, FrameInput>> _confirmed =
            new Dictionary<int, Dictionary<int, FrameInput>>(8);
        private readonly Dictionary<int, int> _pendingRemovalTicks = new Dictionary<int, int>(4);
        private readonly List<int> _removalScratch = new List<int>(4);
        private bool _inSession;

        // 乐观预测扩展：连续确认水位/预测模板/已生效移除记录。
        private readonly Dictionary<int, int> _contigConfirmed = new Dictionary<int, int>(8);
        private readonly Dictionary<int, FrameInput> _lastTemplate = new Dictionary<int, FrameInput>(8);
        private readonly Dictionary<int, int> _removedAtTick = new Dictionary<int, int>(4);
        private int _waterline = -1;

        // ---------------- 服务器侧状态 ----------------

        private readonly Dictionary<int, int> _lastRelayedTick = new Dictionary<int, int>(8);
        private readonly HashSet<int> _leftPlayers = new HashSet<int>();

        public int[] Roster => _roster;

        public bool IsActive(int clientId) => _active.Contains(clientId);

        /// <summary>
        /// 全员确认连续水位线：tick ≤ 水位线的输入对所有该 tick 的活跃玩家均已确认，
        /// 即水位线之前的模拟状态是权威的（不含预测）。
        /// </summary>
        public int ConfirmedWaterline => _waterline;

        // ==================== 客户端侧 ====================

        /// <summary>开局：初始化名单/活跃集合，并预填输入延迟窗口 [0, inputDelayTicks) 的空输入。</summary>
        public void BeginSession(int[] roster, int inputDelayTicks)
        {
            _roster = roster;
            _active.Clear();
            _confirmed.Clear();
            _pendingRemovalTicks.Clear();
            _contigConfirmed.Clear();
            _lastTemplate.Clear();
            _removedAtTick.Clear();
            foreach (int id in roster)
            {
                _active.Add(id);
                var buffer = new Dictionary<int, FrameInput>(256);
                for (int t = 0; t < inputDelayTicks; t++)
                    buffer[t] = FrameInput.Empty(t, id);
                _confirmed[id] = buffer;
                _contigConfirmed[id] = inputDelayTicks - 1;
            }
            _waterline = inputDelayTicks - 1;
            _inSession = true;
        }

        public void EndSession()
        {
            _inSession = false;
            _roster = new int[0];
            _active.Clear();
            _confirmed.Clear();
            _pendingRemovalTicks.Clear();
            _contigConfirmed.Clear();
            _lastTemplate.Clear();
            _removedAtTick.Clear();
            _waterline = -1;
        }

        /// <summary>登记一条服务器广播的确认输入（本地采样不允许直接进入模拟）。</summary>
        public void OnConfirmedInput(FrameInput fi)
        {
            if (!_inSession) return;
            if (!_confirmed.TryGetValue(fi.ClientId, out Dictionary<int, FrameInput> buffer)) return;
            buffer[fi.Tick] = fi;

            // 连续确认进度（可靠有序通道 + 服务器严格连续校验 ⇒ 无空洞，这里仍按通用方式推进）。
            int c = _contigConfirmed[fi.ClientId];
            while (buffer.ContainsKey(c + 1)) c++;
            _contigConfirmed[fi.ClientId] = c;

            // 预测模板：该玩家最大 tick 的确认输入（远端预测 = 重复最后确认输入）。
            if (!_lastTemplate.TryGetValue(fi.ClientId, out FrameInput tpl) || fi.Tick >= tpl.Tick)
                _lastTemplate[fi.ClientId] = fi;

            RecomputeWaterline();
        }

        /// <summary>登记成员移除，effectiveTick 起该玩家不再参与模拟（冻结在最后状态）。</summary>
        public void OnPlayerLeft(int clientId, int effectiveTick)
        {
            if (!_inSession || !_active.Contains(clientId)) return;
            _pendingRemovalTicks[clientId] = effectiveTick;
            RecomputeWaterline();
        }

        /// <summary>推进 tick 前调用：应用所有已生效的移除（各端在同一 tick 边界生效）。</summary>
        public void ApplyRemovals(int tick)
        {
            if (_pendingRemovalTicks.Count == 0) return;
            _removalScratch.Clear();
            foreach (KeyValuePair<int, int> kvp in _pendingRemovalTicks)
            {
                if (kvp.Value > tick) continue;
                _active.Remove(kvp.Key);
                _removedAtTick[kvp.Key] = kvp.Value;
                _removalScratch.Add(kvp.Key);
            }
            foreach (int id in _removalScratch)
                _pendingRemovalTicks.Remove(id);
            RecomputeWaterline();
        }

        /// <summary>所有活跃玩家在指定 tick 的确认输入是否齐备。</summary>
        public bool HasAllInputsFor(int tick)
        {
            for (int i = 0; i < _roster.Length; i++)
            {
                int id = _roster[i];
                if (!_active.Contains(id)) continue;
                if (!_confirmed.TryGetValue(id, out Dictionary<int, FrameInput> buffer)) return false;
                if (!buffer.ContainsKey(tick)) return false;
            }
            return true;
        }

        public FrameInput GetInput(int clientId, int tick) => _confirmed[clientId][tick];

        /// <summary>尝试读取确认输入（未确认返回 false）。</summary>
        public bool TryGetConfirmedInput(int clientId, int tick, out FrameInput fi)
        {
            fi = default;
            return _confirmed.TryGetValue(clientId, out Dictionary<int, FrameInput> buffer)
                   && buffer.TryGetValue(tick, out fi);
        }

        /// <summary>
        /// 远端预测输入：重复该玩家最后一条确认输入（tick 重写为目标 tick）；
        /// 该玩家尚无任何确认输入时返回静止输入。
        /// </summary>
        public FrameInput GetPrediction(int clientId, int tick)
        {
            if (_lastTemplate.TryGetValue(clientId, out FrameInput tpl))
                return new FrameInput
                {
                    Tick = tick,
                    ClientId = clientId,
                    MoveX = tpl.MoveX,
                    MoveY = tpl.MoveY,
                    Buttons = tpl.Buttons,
                };
            return FrameInput.Empty(tick, clientId);
        }

        /// <summary>该玩家在指定 tick 是否应参与模拟（含已登记/已生效的移除判定）。</summary>
        public bool WasActiveAt(int clientId, int tick)
        {
            if (!_inSession || !_confirmed.ContainsKey(clientId)) return false;
            if (_pendingRemovalTicks.TryGetValue(clientId, out int pending)) return pending > tick;
            if (_removedAtTick.TryGetValue(clientId, out int removed)) return removed > tick;
            return _active.Contains(clientId);
        }

        /// <summary>诊断用：该玩家已确认的最大 tick（无则 -1）。</summary>
        public int LatestConfirmedTick(int clientId)
        {
            if (!_contigConfirmed.TryGetValue(clientId, out int c)) return -1;
            return c;
        }

        /// <summary>推进全员确认连续水位线：所有"在下一 tick 活跃"的玩家都已确认该 tick 的输入。</summary>
        private void RecomputeWaterline()
        {
            if (!_inSession) return;
            int w = _waterline;
            while (true)
            {
                int next = w + 1;
                bool ok = true;
                for (int i = 0; i < _roster.Length; i++)
                {
                    int id = _roster[i];
                    if (!WasActiveAt(id, next)) continue;
                    if (!_contigConfirmed.TryGetValue(id, out int c) || c < next) { ok = false; break; }
                }
                if (!ok) break;
                w = next;
            }
            _waterline = w;
        }

        /// <summary>消费完成后清理该 tick 的输入缓存。</summary>
        public void ConsumeTick(int tick)
        {
            foreach (KeyValuePair<int, Dictionary<int, FrameInput>> kvp in _confirmed)
                kvp.Value.Remove(tick);
        }

        // ==================== 服务器侧 ====================

        /// <summary>开局：初始化转发校验基线（首条合法输入 tick = inputDelayTicks）。</summary>
        public void ServerInitRoster(int[] roster, int inputDelayTicks)
        {
            _lastRelayedTick.Clear();
            _leftPlayers.Clear();
            foreach (int id in roster)
                _lastRelayedTick[id] = inputDelayTicks - 1;
        }

        /// <summary>转发校验：严格连续（首提优先，重复/跳号拒绝）、非成员与掉线玩家拒收。</summary>
        public bool ServerTryRelay(int clientId, int tick)
        {
            if (_leftPlayers.Contains(clientId)) return false;
            if (!_lastRelayedTick.TryGetValue(clientId, out int last)) return false;
            if (tick != last + 1) return false;
            _lastRelayedTick[clientId] = tick;
            return true;
        }

        /// <summary>登记掉线，返回统一生效 tick（= 最后一条已转发输入的下一 tick）。</summary>
        public int ServerMarkLeft(int clientId)
        {
            _leftPlayers.Add(clientId);
            return _lastRelayedTick.TryGetValue(clientId, out int last) ? last + 1 : 0;
        }
    }
}
