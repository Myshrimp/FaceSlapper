using System.IO;
using FaceSlapper.Networking;

namespace FaceSlapper.Room
{
    /// <summary>房间状态。</summary>
    public enum RoomState
    {
        /// <summary>大厅等待中。</summary>
        Lobby = 0,
        /// <summary>游戏进行中。</summary>
        Playing = 1,
    }

    /// <summary>房间内的玩家信息（服务器权威，NetList 同步）。</summary>
    [System.Serializable]
    public struct RoomPlayerInfo : INetSerializable
    {
        public int ClientId;
        public string PlayerName;
        public int TeamId;
        public bool IsReady;

        public void Write(BinaryWriter writer)
        {
            writer.Write(ClientId);
            writer.Write(PlayerName ?? string.Empty);
            writer.Write(TeamId);
            writer.Write(IsReady);
        }

        public void Read(BinaryReader reader)
        {
            ClientId = reader.ReadInt32();
            PlayerName = reader.ReadString();
            TeamId = reader.ReadInt32();
            IsReady = reader.ReadBoolean();
        }

        public override string ToString() =>
            $"[Client {ClientId}] {PlayerName} Team={TeamId} Ready={IsReady}";
    }
}
