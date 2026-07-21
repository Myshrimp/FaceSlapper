using System.Text;
using FaceSlapper.Core;
using FaceSlapper.Network;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Room
{
    /// <summary>
    /// 房间流程封装（普通游戏组件）：把 GM/其他系统的调用转发给
    /// 网络层（NetworkComponent）与场景内的 RoomComponent。
    /// 局域网优先，互联网联机后续在此扩展。
    /// </summary>
    public class RoomHandler : MonoBehaviour, IGameComponent
    {
        private RoomComponent _room;

        /// <summary>场景中的 RoomComponent（懒查找）。</summary>
        public RoomComponent Room
        {
            get
            {
                if (_room == null) _room = FindObjectOfType<RoomComponent>();
                return _room;
            }
        }

        private NetworkComponent Net0 => GameManager.Instance.Get<NetworkComponent>();

        public void OnInit() { }

        public void OnShutdown() { }

        /// <summary>开启局域网主机。</summary>
        public bool Host() => Net0.StartHost();

        /// <summary>加入局域网房间。</summary>
        public bool Join(string ip) => Net0.StartClient(ip);

        /// <summary>停止网络。</summary>
        public void Stop() => Net0.Stop();

        /// <summary>房间网络对象是否可用（已连接且已生成）。</summary>
        private bool RoomReady(out RoomComponent room)
        {
            room = Room;
            if (room == null || room.NetObject == null || !room.NetObject.IsSpawned || !Net.IsClient)
            {
                Debug.LogWarning("[RoomHandler] 未连接网络。请先 Host/Join。");
                return false;
            }
            return true;
        }

        /// <summary>请求开始游戏（需已连接）。</summary>
        public bool StartGame()
        {
            if (!RoomReady(out RoomComponent room)) return false;
            room.RequestStartGame();
            return true;
        }

        /// <summary>请求在指定位置生成武器。</summary>
        public bool SpawnWeapon(Vector3 position)
        {
            if (!RoomReady(out RoomComponent room)) return false;
            room.RequestSpawnWeapon(position);
            return true;
        }

        /// <summary>请求设置队伍。</summary>
        public bool SetTeam(int clientId, int teamId)
        {
            if (!RoomReady(out RoomComponent room)) return false;
            room.RequestSetTeam(clientId, teamId);
            return true;
        }

        /// <summary>打印并返回房间玩家列表。</summary>
        public string ListPlayers()
        {
            RoomComponent room = Room;
            if (room == null) return "找不到 RoomComponent";

            var sb = new StringBuilder($"房间状态 {room.State}，玩家 {room.Players.Count} 人:");
            for (int i = 0; i < room.Players.Count; i++)
                sb.Append("\n  ").Append(room.Players[i]);

            string result = sb.ToString();
            Debug.Log(result);
            return result;
        }
    }
}
