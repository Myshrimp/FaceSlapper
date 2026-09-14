namespace FaceSlapper.FrameSync.Separated
{
    public interface IService
    {
        public void OnAddService(object source, bool isServer);
        public void OnRemoveService(object source);
        public void OnUpdate();
        public void OnDestroy();
        public void OnAwake();
        public void OnEnable();
        public void OnDisable();
        public void OnStart();
    }
}