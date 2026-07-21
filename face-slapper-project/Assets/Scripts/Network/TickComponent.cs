using System;
using FaceSlapper.Core;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Network
{
    /// <summary>
    /// 网络 Tick 组件：把 <see cref="Net"/> 门面的 PreTick/Tick/PostTick 事件
    /// 转发给本组件的订阅者（例如未来的服务器权威预测逻辑）。
    /// </summary>
    public class TickComponent : MonoBehaviour, IGameComponent
    {
        private event Action _onPreTick;
        private event Action _onTick;
        private event Action _onPostTick;

        public void OnInit()
        {
            Net.OnPreTick += HandlePreTick;
            Net.OnTick += HandleTick;
            Net.OnPostTick += HandlePostTick;
        }

        public void OnShutdown()
        {
            Net.OnPreTick -= HandlePreTick;
            Net.OnTick -= HandleTick;
            Net.OnPostTick -= HandlePostTick;
        }

        public void RegisterPreTick(Action callback) => _onPreTick += callback;
        public void UnregisterPreTick(Action callback) => _onPreTick -= callback;

        public void RegisterTick(Action callback) => _onTick += callback;
        public void UnregisterTick(Action callback) => _onTick -= callback;

        public void RegisterPostTick(Action callback) => _onPostTick += callback;
        public void UnregisterPostTick(Action callback) => _onPostTick -= callback;

        private void HandlePreTick() => _onPreTick?.Invoke();
        private void HandleTick() => _onTick?.Invoke();
        private void HandlePostTick() => _onPostTick?.Invoke();
    }
}
