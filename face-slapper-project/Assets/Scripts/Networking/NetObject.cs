using System;
using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Networking
{
    /// <summary>
    /// 后端无关的网络对象标识组件：替代各网络库原生的 NetworkObject。
    /// 与后端桥接组件（由后端在生成 Prefab 时添加）配对工作，
    /// 为游戏逻辑提供身份、所有权、生命周期事件与 RPC/NetVar 路由。
    /// </summary>
    [DisallowMultipleComponent]
    public class NetObject : MonoBehaviour
    {
        /// <summary>网络对象 Id，未生成时为 -1。</summary>
        public int NetId => _bridge?.NetId ?? -1;

        /// <summary>是否已在网络上生成。</summary>
        public bool IsSpawned => NetId >= 0;

        /// <summary>所有者 ClientId，无所有者（服务器权威）时为 -1。</summary>
        public int OwnerClientId => _bridge?.OwnerClientId ?? -1;

        /// <summary>本机是否拥有所有权。</summary>
        public bool IsOwner => _bridge != null && _bridge.IsOwner;

        /// <summary>本机是否控制该对象（所有者，或无所有者时的服务器）。</summary>
        public bool IsController => IsOwner || (Net.IsServer && OwnerClientId < 0);

        // ---------------- 生命周期事件 ----------------

        public event Action OnSpawnServer;
        public event Action OnSpawnClient;
        public event Action OnDespawnServer;
        public event Action OnDespawnClient;

        /// <summary>所有权变化（参数: 本机是否新所有者）。</summary>
        public event Action<bool> OnOwnershipChanged;

        /// <summary>收到远端位置/旋转（NetTransformSync 使用）。</summary>
        public event Action<Vector3, Quaternion> OnTransformReceived;

        // ---------------- 内部状态 ----------------

        private INetObjectBridge _bridge;
        private readonly List<NetBehaviour> _behaviours = new List<NetBehaviour>(4);
        private readonly List<INetVarEntry> _vars = new List<INetVarEntry>(8);
        private Dictionary<string, RpcTarget> _rpcTable;

        private struct RpcTarget
        {
            public NetBehaviour Behaviour;
            public System.Reflection.MethodInfo Method;

            public void Invoke(object[] args) => NetRpcDispatcher.Invoke(Method, Behaviour, args);
        }

        internal INetObjectBridge Bridge => _bridge;

        // ---------------- 注册（NetBehaviour.Awake 调用） ----------------

        internal void RegisterBehaviour(NetBehaviour behaviour)
        {
            if (_behaviours.Contains(behaviour)) return;
            _behaviours.Add(behaviour);
            _rpcTable = null;
        }

        internal void UnregisterBehaviour(NetBehaviour behaviour)
        {
            _behaviours.Remove(behaviour);
            _rpcTable = null;
        }

        internal int RegisterVar(INetVarEntry entry)
        {
            _vars.Add(entry);
            return _vars.Count - 1;
        }

        internal void AttachBridge(INetObjectBridge bridge) => _bridge = bridge;

        // ---------------- RPC ----------------

        internal void SendRpcToServer(string method, object[] args)
        {
            if (_bridge == null)
            {
                Debug.LogWarning($"[NetObject] {name} 尚未在网络上生成，无法发送 ServerRpc {method}");
                return;
            }
            _bridge.SendServerRpc(method, NetSerializer.WriteArgs(args));
        }

        internal void SendRpcToObservers(string method, object[] args)
        {
            if (_bridge == null || !Net.IsServer)
            {
                Debug.LogWarning($"[NetObject] ObserversRpc {method} 只能在服务器上发送");
                return;
            }
            _bridge.SendObserversRpc(method, NetSerializer.WriteArgs(args));
        }

        internal void SendRpcToTarget(int clientId, string method, object[] args)
        {
            if (_bridge == null || !Net.IsServer)
            {
                Debug.LogWarning($"[NetObject] TargetRpc {method} 只能在服务器上发送");
                return;
            }
            _bridge.SendTargetRpc(clientId, method, NetSerializer.WriteArgs(args));
        }

        internal void DispatchRpc(string method, object[] args)
        {
            BuildRpcTable();
            if (_rpcTable.TryGetValue(method, out RpcTarget target))
                target.Invoke(args);
            else
                Debug.LogWarning($"[NetObject] {name} 上未找到 [NetRpc] 方法: {method}");
        }

        private void BuildRpcTable()
        {
            if (_rpcTable != null) return;
            _rpcTable = new Dictionary<string, RpcTarget>(8);
            foreach (NetBehaviour behaviour in _behaviours)
            {
                foreach (KeyValuePair<string, System.Reflection.MethodInfo> kvp in NetRpcDispatcher.GetRpcMethods(behaviour.GetType()))
                    _rpcTable[kvp.Key] = new RpcTarget { Behaviour = behaviour, Method = kvp.Value };
            }
        }

        // ---------------- NetVar ----------------

        internal void SendNetVar(int varId)
        {
            if (_bridge == null || !Net.IsServer) return;
            if (varId < 0 || varId >= _vars.Count) return;
            _bridge.SendNetVar(varId, _vars[varId].Serialize());
        }

        internal void ApplyNetVar(int varId, byte[] payload)
        {
            if (varId < 0 || varId >= _vars.Count) return;
            _vars[varId].DeserializeAndApply(payload);
        }

        internal void SendFullStateTo(int clientId)
        {
            if (_bridge == null) return;
            for (int i = 0; i < _vars.Count; i++)
                _bridge.SendNetVarTarget(clientId, i, _vars[i].Serialize());
        }

        // ---------------- 生命周期转发（由后端桥接组件调用） ----------------

        internal void NotifySpawnServer()
        {
            OnSpawnServer?.Invoke();
            foreach (NetBehaviour b in _behaviours) b.OnNetSpawnServer();
        }

        internal void NotifySpawnClient()
        {
            OnSpawnClient?.Invoke();
            foreach (NetBehaviour b in _behaviours) b.OnNetSpawnClient();
        }

        internal void NotifyDespawnServer()
        {
            OnDespawnServer?.Invoke();
            foreach (NetBehaviour b in _behaviours) b.OnNetDespawnServer();
        }

        internal void NotifyDespawnClient()
        {
            OnDespawnClient?.Invoke();
            foreach (NetBehaviour b in _behaviours) b.OnNetDespawnClient();
        }

        internal void NotifyOwnership(bool isOwner)
        {
            OnOwnershipChanged?.Invoke(isOwner);
            foreach (NetBehaviour b in _behaviours) b.OnNetOwnershipChanged(isOwner);
        }

        internal void NotifyTransform(Vector3 position, Quaternion rotation)
        {
            OnTransformReceived?.Invoke(position, rotation);
        }
    }

    /// <summary>NetVar/NetList 的非泛型登记接口（框架内部使用）。</summary>
    internal interface INetVarEntry
    {
        byte[] Serialize();
        void DeserializeAndApply(byte[] payload);
    }
}
