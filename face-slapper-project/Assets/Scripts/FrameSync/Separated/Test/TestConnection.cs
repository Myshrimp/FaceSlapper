using UnityEngine;

using System.Collections;
namespace FaceSlapper.FrameSync.Separated.Test
{
    public class TestConnection : TestBase
    {
        public override void OnUpdate()
        {
            base.OnUpdate();
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<UnityEngine.UI.InputField>() != null) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Q) && !ServerMain.ServerStarted)
            {
                ServerMain.ServerStarted = true;
                ServerMain.NetworkComponent.StartHost();
                Debug.Log("Server main started");
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.R) && !ServerMain.ClientStarted)
            {
                ServerMain.ClientStarted = true;
                ServerMain.NetworkComponent.StartClient(ServerMain.IP);
                Debug.Log("Client started");
            }
            if(UnityEngine.Input.GetKeyDown(KeyCode.Space))
            {
                ServerMain.RequestGameData();
                Debug.Log("GameReady,requesting data");
            }
        }
    }
}