using Newtonsoft.Json;
using System;
using FaceSlapper.FrameSync;

public class PlayerState
{
    public int Id;
    public bool isConnected;
    public FPVec3 position;
    public FPVec3 direction;
    public FPVec3 up;
}

public struct WorldState
{

}

public struct GameState
{
    public int frame;
    public int curScene;
    public int gamePhase;
    public PlayerState[] playerStates;
    public WorldState[] worldStates;
}