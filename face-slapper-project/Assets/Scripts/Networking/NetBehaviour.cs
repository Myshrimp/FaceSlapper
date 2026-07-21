using System;
using System.Reflection;
using UnityEngine;

namespace FaceSlapper.Networking
{
    /// <summary>
    /// 后端无关的网络行为基类：替代各网络库原生的 NetworkBehaviour。
    /// 需要 RPC / NetVar 的游戏脚本继承本类；纯逻辑脚本可直接用 MonoBehaviour + 兄弟 NetObject。
    /// </summary>
    [RequireComponent(typeof(NetObject))]
    public abstract class NetBehaviour : MonoBehaviour
    {
        /// <summary>本物体上的网络对象组件。</summary>
        public NetObject NetObject { get; private set; }

        /// <summary>NetObject 的简写。</summary>
        protected NetObject NetObj => NetObject;

        public bool IsServer => Net.IsServer;
        public bool IsClient => Net.IsClient;
        public bool IsOwner => NetObject != null && NetObject.IsOwner;

        protected virtual void Awake()
        {
            NetObject = GetComponent<NetObject>();
            NetObject.RegisterBehaviour(this);
            RegisterNetVars();
        }

        protected virtual void OnDestroy()
        {
            if (NetObject != null) NetObject.UnregisterBehaviour(this);
        }

        /// <summary>
        /// 反射注册本类（含基类）声明的所有 NetVar/NetList 字段（按声明顺序分配 Id，各端一致）。
        /// 注意必须沿继承链逐层扫描：Type.GetFields 不会返回基类的 private 字段。
        /// </summary>
        private void RegisterNetVars()
        {
            for (Type t = GetType(); t != null && t != typeof(NetBehaviour) && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                FieldInfo[] fields = t.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (!typeof(INetVarEntry).IsAssignableFrom(field.FieldType)) continue;
                    if (field.GetValue(this) is not INetVarEntry entry) continue;

                    int id = NetObject.RegisterVar(entry);
                    if (entry is IInternalNetVar v)
                        v.SetRegistration(id, this);
                }
            }
        }

        // ---------------- RPC 发送 ----------------

        /// <summary>客户端 → 服务器 调用本对象上的 [NetRpc] 方法。</summary>
        protected void SendServerRpc(string method, params object[] args)
        {
            NetObject.SendRpcToServer(method, args);
        }

        /// <summary>服务器 → 所有观察者 调用本对象上的 [NetRpc] 方法。</summary>
        protected void SendObserversRpc(string method, params object[] args)
        {
            NetObject.SendRpcToObservers(method, args);
        }

        /// <summary>服务器 → 指定客户端 调用本对象上的 [NetRpc] 方法。</summary>
        protected void SendTargetRpc(int clientId, string method, params object[] args)
        {
            NetObject.SendRpcToTarget(clientId, method, args);
        }

        // ---------------- NetVar 写入（框架内部回调） ----------------

        internal void WriteNetVar<T>(NetVar<T> var, T value)
        {
            if (!Net.IsServer)
            {
                Debug.LogWarning($"[NetVar] {GetType().Name} 的同步变量只能在服务器上写入");
                return;
            }
            var.ApplyLocal(value);
            NetObject.SendNetVar(var.RegisteredId);
        }

        internal void MarkNetVarDirty(int varId)
        {
            if (!Net.IsServer) return;
            NetObject.SendNetVar(varId);
        }

        // ---------------- 生命周期（由 NetObject 转发） ----------------

        public virtual void OnNetSpawnServer() { }

        public virtual void OnNetSpawnClient() { }

        public virtual void OnNetDespawnServer() { }

        public virtual void OnNetDespawnClient() { }

        public virtual void OnNetOwnershipChanged(bool isOwner) { }
    }

    /// <summary>NetVar/NetList 的内部注册接口。</summary>
    internal interface IInternalNetVar
    {
        void SetRegistration(int id, NetBehaviour host);
    }
}
