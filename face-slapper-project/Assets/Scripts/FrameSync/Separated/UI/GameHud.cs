using FaceSlapper.Core;
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

    private void Awake()
    {
        EventBus.Subscribe<GameState>((state) =>
        {
            Debug.Log("[GameHud] Received game state");
            
        });

        EventBus.Subscribe<PlayerInfo>((info) =>
        {
            PlayerId.text = info.ID.ToString();
            PlayerName.text = info.Name;
        });
    }
}