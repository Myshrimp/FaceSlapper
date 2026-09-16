using System;

namespace FaceSlapper.FrameSync.Separated.Services
{
    public class ServiceBase : IService
    {
        protected ServerMain serverMain;
        protected GameMain gameMain;
        protected bool isServer;
        public virtual void OnAddService(object source, bool isServer)
        {
            if (serverMain != null || gameMain != null)
                OnRemoveService(null);
            this.isServer = isServer;
            if(isServer)
            {
                serverMain = (ServerMain)source;
            }
            else
            {
                gameMain = (GameMain)source;
            }
            OnAwake();
        }

        public virtual void OnRemoveService(object source)
        {
            if (serverMain == null && gameMain == null) return;
            OnDestroy();
            serverMain = null;
            gameMain = null;
            isServer = false;
        }

        public virtual void OnUpdate()
        {
            //throw new System.NotImplementedException();
        }

        public virtual void OnDestroy()
        {
            //throw new System.NotImplementedException();
        }

        public virtual void OnAwake()
        {
            //throw new System.NotImplementedException();
        }

        public virtual void OnEnable()
        {
            //throw new System.NotImplementedException();
        }

        public virtual void OnDisable()
        {
            //throw new System.NotImplementedException();
        }

        public virtual void OnStart()
        {
            //throw new System.NotImplementedException();
        }

        public static T GetService<T>() where T : ServiceBase
        {
            Type t = typeof(T);
            return Activator.CreateInstance<T>();
        }
    }
}