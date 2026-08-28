using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>本地玩家生成事件（相机、输入绑定等监听）。</summary>
    public struct LocalPlayerSpawnedEvent
    {
        public NetObject Player;
    }

    /// <summary>本地玩家被销毁事件。</summary>
    public struct LocalPlayerDespawnedEvent { }

    /// <summary>玩家被击中事件（表现层可监听）。</summary>
    public struct PlayerHitEvent
    {
        public int VictimNetId;
        public Vector3 Direction;
        public float Force;
    }

    /// <summary>玩家被撞入眩晕事件（重击飞行中撞障碍触发，表现层可监听）。</summary>
    public struct PlayerStunnedEvent
    {
        public int NetId;
        public float Duration;
    }

    /// <summary>命中表现事件（服务器广播全端，音效/粒子反馈用）。</summary>
    public struct PlayerHitFxEvent
    {
        public Vector3 Position;
        public Vector3 Direction;
        public float Force;
    }

    /// <summary>眩晕表现事件（服务器广播全端，音效/粒子反馈用）。</summary>
    public struct PlayerStunFxEvent
    {
        public Vector3 Position;
    }

    public struct PlayerDeathEvent
    {
        public int VictimNetId;
        public int KillerId;
    }

    public struct EnterModeEvent
    {
        public EnterModeEvent(string modeId, string name, int playerCount, int gameLength)
        {
            ModeId = modeId;
            DisplayName = name;
            PlayerCount = playerCount;
            GameLength = gameLength;
        }
        public string ModeId;
        public string DisplayName;
        public int PlayerCount;
        public int GameLength;
    }
}
