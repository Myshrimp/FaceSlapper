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

        public bool VoteFor(string modeId)
        {
            Match.RequestVoteForMode(modeId);
            return true;
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
