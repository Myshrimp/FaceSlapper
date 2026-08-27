using FaceSlapper.Networking;
using UnityEngine;
namespace FaceSlapper.TL
{
    /// <summary>
    /// 玩家侧 Timeline 组件：owner 端生成后向服务器上报一次 ready；
    /// ready 计数与 tick 广播统一由场景级 TimelineManager 负责。
    /// </summary>
    public class TimelineComponent : NetBehaviour
    {
        public Timeline GlobalMainTimeline;
        private bool _readySent = false;
        private TimelineManager _tlManager;

        protected override void Awake()
        {
            base.Awake();
            
            _tlManager = TimelineManager.Instance;
            if (_tlManager != null)
                GlobalMainTimeline = _tlManager.MainTimeline;
        }

        private void FixedUpdate()
        {
            if (IsOwner && !_readySent)
            {
                _readySent = true;
                SendReady();
            }
        }

        private void SendReady()
        {
            Debug.Log("Send ready");
            SendServerRpc(nameof(ClientReady));
        }

        [NetRpc]
        private void ClientReady()
        {
            // 服务器实例执行：转发给场景级 TimelineManager 集中计数。
            if (_tlManager != null)
                _tlManager.ServerNotifyReady();
            else
                Debug.LogWarning("[Timeline] 场景中未找到 TimelineManager，ready 上报被丢弃");
        }
    }
}
