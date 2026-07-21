using UnityEngine;

namespace FaceSlapper.Networking
{
    /// <summary>
    /// 网络对象桥接接口：由具体后端（如 FishNet）的桥接组件实现，
    /// NetObject 通过它与底层网络库交互。游戏代码不直接接触本接口。
    /// </summary>
    public interface INetObjectBridge
    {
        /// <summary>网络对象 Id，未生成时为 -1。</summary>
        int NetId { get; }

        /// <summary>所有者 ClientId，无所有者时为 -1。</summary>
        int OwnerClientId { get; }

        /// <summary>本机是否拥有该对象的所有权。</summary>
        bool IsOwner { get; }

        /// <summary>客户端 → 服务器 的 RPC（可靠）。服务器上调用则本地直接派发。</summary>
        void SendServerRpc(string method, byte[] args);

        /// <summary>服务器 → 所有观察者 的 RPC（可靠）。</summary>
        void SendObserversRpc(string method, byte[] args);

        /// <summary>服务器 → 指定客户端 的 RPC（可靠）。</summary>
        void SendTargetRpc(int clientId, string method, byte[] args);

        /// <summary>服务器 → 所有观察者 的 NetVar 同步（可靠）。</summary>
        void SendNetVar(int varId, byte[] payload);

        /// <summary>服务器 → 指定客户端 的 NetVar 同步（用于全量状态补发）。</summary>
        void SendNetVarTarget(int clientId, int varId, byte[] payload);

        /// <summary>位置/旋转同步（不可靠通道）：所有者或服务器 → 其他端。</summary>
        void SendTransform(Vector3 position, Quaternion rotation);

        /// <summary>客户端向服务器请求该对象的全量 NetVar 状态（迟加入补偿）。</summary>
        void RequestFullState();
    }
}
