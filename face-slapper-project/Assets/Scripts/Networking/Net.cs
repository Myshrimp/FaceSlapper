using System;
using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Networking
{
    /// <summary>
    /// 网络门面：游戏代码访问联机能力的唯一入口。
    /// ================================================================
    /// ★ 更换整套联机方案（FishNet → Mirror / Netcode for GameObjects）
    ///   只需修改下面 CreateBackend() 中的【一行】，并实现对应 INetBackend。
    /// ================================================================
    /// </summary>
    public static class Net
    {
        // ★★★ 切换联机后端只需修改这一行 ★★★
        private static INetBackend CreateBackend() => new FishNetImpl.FishNetBackend();
        // private static INetBackend CreateBackend() => new MirrorImpl.MirrorBackend();      // 未来：Mirror
        // private static INetBackend CreateBackend() => new NetcodeImpl.NetcodeBackend();    // 未来：Netcode for GameObjects

        /// <summary>当前联机后端。</summary>
        public static INetBackend Backend { get; } = CreateBackend();

        // ---------------- 状态快捷访问 ----------------

        public static bool IsServer => Backend.IsServer;
        public static bool IsClient => Backend.IsClient;
        public static bool IsHost => Backend.IsHost;
        public static int LocalClientId => Backend.LocalClientId;

        // ---------------- 服务器 API ----------------

        public static class Server
        {
            /// <summary>生成网络对象（ownerClientId 小于 0 = 服务器权威）。</summary>
            public static NetObject Spawn(NetObject prefab, Vector3 position, Quaternion rotation, int ownerClientId = -1)
                => Backend.Spawn(prefab, position, rotation, ownerClientId);

            public static void Despawn(NetObject obj) => Backend.Despawn(obj);

            public static bool TryGetObject(int netId, out NetObject obj) => Backend.TryGetObjectServer(netId, out obj);

            /// <summary>当前所有已连接客户端 Id。</summary>
            public static IReadOnlyCollection<int> ClientIds => Backend.ConnectedClientIds;

            public static void GiveOwnership(NetObject obj, int clientId) => Backend.GiveOwnership(obj, clientId);

            public static void RemoveOwnership(NetObject obj) => Backend.RemoveOwnership(obj);
        }

        // ---------------- 客户端 API ----------------

        public static class Client
        {
            public static bool TryGetObject(int netId, out NetObject obj) => Backend.TryGetObjectClient(netId, out obj);

            /// <summary>本机玩家对象（可能为 null）。</summary>
            public static NetObject LocalPlayer => Backend.LocalPlayer;
        }

        // ---------------- 事件转发 ----------------

        public static event Action<int> OnRemoteClientConnected
        {
            add => Backend.OnRemoteClientConnected += value;
            remove => Backend.OnRemoteClientConnected -= value;
        }

        public static event Action<int> OnRemoteClientDisconnected
        {
            add => Backend.OnRemoteClientDisconnected += value;
            remove => Backend.OnRemoteClientDisconnected -= value;
        }

        public static event Action OnServerStarted
        {
            add => Backend.OnServerStarted += value;
            remove => Backend.OnServerStarted -= value;
        }

        public static event Action OnServerStopped
        {
            add => Backend.OnServerStopped += value;
            remove => Backend.OnServerStopped -= value;
        }

        public static event Action OnClientStarted
        {
            add => Backend.OnClientStarted += value;
            remove => Backend.OnClientStarted -= value;
        }

        public static event Action OnClientStopped
        {
            add => Backend.OnClientStopped += value;
            remove => Backend.OnClientStopped -= value;
        }

        public static event Action OnPreTick
        {
            add => Backend.OnPreTick += value;
            remove => Backend.OnPreTick -= value;
        }

        public static event Action OnTick
        {
            add => Backend.OnTick += value;
            remove => Backend.OnTick -= value;
        }

        public static event Action OnPostTick
        {
            add => Backend.OnPostTick += value;
            remove => Backend.OnPostTick -= value;
        }
    }
}
