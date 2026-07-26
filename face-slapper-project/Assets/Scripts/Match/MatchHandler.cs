using System.Text;
using FaceSlapper.Core;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Match
{
    /// <summary>
    /// 对局流程封装（普通游戏组件）：把 GM/UI 的调用转发给场景内的 MatchComponent。
    /// 与 RoomHandler 分层一致：本类是本地入口，MatchComponent 是网络权威。
    /// </summary>
    public class MatchHandler : MonoBehaviour, IGameComponent
    {
        private MatchComponent _match;

        /// <summary>场景中的 MatchComponent（懒查找）。</summary>
        public MatchComponent Match
        {
            get
            {
                if (_match == null) _match = FindObjectOfType<MatchComponent>();
                return _match;
            }
        }

        public void OnInit() { }

        public void OnShutdown() { }

        private bool MatchReady(out MatchComponent match)
        {
            match = Match;
            if (match == null || match.NetObject == null || !match.NetObject.IsSpawned || !Net.IsClient)
            {
                Debug.LogWarning("[MatchHandler] 对局管理器不可用。请先 Host/Join。");
                return false;
            }
            return true;
        }

        /// <summary>选择模式（可选模式见 ListModes）。</summary>
        public bool SelectMode(string modeId)
        {
            if (!MatchReady(out MatchComponent match)) return false;
            match.RequestSelectMode(modeId);
            return true;
        }

        /// <summary>以默认名字注册本机玩家。</summary>
        public bool Register() => Register($"Player{Net.LocalClientId}", 0);

        /// <summary>注册本机玩家信息（需已选择模式）。</summary>
        public bool Register(string playerName, int teamId)
        {
            if (!MatchReady(out MatchComponent match)) return false;
            match.RequestRegister(playerName, teamId);
            return true;
        }

        /// <summary>注销本机玩家。</summary>
        public bool Unregister()
        {
            if (!MatchReady(out MatchComponent match)) return false;
            match.RequestUnregister();
            return true;
        }

        /// <summary>开始对局。</summary>
        public bool StartMatch()
        {
            if (!MatchReady(out MatchComponent match)) return false;
            match.RequestStartMatch();
            return true;
        }

        /// <summary>结束对局（按当前比分结算）。</summary>
        public bool EndMatch()
        {
            if (!MatchReady(out MatchComponent match)) return false;
            match.RequestEndMatch();
            return true;
        }

        /// <summary>打印并返回当前对局信息。</summary>
        public string MatchInfo()
        {
            MatchComponent match = Match;
            if (match == null) return "找不到 MatchComponent";

            var sb = new StringBuilder($"对局状态 {match.State}，模式 {(match.Mode != null ? match.Mode.ToString() : "未选择")}");
            if (match.Mode != null && match.Mode.Duration > 0f && match.State == MatchState.Playing)
                sb.Append($"，剩余 {match.RemainingTime:F0}s");
            sb.Append($"，玩家 {match.Players.Count} 人:");
            for (int i = 0; i < match.Players.Count; i++)
                sb.Append("\n  ").Append(match.Players[i]);
            if (match.State == MatchState.Ended)
                sb.Append($"\n胜者: {(match.WinnerClientId >= 0 ? match.WinnerClientId.ToString() : "平局")}");

            string result = sb.ToString();
            Debug.Log(result);
            return result;
        }

        /// <summary>打印并返回所有可选模式。</summary>
        public string ListModes()
        {
            var sb = new StringBuilder("可选模式:");
            foreach (GameModeBase mode in GameModes.All)
            {
                sb.Append($"\n  {mode.ModeId} - {mode.DisplayName}");
                if (mode.TargetScore > 0) sb.Append($"（{mode.TargetScore} 分获胜）");
                if (mode.Duration > 0f) sb.Append($"（限时 {mode.Duration:F0}s）");
            }

            string result = sb.ToString();
            Debug.Log(result);
            return result;
        }
    }
}
