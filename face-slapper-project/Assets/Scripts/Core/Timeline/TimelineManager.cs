using System;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.TL
{
    /// <summary>
    /// 场景级 Timeline 权威（与 RoomComponent 同挂场景物体，全场景唯一）：
    /// 服务器集中统计玩家 ready、按 GameSettings.TicksPerSec 推进主 Timeline 并广播 tick；
    /// 客户端跟随服务器 tick 推进本地 Timeline。
    /// </summary>
    public class TimelineManager : NetBehaviour
    {
        /// <summary>场景内唯一实例（各端本地可用）。</summary>
        public static TimelineManager Instance { get; private set; }

        /// <summary>主 Timeline（服务器权威推进，客户端跟随）。</summary>
        public Timeline MainTimeline;

        /// <summary>
        /// 主 Timeline 每推进一帧在各端本地触发（参数为推进后的 CurTick）。
        /// 计时/结算类逻辑（Buff 倒计时、技能 CD 等）应挂本事件，而不是各自按 deltaTime 计时。
        /// </summary>
        public event Action<int> Ticked;

        private int _readyCount;
        private bool _ready2Tick;
        private float _tickAccumulator;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            MainTimeline = new Timeline("Main", 0, -1);
        }

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        /// <summary>服务器端：玩家上报 ready（由各玩家 TimelineComponent 的服务器实例转发）。</summary>
        public void ServerNotifyReady()
        {
            if (!Net.IsServer || _ready2Tick) return;
            _readyCount++;
            Debug.Log($"[Timeline] 玩家 ready（{_readyCount}/{Net.Server.ClientIds.Count}）");
        }

        private void FixedUpdate()
        {
            if (!Net.IsServer || MainTimeline == null) return;

            if (!_ready2Tick)
            {
                int expected = Net.Server.ClientIds.Count;
                if (expected > 0 && _readyCount >= expected)
                {
                    _ready2Tick = true;
                    Debug.Log("[Timeline] Players are ready!");
                }
                else
                {
                    return;
                }
            }

            // 按配置的 tick 频率推进，而不是跟随 FixedUpdate 频率。
            _tickAccumulator += Time.fixedDeltaTime;
            while (_tickAccumulator >= GameSettings.TickFreq)
            {
                _tickAccumulator -= GameSettings.TickFreq;
                MainTimeline.Tick(MainTimeline.CurTick);
                SendObserversRpc(nameof(SyncTick), MainTimeline.CurTick);
                Ticked?.Invoke(MainTimeline.CurTick);
            }
        }

        [NetRpc]
        private void SyncTick(int tick)
        {
            // 服务器（Host）已自行推进，这里只处理纯客户端。
            if (Net.IsServer) return;
            MainTimeline.Tick(tick);
            Ticked?.Invoke(MainTimeline.CurTick);
        }
    }
}
