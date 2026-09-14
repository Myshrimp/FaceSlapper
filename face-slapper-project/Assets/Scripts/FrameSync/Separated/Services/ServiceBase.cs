using System;

namespace FaceSlapper.FrameSync.Separated.Services
{
    public class ServiceBase : IService
    {
        protected ServerGame game;
        protected bool isServer;
        public virtual void OnAddService(object source, bool isServer)
        {
            game = source as ServerGame;
            //throw new System.NotImplementedException();
        }

        public virtual void OnRemoveService(object source)
        {
            game = null;
            //throw new System.NotImplementedException();
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