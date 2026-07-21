using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace FaceSlapper.Networking.FishNetImpl
{
    /// <summary>
    /// FishNet 后端实现：把 <see cref="INetBackend"/> 映射到 FishNet Pro 4.7 的 API。
    /// 全部 FishNet 引用都收口在本文件与 FishNetNetObjectBridge 中。
    /// </summary>
    public class FishNetBackend : INetBackend
    {
        private FishNetBackendRunner _runner;

        private static NetworkManager NM => InstanceFinder.NetworkManager;

        // ---------------- 状态 ----------------

        public bool IsServer => NM != null && NM.IsServerStarted;
        public bool IsClient => NM != null && NM.IsClientStarted;
        public bool IsHost => NM != null && NM.IsHostStarted;
        public int LocalClientId => IsClient ? NM.ClientManager.Connection.ClientId : -1;

        // ---------------- 事件 ----------------

        public event Action<int> OnRemoteClientConnected;
        public event Action<int> OnRemoteClientDisconnected;
        public event Action OnServerStarted;
        public event Action OnServerStopped;
        public event Action OnClientStarted;
        public event Action OnClientStopped;
        public event Action OnPreTick;
        public event Action OnTick;
        public event Action OnPostTick;

        // ---------------- 生命周期 ----------------

        public void Initialize()
        {
            if (_runner != null) return;
            var go = new GameObject("~FishNetBackend");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<FishNetBackendRunner>();
            _runner.Bind(this);
        }

        public void Shutdown()
        {
            if (_runner != null)
            {
                UnityEngine.Object.Destroy(_runner.gameObject);
                _runner = null;
            }
        }

        // ---------------- 连接 ----------------

        public bool StartHost(ushort port)
        {
            if (NM == null)
            {
                Debug.LogError("[FishNetBackend] 场景中未找到 NetworkManager。");
                return false;
            }
            SetPort(port);
            NM.ServerManager.StartConnection();
            NM.ClientManager.StartConnection();
            return true;
        }

        public bool StartClient(string address, ushort port)
        {
            if (NM == null)
            {
                Debug.LogError("[FishNetBackend] 场景中未找到 NetworkManager。");
                return false;
            }
            SetPort(port);
            SetClientAddress(address);
            return NM.ClientManager.StartConnection();
        }

        public void Stop()
        {
            if (NM == null) return;
            if (NM.IsClientStarted) NM.ClientManager.StopConnection();
            if (NM.IsServerStarted) NM.ServerManager.StopConnection(true);
        }

        private static void SetPort(ushort port)
        {
            var tugboat = NM.TransportManager.GetTransport<FishNet.Transporting.Tugboat.Tugboat>();
            if (tugboat != null) tugboat.SetPort(port);
        }

        private static void SetClientAddress(string address)
        {
            var tugboat = NM.TransportManager.GetTransport<FishNet.Transporting.Tugboat.Tugboat>();
            if (tugboat != null) tugboat.SetClientAddress(address);
        }

        // ---------------- 生成/销毁 ----------------

        public NetObject Spawn(NetObject prefab, Vector3 position, Quaternion rotation, int ownerClientId = -1)
        {
            if (NM == null || !IsServer)
            {
                Debug.LogWarning("[FishNetBackend] Spawn 只能在服务器上调用。");
                return null;
            }

            NetworkObject fishNob = prefab.GetComponent<NetworkObject>();
            if (fishNob == null)
            {
                Debug.LogError($"[FishNetBackend] Prefab {prefab.name} 缺少 FishNet NetworkObject，请重新运行 FaceSlapper/Setup All。");
                return null;
            }

            NetworkObject nob = NM.GetPooledInstantiated(fishNob, position, rotation, true);
            NetworkConnection owner = null;
            if (ownerClientId >= 0)
                NM.ServerManager.Clients.TryGetValue(ownerClientId, out owner);
            NM.ServerManager.Spawn(nob, owner);
            return nob.GetComponent<NetObject>();
        }

        public void Despawn(NetObject obj)
        {
            if (NM == null || obj == null) return;
            NetworkObject fishNob = obj.GetComponent<NetworkObject>();
            if (fishNob != null) NM.ServerManager.Despawn(fishNob);
        }

        // ---------------- 查找 ----------------

        public bool TryGetObjectServer(int netId, out NetObject obj)
        {
            obj = null;
            if (NM == null || !IsServer) return false;
            if (NM.ServerManager.Objects.Spawned.TryGetValue(netId, out NetworkObject nob))
                obj = nob.GetComponent<NetObject>();
            return obj != null;
        }

        public bool TryGetObjectClient(int netId, out NetObject obj)
        {
            obj = null;
            if (NM == null || !IsClient) return false;
            if (NM.ClientManager.Objects.Spawned.TryGetValue(netId, out NetworkObject nob))
                obj = nob.GetComponent<NetObject>();
            return obj != null;
        }

        public NetObject LocalPlayer
        {
            get
            {
                if (!IsClient) return null;
                NetworkObject nob = NM.ClientManager.Connection?.FirstObject;
                return nob != null ? nob.GetComponent<NetObject>() : null;
            }
        }

        public IReadOnlyCollection<int> ConnectedClientIds =>
            IsServer ? NM.ServerManager.Clients.Keys : (IReadOnlyCollection<int>)Array.Empty<int>();

        // ---------------- 所有权 ----------------

        public void GiveOwnership(NetObject obj, int clientId)
        {
            if (!IsServer || obj == null) return;
            if (!NM.ServerManager.Clients.TryGetValue(clientId, out NetworkConnection conn)) return;
            obj.GetComponent<NetworkObject>()?.GiveOwnership(conn);
        }

        public void RemoveOwnership(NetObject obj)
        {
            if (!IsServer || obj == null) return;
            obj.GetComponent<NetworkObject>()?.RemoveOwnership();
        }

        // ---------------- 场景 ----------------

        public void LoadGlobalScene(string sceneName)
        {
            if (NM == null || !IsServer) return;
            var sld = new SceneLoadData(sceneName);
            NM.SceneManager.LoadGlobalScenes(sld);
        }

        // ---------------- 事件转发（由 Runner 回调） ----------------

        internal void RaiseRemoteClientConnected(int clientId) => OnRemoteClientConnected?.Invoke(clientId);
        internal void RaiseRemoteClientDisconnected(int clientId) => OnRemoteClientDisconnected?.Invoke(clientId);
        internal void RaiseServerStarted() => OnServerStarted?.Invoke();
        internal void RaiseServerStopped() => OnServerStopped?.Invoke();
        internal void RaiseClientStarted() => OnClientStarted?.Invoke();
        internal void RaiseClientStopped() => OnClientStopped?.Invoke();
        internal void RaisePreTick() => OnPreTick?.Invoke();
        internal void RaiseTick() => OnTick?.Invoke();
        internal void RaisePostTick() => OnPostTick?.Invoke();

        // ---------------- 编辑器集成 ----------------
#if UNITY_EDITOR
        public void EditorEnsureManagerInScene(ushort port)
        {
            NetworkManager nm = UnityEngine.Object.FindObjectOfType<NetworkManager>();
            if (nm == null)
            {
                var go = new GameObject("NetworkManager");
                nm = go.AddComponent<NetworkManager>();
            }

            var transportManager = nm.GetComponent<FishNet.Managing.Transporting.TransportManager>();
            if (transportManager == null)
                transportManager = nm.gameObject.AddComponent<FishNet.Managing.Transporting.TransportManager>();

            var tugboat = nm.GetComponent<FishNet.Transporting.Tugboat.Tugboat>();
            if (tugboat == null)
                tugboat = nm.gameObject.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();
            tugboat.SetPort(port);
            transportManager.Transport = tugboat;
        }

        public void EditorEnsurePrefabComponents(GameObject prefabRoot)
        {
            // 清理旧版直连 FishNet 的组件（如 NetworkTransform）。
            var legacyNt = prefabRoot.GetComponent<FishNet.Component.Transforming.NetworkTransform>();
            if (legacyNt != null) UnityEngine.Object.DestroyImmediate(legacyNt, true);

            if (prefabRoot.GetComponent<NetworkObject>() == null)
                prefabRoot.AddComponent<NetworkObject>();
            if (prefabRoot.GetComponent<NetObject>() == null)
                prefabRoot.AddComponent<NetObject>();
            if (prefabRoot.GetComponent<FishNetNetObjectBridge>() == null)
                prefabRoot.AddComponent<FishNetNetObjectBridge>();
        }

        public void EditorRegisterSpawnablePrefab(GameObject prefabRoot)
        {
            const string path = "Assets/DefaultPrefabObjects.asset";
            var dpo = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.DefaultPrefabObjects>(path);
            if (dpo == null)
            {
                Debug.LogWarning($"[FishNetBackend] 未找到 {path}。");
                return;
            }
            // 清理已失效的 Prefab 引用（例如 Prefab 被重建后留下的空条目）。
            dpo.RemoveNull();

            NetworkObject nob = prefabRoot.GetComponent<NetworkObject>();
            if (nob != null)
            {
                dpo.AddObject(nob, true, false);
                UnityEditor.EditorUtility.SetDirty(dpo);
            }
        }
#endif
    }

    /// <summary>
    /// FishNet 事件绑定器：NetworkManager 在场景加载后才可用，这里做延迟订阅并转发事件。
    /// </summary>
    public class FishNetBackendRunner : MonoBehaviour
    {
        private FishNetBackend _backend;
        private bool _bound;

        public void Bind(FishNetBackend backend) => _backend = backend;

        private void Update()
        {
            if (!_bound) TryBind();
        }

        private void TryBind()
        {
            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm == null) return;

            nm.ServerManager.OnServerConnectionState += OnServerConnectionState;
            nm.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            nm.ClientManager.OnClientConnectionState += OnClientConnectionState;
            nm.TimeManager.OnPreTick += OnPreTick;
            nm.TimeManager.OnTick += OnTick;
            nm.TimeManager.OnPostTick += OnPostTick;
            _bound = true;
        }

        private void OnDestroy()
        {
            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm != null && _bound)
            {
                nm.ServerManager.OnServerConnectionState -= OnServerConnectionState;
                nm.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
                nm.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                nm.TimeManager.OnPreTick -= OnPreTick;
                nm.TimeManager.OnTick -= OnTick;
                nm.TimeManager.OnPostTick -= OnPostTick;
            }
            _bound = false;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started) _backend.RaiseServerStarted();
            else if (args.ConnectionState == LocalConnectionState.Stopped) _backend.RaiseServerStopped();
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started) _backend.RaiseClientStarted();
            else if (args.ConnectionState == LocalConnectionState.Stopped) _backend.RaiseClientStopped();
        }

        private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started) _backend.RaiseRemoteClientConnected(conn.ClientId);
            else if (args.ConnectionState == RemoteConnectionState.Stopped) _backend.RaiseRemoteClientDisconnected(conn.ClientId);
        }

        private void OnPreTick() => _backend.RaisePreTick();
        private void OnTick() => _backend.RaiseTick();
        private void OnPostTick() => _backend.RaisePostTick();
    }
}
