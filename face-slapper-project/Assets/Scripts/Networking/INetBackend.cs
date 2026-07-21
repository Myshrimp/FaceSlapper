using System;
using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Networking
{
    /// <summary>
    /// 联机后端接口：对底层网络库（FishNet / Mirror / Netcode for GameObjects）的完整抽象。
    /// 游戏代码只允许通过 <see cref="Net"/> 门面访问本接口，不允许直接引用任何底层网络库类型。
    /// 更换联机方案 = 实现一个新的 INetBackend + 修改 Net.cs 中的一行工厂代码。
    /// </summary>
    public interface INetBackend
    {
        // ---------------- 状态 ----------------

        /// <summary>服务器是否已启动（Host 模式下也为 true）。</summary>
        bool IsServer { get; }

        /// <summary>客户端是否已启动（Host 模式下也为 true）。</summary>
        bool IsClient { get; }

        /// <summary>是否为 Host（同时是服务器与客户端）。</summary>
        bool IsHost { get; }

        /// <summary>本机连接的 ClientId，未连接时为 -1。</summary>
        int LocalClientId { get; }

        // ---------------- 生命周期 ----------------

        /// <summary>初始化后端（GameEntry 启动时调用一次）。</summary>
        void Initialize();

        /// <summary>关闭后端。</summary>
        void Shutdown();

        // ---------------- 连接 ----------------

        /// <summary>启动主机（服务器 + 本地客户端）。</summary>
        bool StartHost(ushort port);

        /// <summary>启动纯客户端并连接指定地址。</summary>
        bool StartClient(string address, ushort port);

        /// <summary>停止服务器与客户端。</summary>
        void Stop();

        // ---------------- 对象生成/销毁（仅服务器） ----------------

        /// <summary>生成一个网络对象。ownerClientId 小于 0 表示无所有者（服务器权威）。</summary>
        NetObject Spawn(NetObject prefab, Vector3 position, Quaternion rotation, int ownerClientId = -1);

        /// <summary>销毁一个网络对象。</summary>
        void Despawn(NetObject obj);

        // ---------------- 对象查找 ----------------

        bool TryGetObjectServer(int netId, out NetObject obj);

        bool TryGetObjectClient(int netId, out NetObject obj);

        /// <summary>本机客户端拥有的玩家对象（可能为 null）。</summary>
        NetObject LocalPlayer { get; }

        /// <summary>服务器端：当前所有已连接客户端的 Id 集合。</summary>
        IReadOnlyCollection<int> ConnectedClientIds { get; }

        // ---------------- 所有权（仅服务器） ----------------

        void GiveOwnership(NetObject obj, int clientId);

        void RemoveOwnership(NetObject obj);

        // ---------------- 事件 ----------------

        /// <summary>服务器端：远程客户端连接/断开。</summary>
        event Action<int> OnRemoteClientConnected;
        event Action<int> OnRemoteClientDisconnected;

        event Action OnServerStarted;
        event Action OnServerStopped;
        event Action OnClientStarted;
        event Action OnClientStopped;

        /// <summary>网络 Tick 事件（对齐底层网络库的模拟刻）。</summary>
        event Action OnPreTick;
        event Action OnTick;
        event Action OnPostTick;

        // ---------------- 场景 ----------------

        /// <summary>以网络同步方式加载全局场景（仅服务器）。</summary>
        void LoadGlobalScene(string sceneName);

        // ---------------- 编辑器集成（搭建工具用） ----------------
#if UNITY_EDITOR
        /// <summary>确保 NetworkManager 等后端管理物体存在于场景中。</summary>
        void EditorEnsureManagerInScene(ushort port);

        /// <summary>给 Prefab 根物体添加后端所需的网络组件（如 NetworkObject + 桥接组件）。</summary>
        void EditorEnsurePrefabComponents(GameObject prefabRoot);

        /// <summary>把 Prefab 注册为可网络生成。</summary>
        void EditorRegisterSpawnablePrefab(GameObject prefabRoot);
#endif
    }
}
