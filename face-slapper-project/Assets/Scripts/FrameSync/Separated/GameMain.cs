using FaceSlapper.Core;
using FaceSlapper.Input;
using FaceSlapper.Network;
using UnityEngine;
using Newtonsoft.Json;

public class GameMain : MonoBehaviour
{
    public ServerMain ServerMain;
    public PlayerInfo PlayerInfo;
    private GameState GameState;
    private void Awake()
    {
        GameManager gm = GameManager.Instance;
        // 基础组件优先，业务组件在后。
        gm.AddAndRegister<LogComponent>();
        gm.AddAndRegister<TimeComponent>();
        gm.AddAndRegister<PoolComponent>();
        gm.AddAndRegister<InputComponent>();
        gm.AddAndRegister<NetworkComponent>();

        ServerMain = GameObject.FindObjectOfType<ServerMain>();
        ServerMain.GameStateArrivedCb += OnGameStateArrived;
        ServerMain.ReceivePlayerInfoCb += OnAssignId;
    }
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q) && !ServerMain.ServerStarted)
        {
            ServerMain.ServerStarted = true;
            ServerMain.NetworkComponent.StartHost();
            Debug.Log("Server main started");
        }
        if (Input.GetKeyDown(KeyCode.R) && !ServerMain.ClientStarted)
        {
            ServerMain.ClientStarted = true;
            ServerMain.NetworkComponent.StartClient(ServerMain.IP);
            Debug.Log("Client started");
        }
        if(Input.GetKeyDown(KeyCode.Space))
        {
            ServerMain.RequestGameData();
            Debug.Log("GameReady,requesting data");
        }
    }

    private void OnGameStateArrived(string state)
    {
        Debug.Log("[GameMain] GameState received");
        GameState = JsonConvert.DeserializeObject<GameState>(state);
        EventBus.Publish<GameState>(GameState);
    }

    private void OnAssignId(string info)
    {
        PlayerInfo = JsonConvert.DeserializeObject<PlayerInfo>(info);
        EventBus.Publish<PlayerInfo>(PlayerInfo);
    }

    public int ReceiveLocalInputs()
    {
        return 0;
    }
}