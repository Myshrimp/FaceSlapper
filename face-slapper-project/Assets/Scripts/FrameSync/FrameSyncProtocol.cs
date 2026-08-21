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

        // ---------------- 服务器侧状态 ----------------

        private readonly Dictionary<int, int> _lastRelayedTick = new Dictionary<int, int>(8);
        private readonly HashSet<int> _leftPlayers = new HashSet<int>();

        public int[] Roster => _roster;

        public bool IsActive(int clientId) => _active.Contains(clientId);

        // ==================== 客户端侧 ====================

        /// <summary>开局：初始化名单/活跃集合，并预填输入延迟窗口 [0, inputDelayTicks) 的空输入。</summary>
        public void BeginSession(int[] roster, int inputDelayTicks)
        {
            _roster = roster;
            _active.Clear();
            _confirmed.Clear();
            _pendingRemovalTicks.Clear();
            foreach (int id in roster)
            {
                _active.Add(id);
                var buffer = new Dictionary<int, FrameInput>(256);
                for (int t = 0; t < inputDelayTicks; t++)
                    buffer[t] = FrameInput.Empty(t, id);
                _confirmed[id] = buffer;
            }
            _inSession = true;
        }

        public void EndSession()
        {
            _inSession = false;
            _roster = new int[0];
            _active.Clear();
            _confirmed.Clear();
            _pendingRemovalTicks.Clear();
        }

        /// <summary>登记一条服务器广播的确认输入（本地采样不允许直接进入模拟）。</summary>
        public void OnConfirmedInput(FrameInput fi)
        {
            if (!_inSession) return;
            if (_confirmed.TryGetValue(fi.ClientId, out Dictionary<int, FrameInput> buffer))
                buffer[fi.Tick] = fi;
        }

        /// <summary>登记成员移除，effectiveTick 起该玩家不再参与模拟（冻结在最后状态）。</summary>
        public void OnPlayerLeft(int clientId, int effectiveTick)
        {
            if (!_inSession || !_active.Contains(clientId)) return;
            _pendingRemovalTicks[clientId] = effectiveTick;
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
                _removalScratch.Add(kvp.Key);
            }
            foreach (int id in _removalScratch)
                _pendingRemovalTicks.Remove(id);
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

        /// <summary>消费完成后清理该 tick 的输入缓存。</summary>
        public void ConsumeTick(int tick)
        {
            foreach (KeyValuePair<int, Dictionary<int, FrameInput>> kvp in _confirmed)
                kvp.Value.Remove(tick);
        }

        /// <summary>诊断用：该玩家已确认的最大 tick（无则 -1）。</summary>
        public int LatestConfirmedTick(int clientId)
        {
            if (!_confirmed.TryGetValue(clientId, out Dictionary<int, FrameInput> buffer)) return -1;
            int max = -1;
            foreach (int t in buffer.Keys) if (t > max) max = t;
            return max;
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
