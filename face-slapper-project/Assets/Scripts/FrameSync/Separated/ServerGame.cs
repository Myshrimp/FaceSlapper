using FaceSlapper.FrameSync;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
public class ServerGame
{
    private PlayerInfo[] _playerInfos;
    private Dictionary<int, PlayerState> _players=new();

    public ServerGame() { }
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
}