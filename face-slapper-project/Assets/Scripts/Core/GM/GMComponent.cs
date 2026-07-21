using FaceSlapper.Room;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// GM 功能库：命令行 /gm func MethodName(...) 通过反射调用本组件内的同名 public 方法。
    /// 新增 GM 功能时直接在这里加 public 方法即可（参数支持 int/float/bool/string）。
    /// </summary>
    public class GMComponent : MonoBehaviour, IGameComponent
    {
        private RoomHandler Room => GameManager.Instance.Get<RoomHandler>();

        public void OnInit() { }

        public void OnShutdown() { }

        /// <summary>开启局域网主机（同时作为服务器与客户端）。</summary>
        public string Host()
        {
            bool ok = Room.Host();
            return ok ? "主机已启动 (Host = Server + Client)" : "启动失败，请检查场景中 NetworkManager";
        }

        /// <summary>加入局域网房间。用法: Join 192.168.1.100</summary>
        public string Join(string ip)
        {
            bool ok = Room.Join(ip);
            return ok ? $"正在连接 {ip} ..." : "连接发起失败";
        }

        public string Join()
        {
            return Join("127.0.0.1");
        }

        /// <summary>停止服务器/客户端。</summary>
        public string Stop()
        {
            Room.Stop();
            return "已停止网络连接";
        }

        /// <summary>开始游戏：服务器为所有已连接玩家生成角色（需要 Host 权限）。</summary>
        public string StartGame()
        {
            return Room.StartGame() ? "开始游戏指令已发送" : "失败：未连接网络或找不到 RoomComponent";
        }

        /// <summary>在指定位置生成一把武器。用法: SpawnWeapon 2 3</summary>
        public string SpawnWeapon(float x, float z)
        {
            return Room.SpawnWeapon(new Vector3(x, 1f, z)) ? $"已在 ({x}, {z}) 生成武器" : "失败：未连接网络";
        }

        /// <summary>打印房间内所有玩家。</summary>
        public string ListPlayers()
        {
            return Room.ListPlayers();
        }

        /// <summary>设置玩家队伍。用法: SetTeam 0 1</summary>
        public string SetTeam(int clientId, int teamId)
        {
            return Room.SetTeam(clientId, teamId) ? $"已请求设置玩家 {clientId} 为队伍 {teamId}" : "失败：未连接网络";
        }

        /// <summary>写一条日志。用法: Log hello</summary>
        public string Log(string msg)
        {
            Debug.Log($"[GM] {msg}");
            return $"已记录: {msg}";
        }
    }
}
