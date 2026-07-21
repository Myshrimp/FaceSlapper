using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace FaceSlapper.Networking.FishNetImpl
{
    /// <summary>
    /// FishNet 桥接组件：挂在每个网络 Prefab/场景对象上（由搭建工具自动添加），
    /// 把 FishNet 的网络回调与 RPC 通道转发给后端无关的 <see cref="NetObject"/>。
    /// 这是游戏对象上唯一允许出现的 FishNet NetworkBehaviour。
    /// </summary>
    [RequireComponent(typeof(NetObject))]
    public class FishNetNetObjectBridge : NetworkBehaviour, INetObjectBridge
    {
        private NetObject _netObject;

        public int NetId => NetworkObject != null && NetworkObject.IsSpawned ? NetworkObject.ObjectId : -1;

        public int OwnerClientId =>
            NetworkObject != null && NetworkObject.Owner != null && NetworkObject.Owner.IsValid
                ? NetworkObject.Owner.ClientId
                : -1;

        public bool IsOwner =>
            NetworkObject != null && NetworkObject.Owner != null && NetworkObject.Owner.IsLocalClient;

        private void Awake()
        {
            _netObject = GetComponent<NetObject>();
            _netObject.AttachBridge(this);
        }

        // ---------------- FishNet 生命周期 → NetObject ----------------

        public override void OnStartServer()
        {
            base.OnStartServer();
            _netObject.NotifySpawnServer();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _netObject.NotifyDespawnServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _netObject.NotifySpawnClient();
            // 纯客户端：向服务器请求全量 NetVar 状态（迟加入补偿）。
            if (!IsServerInitialized)
                RequestFullState();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _netObject.NotifyDespawnClient();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            _netObject.NotifyOwnership(IsOwner);
        }

        // ---------------- INetObjectBridge ----------------

        public void SendServerRpc(string method, byte[] args)
        {
            // 服务器上调用（含 Host）直接本地派发，保证语义确定。
            if (IsServerInitialized)
                _netObject.DispatchRpc(method, NetSerializer.ReadArgs(args));
            else
                RpcToServer(method, args, Channel.Reliable);
        }

        public void SendObserversRpc(string method, byte[] args)
        {
            if (IsServerInitialized)
                RpcToObservers(method, args, Channel.Reliable);
        }

        public void SendTargetRpc(int clientId, string method, byte[] args)
        {
            if (!IsServerInitialized) return;
            if (ServerManager.Clients.TryGetValue(clientId, out NetworkConnection conn))
                RpcToTarget(conn, method, args);
        }

        public void SendNetVar(int varId, byte[] payload)
        {
            if (IsServerInitialized)
                RpcVarToObservers(varId, payload, Channel.Reliable);
        }

        public void SendNetVarTarget(int clientId, int varId, byte[] payload)
        {
            if (!IsServerInitialized) return;
            if (ServerManager.Clients.TryGetValue(clientId, out NetworkConnection conn))
                RpcVarToTarget(conn, varId, payload);
        }

        public void SendTransform(Vector3 position, Quaternion rotation)
        {
            if (IsServerInitialized)
                RpcTransformObservers(position, rotation, Channel.Unreliable);
            else if (IsClientInitialized)
                RpcTransformServer(position, rotation, Channel.Unreliable);
        }

        public void RequestFullState()
        {
            if (IsClientInitialized && !IsServerInitialized)
                RpcFullStateRequest();
        }

        // ---------------- FishNet RPC 通道 ----------------

        [ServerRpc(RequireOwnership = false)]
        private void RpcToServer(string method, byte[] args, Channel channel = Channel.Reliable)
        {
            _netObject.DispatchRpc(method, NetSerializer.ReadArgs(args));
        }

        [ObserversRpc]
        private void RpcToObservers(string method, byte[] args, Channel channel = Channel.Reliable)
        {
            _netObject.DispatchRpc(method, NetSerializer.ReadArgs(args));
        }

        [TargetRpc]
        private void RpcToTarget(NetworkConnection conn, string method, byte[] args)
        {
            _netObject.DispatchRpc(method, NetSerializer.ReadArgs(args));
        }

        [ObserversRpc]
        private void RpcVarToObservers(int varId, byte[] payload, Channel channel = Channel.Reliable)
        {
            _netObject.ApplyNetVar(varId, payload);
        }

        [TargetRpc]
        private void RpcVarToTarget(NetworkConnection conn, int varId, byte[] payload)
        {
            _netObject.ApplyNetVar(varId, payload);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RpcFullStateRequest(NetworkConnection conn = null)
        {
            if (conn == null || !conn.IsValid) return;
            _netObject.SendFullStateTo(conn.ClientId);
        }

        [ServerRpc(RequireOwnership = true)]
        private void RpcTransformServer(Vector3 position, Quaternion rotation, Channel channel = Channel.Unreliable)
        {
            // 服务器端应用（服务器上的该对象副本是非权威接收端），并转发给其他观察者。
            _netObject.NotifyTransform(position, rotation);
            RpcTransformObservers(position, rotation, Channel.Unreliable);
        }

        [ObserversRpc(ExcludeServer = true)]
        private void RpcTransformObservers(Vector3 position, Quaternion rotation, Channel channel = Channel.Unreliable)
        {
            // 所有者不回放（自己就是权威端）。
            if (NetworkObject.Owner != null && NetworkObject.Owner.IsLocalClient) return;
            _netObject.NotifyTransform(position, rotation);
        }
    }
}
