using System.IO;
using FaceSlapper.Networking;

namespace FaceSlapper.Match
{
    /// <summary>对局状态。</summary>
    public enum MatchState
    {
        /// <summary>空闲：可选择模式、注册玩家。</summary>
        Idle = 0,
        /// <summary>对局进行中。</summary>
        Playing = 1,
        /// <summary>对局已结束（可查看结果，可再次开始）。</summary>
        Ended = 2,
    }

    /// <summary>对局内的玩家信息（服务器权威，NetList 同步）。</summary>
    [System.Serializable]
    public struct MatchPlayerInfo : INetSerializable
    {
        public int ClientId;
        public string PlayerName;
        public int TeamId;
        public int Score;

        public void Write(BinaryWriter writer)
        {
            writer.Write(ClientId);
            writer.Write(PlayerName ?? string.Empty);
            writer.Write(TeamId);
            writer.Write(Score);
        }

        public void Read(BinaryReader reader)
        {
            ClientId = reader.ReadInt32();
            PlayerName = reader.ReadString();
            TeamId = reader.ReadInt32();
            Score = reader.ReadInt32();
        }

        public override string ToString() =>
            $"[Client {ClientId}] {PlayerName} Team={TeamId} Score={Score}";
    }

    /// <summary>对局状态变化事件（各端均发布）。</summary>
    public struct MatchStateChangedEvent
    {
        public MatchState State;
        public string ModeId;
    }

    /// <summary>对局结束事件（各端均发布）。WinnerClientId &lt; 0 表示平局/无胜者。</summary>
    public struct MatchEndedEvent
    {
        public int WinnerClientId;
        public string WinnerName;
    }
}
