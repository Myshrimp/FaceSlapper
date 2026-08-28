using FaceSlapper.Core;
using FaceSlapper.Networking;
using FaceSlapper.Room;
using FaceSlapper.TL;
using System;
using System.Collections.Generic;
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
        public Dictionary<string, MatchModeHandler> ModeList;
        public Dictionary<string, int> ModeVotes;
        public Dictionary<int, int> PlayerScore;
        public int TotalVotes = 0;
        public MatchModeHandler CurrentMode;
        private readonly NetVar<MatchState> _state = new NetVar<MatchState>(MatchState.Idle);
        private RoomHandler _roomHandler => GameManager.Instance.Get<RoomHandler>();


        public MatchState State => _state.Value;

        protected override void Awake()
        {
            base.Awake();
            _state.OnChange += (prev, next) =>
            {
                EventBus.Publish(new MatchStateChangedEvent { State = next, ModeId = CurrentMode.ModeId });
                Debug.Log($"[Match] 对局状态: {prev} -> {next}（模式: {CurrentMode.ModeId}）");
            };

            ModeVotes = new Dictionary<string, int>();
            ModeList = new Dictionary<string, MatchModeHandler>();
            PlayerScore = new Dictionary<int, int>();

            ModeList["Default"] = new MatchModeBase();
            ModeList["DeathMatch"] = new DeathMatchMode();

            CurrentMode = ModeList["Default"];
        }

        public override void OnNetSpawnServer() => ServerInstance = this;

        public override void OnNetDespawnServer()
        {
            if (ServerInstance == this) ServerInstance = null;
        }

        private void Update()
        {
            if(CurrentMode.Started)
            {
                CurrentMode.OnUpdate();
            }
        }

        // ---------------- 客户端请求 ----------------

        public void RequestVoteForMode(string modeId) => SendServerRpc(nameof(CmdVoteForMode), modeId);
        /// <summary>请求选择模式（选择后需重新注册玩家信息）。</summary>
        public void RequestSelectMode(string modeId) => SendServerRpc(nameof(CmdSelectMode), modeId);

        // ---------------- 服务器 RPC ----------------
        [NetRpc]
        private void CmdVoteForMode(string modeId)
        {
            if(!ModeList.ContainsKey(modeId))
            {
                Debug.Log($"Not such mode {modeId}!");
            }
            AddVote(modeId);
            SendObserversRpc(nameof(NotifyPlayerVote), modeId);
            TotalVotes += 1;
            if(TotalVotes == _roomHandler.GetPlayersCount())
            {
                SelectMostVotedMode();
            }
            Debug.Log($"[Match] 已投票模式: {modeId}");
        }

        [NetRpc]
        private void NotifyPlayerVote(string modeId)
        {
            if(!IsServer)
                AddVote(modeId);
        }

        private void AddVote(string modeId)
        {
            if (!ModeVotes.ContainsKey(modeId))
                ModeVotes[modeId] = 0;
            ModeVotes[modeId] += 1;
        }

        [NetRpc]
        private void CmdSelectMode(string modeId)
        {
            if (_state.Value == MatchState.Playing) return;
            if (!ModeList.ContainsKey(modeId))
            {
                Debug.LogWarning($"[Match] 未知模式: {modeId}");
                return;
            }

            ModeList[modeId].OnEnterMode(this);
        }

        private void SelectMostVotedMode()
        {
            string resultMode = "Default";
            int curMaxVotes = 0;
            foreach(var mode in ModeList)
            {
                if (ModeVotes.ContainsKey(mode.Key) && ModeVotes[mode.Key] > curMaxVotes)
                {
                    curMaxVotes = ModeVotes[mode.Key];
                    resultMode = mode.Key;
                }
            }
            Debug.Log(string.Format("Selected most voted mode:%s with %d votes", resultMode, curMaxVotes));
            SelectMode(resultMode);
        }

        private void SelectMode(string modeId)
        {
            if (_state.Value == MatchState.Playing) return;
            if (!ModeList.ContainsKey(modeId))
            {
                Debug.LogWarning($"[Match] 未知模式: {modeId}");
                return;
            }
            CurrentMode = ModeList[modeId];
            CurrentMode.OnEnterMode(this);
            _state.Value = MatchState.Idle;
            Debug.Log($"[Match] 已选择模式: {modeId}");
        }

        public void ChangeState(MatchState state)
        {
            _state.Value = state;
        }

        public void ServerAddPlayerScore(int playerId, int score)
        {
            SendObserversRpc(nameof(NotifyAddPlayerScore), playerId, score);
        }

        public void ServerModeEnd()
        {
            CurrentMode.OnExitMode();
            TotalVotes = 0;
            foreach (var vote in ModeVotes)
            {
                ModeVotes[vote.Key] = 0;
            }
        }

        public void Attach2Timeline(MatchModeHandler mode)
        {
            TimelineManager.Instance.Ticked += mode.OnTick;
        }

        public void DetachTimeline(MatchModeHandler mode)
        {
            TimelineManager.Instance.Ticked -= mode.OnTick;
        }

        [NetRpc]
        private void NotifyAddPlayerScore(int playerId, int score)
        {
            if (!PlayerScore.ContainsKey(playerId))
            {
                PlayerScore[playerId] = 0;
            }
            PlayerScore[playerId] += score;
        }

        public int GetPlayerCount() => _roomHandler.GetPlayersCount();
    }

    public abstract class MatchModeHandler
    {
        public string ModeId;
        public MatchComponent MatchComp;
        public bool Started;
        public virtual void OnEnterMode(MatchComponent matchComp) { }
        public virtual void OnUpdate() { }
        public virtual void OnTick(int tick) { }
        public virtual void OnExitMode() { }
    }

    public class MatchModeBase : MatchModeHandler
    {
        protected int _gameLength; //单位：秒
        protected int _gameTicks;
        protected int _ticksRemaining;
        public override void OnEnterMode(MatchComponent matchComp)
        {
            MatchComp = matchComp;
            MatchComp.Attach2Timeline(this);
        }
        public override void OnExitMode()
        {
            MatchComp.DetachTimeline(this);
            MatchComp = null;
        }
    }

    public class DeathMatchMode : MatchModeBase 
    {
        public override void OnEnterMode(MatchComponent matchComp)
        {
            base.OnEnterMode(matchComp);
            _gameLength = 60;
            _gameTicks = GameSettings.Sec2Ticks(_gameLength);
            _ticksRemaining = _gameTicks;
            Started = true;
            EventBus.Subscribe<PlayerDeathEvent>(OnPlayerDeath);
            EventBus.Publish<EnterModeEvent>(new EnterModeEvent("DeathMatch", "DeathMatch", MatchComp.GetPlayerCount(), _gameLength));
            matchComp.ChangeState(MatchState.Playing);
            Debug.Log("DeathMatch begin");
        }

        public override void OnExitMode()
        {
            base.OnExitMode();
            Started = false;
            EventBus.Unsubscribe<PlayerDeathEvent>(OnPlayerDeath);
        }

        private void OnPlayerDeath(PlayerDeathEvent data)
        {
            MatchComp.ServerAddPlayerScore(data.KillerId, 1); 
        }

        public override void OnTick(int tick)
        {
            _ticksRemaining -= 1;
            if(_ticksRemaining == 0)
            {
                MatchComp.ServerModeEnd();
            }
        }
    }
}
