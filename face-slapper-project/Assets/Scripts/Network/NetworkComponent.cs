using FaceSlapper.Core;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Network
{
    /// <summary>
    /// 网络组件：对 <see cref="Net"/> 门面的高层封装，提供 Host/Join/Stop。
    /// 不感知任何底层网络库——后端切换对本组件零影响。
    /// </summary>
    public class NetworkComponent : MonoBehaviour, IGameComponent
    {
        [SerializeField] private ushort _port = 7770;

        public bool IsHost => Net.IsHost;
        public bool IsServer => Net.IsServer;
        public bool IsClient => Net.IsClient;

        public ushort Port
        {
            get => _port;
            set => _port = value;
        }

        public void OnInit() => Net.Backend.Initialize();

        public void OnShutdown() => Net.Backend.Shutdown();

        /// <summary>启动主机：服务器 + 本地客户端。</summary>
        public bool StartHost()
        {
            bool ok = Net.Backend.StartHost(_port);
            if (ok) Debug.Log($"[NetworkComponent] 主机已启动，端口 {_port}");
            return ok;
        }

        /// <summary>启动纯客户端并连接到指定地址。</summary>
        public bool StartClient(string address)
        {
            bool ok = Net.Backend.StartClient(address, _port);
            Debug.Log($"[NetworkComponent] 客户端连接 {address}:{_port} -> {(ok ? "已发起" : "失败")}");
            return ok;
        }

        /// <summary>停止服务器与客户端。</summary>
        public void Stop() => Net.Backend.Stop();
    }
}
