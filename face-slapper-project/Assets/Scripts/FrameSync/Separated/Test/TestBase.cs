using System;
using UnityEngine;

namespace FaceSlapper.FrameSync.Separated.Test
{
    [Serializable]
    public class TestBase
    {
        public string Name;
        public GameMain GameMain;
        public ServerMain ServerMain;

        public virtual void OnAwake(GameMain gameMain)
        {
            GameMain = gameMain;
            ServerMain = gameMain.ServerMain;
        }
        public virtual void OnStart()
        { }
        public virtual void OnUpdate(){}
        public virtual void OnFixedUpdate(){}
        public virtual void OnLateUpdate(){}
    }
}