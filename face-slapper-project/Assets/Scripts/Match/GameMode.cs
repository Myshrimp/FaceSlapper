using System.Collections.Generic;

namespace FaceSlapper.Match
{
    /// <summary>
    /// 游戏模式基类：只描述规则（计分、时长、胜负判定），
    /// 对局生命周期（状态机/同步/玩家注册）由 MatchComponent 统一管理。
    /// 新增模式：继承本类并注册到 GameModes，无需改动对局管理器。
    /// </summary>
    public abstract class GameModeBase
    {
        /// <summary>模式唯一 Id（用于网络同步与 GM 命令）。</summary>
        public abstract string ModeId { get; }

        /// <summary>显示名。</summary>
        public abstract string DisplayName { get; }

        /// <summary>对局时长（秒），0 = 不限时。</summary>
        public virtual float Duration => 0f;

        /// <summary>胜利所需分数，0 = 不以分数直接获胜（如计时赛）。</summary>
        public virtual int TargetScore => 0;

        /// <summary>每次有效命中获得的分数。</summary>
        public virtual int ScorePerHit => 1;

        /// <summary>
        /// 服务器判定当前领先者（对局结束时调用）。
        /// 返回胜者 ClientId，&lt; 0 表示平局/无法判定。
        /// </summary>
        public virtual int EvaluateWinner(IEnumerable<MatchPlayerInfo> players)
        {
            int winner = -1;
            int best = int.MinValue;
            bool tie = false;

            foreach (MatchPlayerInfo player in players)
            {
                if (player.Score > best)
                {
                    best = player.Score;
                    winner = player.ClientId;
                    tie = false;
                }
                else if (player.Score == best)
                {
                    tie = true;
                }
            }
            return tie ? -1 : winner;
        }

        public override string ToString() => $"{DisplayName}({ModeId})";
    }

    /// <summary>抢分赛：率先达到目标分数者获胜。</summary>
    public class ScoreRaceMode : GameModeBase
    {
        public override string ModeId => "score_race";
        public override string DisplayName => "抢分赛";
        public override int TargetScore => 10;
    }

    /// <summary>计时赛：时间结束时分数最高者获胜。</summary>
    public class TimedScoreMode : GameModeBase
    {
        public override string ModeId => "timed";
        public override string DisplayName => "计时赛";
        public override float Duration => 120f;
    }

    /// <summary>模式注册表（纯本地，各端一致）。</summary>
    public static class GameModes
    {
        private static readonly Dictionary<string, GameModeBase> ById = new Dictionary<string, GameModeBase>();

        static GameModes()
        {
            Register(new ScoreRaceMode());
            Register(new TimedScoreMode());
        }

        public static void Register(GameModeBase mode) => ById[mode.ModeId] = mode;

        public static GameModeBase Get(string modeId)
        {
            return !string.IsNullOrEmpty(modeId) && ById.TryGetValue(modeId, out GameModeBase mode) ? mode : null;
        }

        public static IEnumerable<GameModeBase> All => ById.Values;
    }
}
