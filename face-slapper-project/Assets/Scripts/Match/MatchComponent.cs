using FaceSlapper.Core;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Match
{
    /// <summary>
    /// 对局管理器（场景网络对象，服务器权威）：
    /// 管理模式选择、玩家注册与对局生命周期（Idle → Playing → Ended）。
    /// 模式规则由 GameModeBase 描述，本组件只维护状态机与同步数据。
    /// 流程：选择模式 → 各玩家注册信息 → 开始 → 命中计分 → 达成胜利条件结束。
    /// </summary>
    public class MatchComponent : NetBehaviour
    {
        /// <summary>服务器端实例（命中计分链路使用，仅服务器有效）。</summary>
        public static MatchComponent ServerInstance { get; private set; }

        /// <summary>已注册的对局玩家（服务器写，全员同步）。</summary>
        public readonly NetList<MatchPlayerInfo> Players = new NetList<MatchPlayerInfo>();

        private readonly NetVar<string> _modeId = new NetVar<string>(string.Empty);
        private readonly NetVar<MatchState> _state = new NetVar<MatchState>(MatchState.Idle);
        private readonly NetVar<int> _winnerClientId = new NetVar<int>(-1);
        private readonly NetVar<float> _remainingTime = new NetVar<float>(0f);

        public MatchState State => _state.Value;
        public string ModeId => _modeId.Value;
        public GameModeBase Mode => GameModes.Get(_modeId.Value);
        public int WinnerClientId => _winnerClientId.Value;

        /// <summary>剩余时间（秒，限时模式有效，由服务器每秒同步一次）。</summary>
        public float RemainingTime => _remainingTime.Value;

        private float _endTime;
        private float _nextTimeSync;

        protected override void Awake()
        {
            base.Awake();
            _state.OnChange += (prev, next) =>
            {
                EventBus.Publish(new MatchStateChangedEvent { State = next, ModeId = _modeId.Value });
                Debug.Log($"[Match] 对局状态: {prev} -> {next}（模式: {_modeId.Value}）");
            };
            _winnerClientId.OnChange += (prev, next) =>
            {
                if (next < -1) return; // 初始值
                EventBus.Publish(new MatchEndedEvent { WinnerClientId = next, WinnerName = GetPlayerName(next) });
            };
        }

        public override void OnNetSpawnServer() => ServerInstance = this;

        public override void OnNetDespawnServer()
        {
            if (ServerInstance == this) ServerInstance = null;
        }

        // ---------------- 客户端请求 ----------------

        /// <summary>请求选择模式（选择后需重新注册玩家信息）。</summary>
        public void RequestSelectMode(string modeId) => SendServerRpc(nameof(CmdSelectMode), modeId);

        /// <summary>请求注册本机玩家信息（需已选择模式）。</summary>
        public void RequestRegister(string playerName, int teamId)
            => SendServerRpc(nameof(CmdRegister), Net.LocalClientId, playerName, teamId);

        /// <summary>请求注销本机玩家。</summary>
        public void RequestUnregister() => SendServerRpc(nameof(CmdUnregister), Net.LocalClientId);

        /// <summary>请求开始对局。</summary>
        public void RequestStartMatch() => SendServerRpc(nameof(CmdStartMatch));

        /// <summary>请求结束对局（按当前比分结算）。</summary>
        public void RequestEndMatch() => SendServerRpc(nameof(CmdEndMatch));

        // ---------------- 服务器 RPC ----------------

        [NetRpc]
        private void CmdSelectMode(string modeId)
        {
            if (_state.Value == MatchState.Playing) return;
            if (GameModes.Get(modeId) == null)
            {
                Debug.LogWarning($"[Match] 未知模式: {modeId}");
                return;
            }

            _modeId.Value = modeId;
            _winnerClientId.Value = -1;
            Players.Clear(); // 模式变更后需重新注册。
            _state.Value = MatchState.Idle;
            Debug.Log($"[Match] 已选择模式: {GameModes.Get(modeId)}");
        }

        [NetRpc]
        private void CmdRegister(int clientId, string playerName, int teamId)
        {
            if (_state.Value != MatchState.Idle || Mode == null) return;

            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId == clientId)
                {
                    MatchPlayerInfo existing = Players[i];
                    existing.PlayerName = playerName;
                    existing.TeamId = teamId;
                    Players[i] = existing;
                    return;
                }
            }

            Players.Add(new MatchPlayerInfo
            {
                ClientId = clientId,
                PlayerName = playerName,
                TeamId = teamId,
                Score = 0,
            });
            Debug.Log($"[Match] 玩家注册: Client {clientId} {playerName}（当前 {Players.Count} 人）");
        }

        [NetRpc]
        private void CmdUnregister(int clientId)
        {
            if (_state.Value == MatchState.Playing) return;
            for (int i = Players.Count - 1; i >= 0; i--)
            {
                if (Players[i].ClientId == clientId)
                    Players.RemoveAt(i);
            }
        }

        [NetRpc]
        private void CmdStartMatch()
        {
            if (_state.Value == MatchState.Playing) return;
            if (Mode == null)
            {
                Debug.LogWarning("[Match] 尚未选择模式，无法开始。");
                return;
            }
            if (Players.Count == 0)
            {
                Debug.LogWarning("[Match] 没有已注册的玩家，无法开始。");
                return;
            }

            for (int i = 0; i < Players.Count; i++)
            {
                MatchPlayerInfo info = Players[i];
                info.Score = 0;
                Players[i] = info;
            }

            _winnerClientId.Value = -1;
            _endTime = Time.time + Mode.Duration;
            _remainingTime.Value = Mode.Duration;
            _state.Value = MatchState.Playing;
            Debug.Log($"[Match] 对局开始: {Mode}，{Players.Count} 名玩家");
        }

        [NetRpc]
        private void CmdEndMatch()
        {
            if (_state.Value != MatchState.Playing) return;
            EndMatch(Mode != null ? Mode.EvaluateWinner(Players) : -1);
        }

        // ---------------- 服务器计分 ----------------

        /// <summary>
        /// 命中计分（仅服务器调用，由 NetworkIdentity 命中链路转发）。
        /// </summary>
        public void NotifyHitConfirmed(int attackerClientId, int victimClientId)
        {
            if (_state.Value != MatchState.Playing || Mode == null) return;
            if (attackerClientId == victimClientId) return;

            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId != attackerClientId) continue;

                MatchPlayerInfo info = Players[i];
                info.Score += Mode.ScorePerHit;
                Players[i] = info;

                if (Mode.TargetScore > 0 && info.Score >= Mode.TargetScore)
                    EndMatch(attackerClientId);
                return;
            }
        }

        private void EndMatch(int winnerClientId)
        {
            _state.Value = MatchState.Ended;
            _remainingTime.Value = 0f;
            _winnerClientId.Value = winnerClientId;
            Debug.Log($"[Match] 对局结束，胜者: {(winnerClientId >= 0 ? GetPlayerName(winnerClientId) : "平局")}");
        }

        private void Update()
        {
            // 限时模式的倒计时（仅服务器驱动，每秒同步一次）。
            if (!Net.IsServer || _state.Value != MatchState.Playing || Mode == null || Mode.Duration <= 0f)
                return;

            float remaining = _endTime - Time.time;
            if (remaining <= 0f)
            {
                EndMatch(Mode.EvaluateWinner(Players));
                return;
            }
            if (Time.time >= _nextTimeSync)
            {
                _nextTimeSync = Time.time + 1f;
                _remainingTime.Value = remaining;
            }
        }

        private string GetPlayerName(int clientId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId == clientId)
                    return Players[i].PlayerName;
            }
            return $"Client{clientId}";
        }
    }
}
