using FaceSlapper.Core;
using FishNet.Demo.AdditiveScenes;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class GameHud : MonoBehaviour
{
    public Text PlayerId;
    public Text PlayerName;

    public GameObject PlayerInfoListItem;
    public Transform PlayersListRoot;
    public List<PlayerListItem> ListItems;
    private List<int> _playerIds;
    private Dictionary<int, PlayerListItem> _playerListItemMap;

    private void Awake()
    {
        _playerIds = new List<int>(10);
        _playerListItemMap = new();
        EventBus.Subscribe<GameState>((state) =>
        {
            Debug.Log("[GameHud] Received game state");
            var players = state.playerStates;
            foreach(var p in players)
            {
                _playerIds.Add(p.Id);
            }
            RefreshPlayerListView();
        });

        EventBus.Subscribe<PlayerInfo>((info) =>
        {
            PlayerId.text = info.ID.ToString();
            PlayerName.text = info.Name;
        });
    }

    private void RefreshPlayerListView()
    {
        foreach(var id in _playerIds)
        {
            if(_playerListItemMap.ContainsKey(id))
            {
                _playerListItemMap[id].Setup(id);
            }
            else
            {
                _playerListItemMap[id] = Instantiate(PlayerInfoListItem, PlayersListRoot).GetComponent<PlayerListItem>();
                _playerListItemMap[id].Setup(id);
            }
        }

        UpdatePlayerListItemMap();
    }

    private void UpdatePlayerListItemMap()
    {
        foreach(var kv in _playerListItemMap)
        {
            var item = kv.Value;
            item.gameObject.SetActive(item.active);
            item.active = false;
        }   
    }
}