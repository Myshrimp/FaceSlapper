using FaceSlapper.Core;
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

    public Dictionary<int, PlayerInfo> PlayerInfos;
    public Dictionary<int, PlayerState> Players;
    public string GameStateRaw;
    public GameState GameState;
    public bool ServerStarted;
    public bool ClientStarted; //TEST
    public string IP="127.0.0.1";
    public Action<string> GameStateArrivedCb;
    public Action<string> ReceivePlayerInfoCb;
    public Action<DataMsg> ServerDataArrivedCb;
    private ServerGame game;

    public NetworkComponent NetworkComponent => GameManager.Instance.Get<NetworkComponent>();
    protected override void Awake()
    {
        base.Awake();
        PlayerInfos = new Dictionary<int, PlayerInfo>();
        Players = new Dictionary<int, PlayerState>();
        game = new ServerGame();
    }

    public override void OnNetSpawnServer()
    {
        base.OnNetSpawnServer();
        Net.OnRemoteClientConnected += OnClientConnected;
        Net.OnRemoteClientDisconnected += OnClientDisconnected;
    }

    public override void OnNetSpawnClient()
    {
        base.OnNetSpawnClient();
    }

    public void RequestGameData()
    {
        SendServerRpc(nameof(CmdGetGameData));
    }

    public void RequestClientSendData(string data)
    {
        SendServerRpc(nameof(CmdServerReceiveData), data);
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
        DataMsg dataMsg = JsonConvert.DeserializeObject<DataMsg>(data);
        game.Handle(dataMsg);
    }

    [NetRpc]
    private void CmdClientReceiveData(string data)
    {
        DataMsg dataMsg = JsonConvert.DeserializeObject<DataMsg>(data);
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
