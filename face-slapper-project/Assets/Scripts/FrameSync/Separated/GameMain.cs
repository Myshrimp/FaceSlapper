using System;
using System.Collections.Generic;
using FaceSlapper.Core;
using FaceSlapper.FrameSync.Separated;
using FaceSlapper.FrameSync.Separated.Test;
using FaceSlapper.Input;
using FaceSlapper.Network;
using UnityEngine;
using Newtonsoft.Json;
using FaceSlapper.FrameSync.Separated.Test;

public class GameMain : MonoBehaviour
{
    public ServerMain ServerMain;
    public PlayerInfo PlayerInfo;
    public List<TestBase> Tests;
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
        ServerMain.ServerDataArrivedCb += OnDataArrive;

        for (int i=0;i<Tests.Count;i++)
        {
            Type t = Type.GetType($"FaceSlapper.FrameSync.Separated.Test.{Tests[i].Name}");
            Tests[i] = Activator.CreateInstance(t) as TestBase;
            Tests[i].OnAwake(this);
        }
    }
    private void Update()
    {
        foreach (var test in Tests)
        {
            test.OnUpdate();
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

    private void OnDataArrive(DataMsg msg)
    {
        MsgHead head = JsonConvert.DeserializeObject<MsgHead>(msg.head);
        switch (head.module)
        {
            case "Chat":
                EventBus.Publish<ChatEvent>(new ChatEvent(msg));
                break;
            default:
                break;
        }
    }

    public int ReceiveLocalInputs()
    {
        return 0;
    }
}