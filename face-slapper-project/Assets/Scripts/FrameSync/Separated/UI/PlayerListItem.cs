using FishNet.Demo.AdditiveScenes;
using UnityEngine;
using UnityEngine.UI;

public class PlayerListItem : MonoBehaviour
{
    public int playerId;
    public bool active;
    public Text playerName;
    public void Setup(int id)
    {
        playerId = id;
        playerName.text = playerId.ToString();
        active = true;
    }
}