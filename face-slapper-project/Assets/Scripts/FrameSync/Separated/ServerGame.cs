using FaceSlapper.FrameSync;
using System.Collections.Generic;
using System.Linq;
using FaceSlapper.Core;
using FaceSlapper.FrameSync.Separated;
using UnityEngine;
using FaceSlapper.FrameSync.Separated.Services;
public class ServerGame
{
    private PlayerInfo[] _playerInfos;
    private Dictionary<int, PlayerState> _players=new();
    private ServerMain _main;
    public ServerMain ServerMain => _main;

    public ServerGame(ServerMain main) 
    { 
        this._main = main; 
    }
    public void OnInitPlayers(PlayerInfo[] playerInfos)
    {
        _playerInfos = playerInfos ?? new PlayerInfo[0];
        for(int i=0; i<playerInfos.Length; i++)
        {
            if (!_players.ContainsKey(i))
            {
                _players.Add(i, new PlayerState());
            }
            if(playerInfos[i] == null || !playerInfos[i].IsConnected)
            {
                _players[i].isConnected = false;
            }
            _players[i].Id = playerInfos[i].ID;
            _players[i].position = new FPVec3(FP.FromFloat(0), FP.FromFloat(0), FP.FromFloat(0));
        }
        Debug.Log($"[SeverGame]{_players.Count} players");
    }

    public GameState FetchGameState()
    {
        GameState gt = new GameState();
        gt.playerStates = _players.Values.ToArray();
        return gt;
    }

    public void Handle(DataMsg msg, int senderClientId)
    {
        if (ChatService.TryReadMessage(msg, out _, out _))
            EventBus.Publish(new ServerChatEvent(_main, msg, senderClientId));
    }
}
