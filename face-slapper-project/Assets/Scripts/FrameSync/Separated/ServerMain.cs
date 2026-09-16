using FaceSlapper.Core;
using FaceSlapper.FrameSync.Separated;
using FaceSlapper.FrameSync.Separated.Services;
using FaceSlapper.FrameSync.Separated.Test;
using FaceSlapper.Network;
using FaceSlapper.Networking;
using LiteNetLib.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
public class PlayerInfo
{
    public string Name { get; set; }
    public int ID;
    public bool IsConnected;
}

public class ServerMain : NetBehaviour
{
    [SerializeField]
    private int _connectionCount;
    [SerializeField]
    private NetObject _netObserver;
    [SerializeField]
    private List<string> _servicesName = new List<string> { nameof(ChatService) };

    public Dictionary<int, PlayerInfo> PlayerInfos;
    public Dictionary<int, PlayerState> Players;
    public string GameStateRaw;
    public GameState GameState;
    public bool ServerStarted;
    public bool ClientStarted; //TEST
    public bool IsClientReady { get; private set; }
    public string IP="127.0.0.1";
    public Action<string> GameStateArrivedCb;
    public Action<string> ReceivePlayerInfoCb;
    public Action<DataMsg> ServerDataArrivedCb;
    private ServerGame game;
    private readonly Dictionary<string, ServiceBase> _services = new Dictionary<string, ServiceBase>();

    public NetworkComponent NetworkComponent => GameManager.Instance.Get<NetworkComponent>();
    protected override void Awake()
    {
        base.Awake();
        PlayerInfos = new Dictionary<int, PlayerInfo>();
        Players = new Dictionary<int, PlayerState>();
        game = new ServerGame(this);
    }

    public override void OnNetSpawnServer()
    {
        base.OnNetSpawnServer();
        Net.OnRemoteClientConnected -= OnClientConnected;
        Net.OnRemoteClientDisconnected -= OnClientDisconnected;
        Net.OnRemoteClientConnected += OnClientConnected;
        Net.OnRemoteClientDisconnected += OnClientDisconnected;
        if (_services.Count > 0) return;
        if (_servicesName == null || _servicesName.Count == 0)
            _servicesName = new List<string> { nameof(ChatService) };
        foreach (string name in _servicesName)
        {
            if (string.IsNullOrWhiteSpace(name) || _services.ContainsKey(name)) continue;
            Type type = Type.GetType($"FaceSlapper.FrameSync.Separated.Services.{name}");
            if (type == null || type.IsAbstract || !typeof(ServiceBase).IsAssignableFrom(type))
            {
                Debug.LogWarning($"[ServerMain] Invalid service: {name}");
                continue;
            }
            var service = (ServiceBase)Activator.CreateInstance(type);
            service.OnAddService(this, true);
            _services.Add(name, service);
        }
        Debug.Log($"[ServerMain] Registered {_services.Count} services");
    }

    public override void OnNetDespawnServer()
    {
        RemoveServices();
        base.OnNetDespawnServer();
    }

    protected override void OnDestroy()
    {
        RemoveServices();
        base.OnDestroy();
    }

    private void RemoveServices()
    {
        Net.OnRemoteClientConnected -= OnClientConnected;
        Net.OnRemoteClientDisconnected -= OnClientDisconnected;
        foreach (ServiceBase service in _services.Values)
            service.OnRemoveService(this);
        _services.Clear();
    }

    public override void OnNetSpawnClient()
    {
        base.OnNetSpawnClient();
        IsClientReady = true;
    }

    public override void OnNetDespawnClient()
    {
        IsClientReady = false;
        base.OnNetDespawnClient();
    }

    public void RequestGameData()
    {
        SendServerRpc(nameof(CmdGetGameData));
    }

    public void RequestClientSendData(string data)
    {
        SendServerRpc(nameof(CmdServerReceiveData), data);
    }

    public void BroadcastData(DataMsg data)
    {
        SendObserversRpc(nameof(CmdClientReceiveData), JsonConvert.SerializeObject(data));
    }

    public void ObserverRpc(string method, params object[] args)
    {
        SendObserversRpc(method, args);
    }

    public void TargetRpc(int clientId, string method, params object[] args)
    {
        SendTargetRpc(clientId, method, args);
    }

    public void ServerRpc(string method, params object[] args)
    {
        SendServerRpc(method, args);
    }
    private void OnClientConnected(int playerId)
    {
        RegisterPlayer(playerId);
        UpdateGameState();
        PlayerInfo info = new PlayerInfo();
        info.ID = playerId;
        info.Name = $"Player{playerId}";
        info.IsConnected = true;
        PlayerInfos[playerId] = info;
        Debug.Log($"Player {playerId} has connected");
    }

    private void OnClientDisconnected(int playerId)
    {
        UpdateGameState();
        SendObserversRpc(nameof(CmdSyncGameState), GameStateRaw);
        Debug.Log($"Player {playerId} has disconnected");
    }

    private void RegisterPlayer(int playerId)
    {
        PlayerInfo info = new PlayerInfo();
        info.ID = playerId;
        info.Name = $"Player{playerId}";
        info.IsConnected = true;

        PlayerInfos[playerId] = info;
        game.OnInitPlayers(PlayerInfos.Values.ToArray());
    }

    private void UpdateGameState()
    {
        GameState = game.FetchGameState();
        GameStateRaw = JsonConvert.SerializeObject(GameState, Formatting.None);
        Debug.Log($"GameState:{GameStateRaw}");
    }

    [NetRpc]
    private void CmdSyncGameState(string gameState)
    {
        Debug.Log("[ServerMain] GameStateCallback");
        if(GameStateArrivedCb != null) 
            GameStateArrivedCb(gameState);
    }

    [NetRpc]
    private void CmdReceivePlayerInfo(string playerInfo)
    {
        ReceivePlayerInfoCb(playerInfo);
    }

    [NetRpc]
    private void CmdGetGameData()
    {
        foreach(var player in PlayerInfos.Values)
        {
            int playerId = player.ID;
            SendTargetRpc(playerId, nameof(CmdReceivePlayerInfo), JsonConvert.SerializeObject(PlayerInfos[playerId]));
            SendObserversRpc(nameof(CmdSyncGameState), GameStateRaw);
        }
    }

    [NetRpc]
    private void CmdServerReceiveData(string data)
    {
        if (!IsServer) return;
        int senderClientId = NetObject.RpcSenderClientId;
        if (senderClientId < 0 && Net.IsHost)
            senderClientId = Net.LocalClientId;
        if (senderClientId < 0 || !Net.Server.ClientIds.Contains(senderClientId)) return;
        if (Protocol.TryReadData(data, out DataMsg dataMsg))
            game.Handle(dataMsg, senderClientId);
    }

    [NetRpc]
    private void CmdClientReceiveData(string data)
    {
        if (!IsClient || NetObject.RpcSenderClientId >= 0) return;
        if (Protocol.TryReadData(data, out DataMsg dataMsg))
            ServerDataArrivedCb?.Invoke(dataMsg);
    }
}

public struct DataMsg
{
    public string head;
    public string body;

    public DataMsg(string head, string body)
    {
        this.head = head;
        this.body = body;
    }
}

public struct MsgHead
{
    public int fromPlayer;
    public bool isBroadcast;
    public int[] toPlayers;
    public string module;
    public string eventType;
}
