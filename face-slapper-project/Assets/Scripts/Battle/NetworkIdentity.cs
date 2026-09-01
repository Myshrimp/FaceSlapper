using FaceSlapper.Core;
using FaceSlapper.Match;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 联机玩家身份信息（队伍、网络 ID、颜色等，NetVar 同步）。
    /// 同时承载"击飞"网络链路：攻击者上报 → 服务器校验 → 受害者 Owner 执行受力。
    /// </summary>
    public class NetworkIdentity : NetBehaviour
    {
        [Header("身份信息")]
        public readonly NetVar<int> PlayerId = new NetVar<int>();
        public readonly NetVar<int> TeamId = new NetVar<int>();
        public readonly NetVar<string> PlayerName = new NetVar<string>();
        public readonly NetVar<int> ColorIndex = new NetVar<int>();

        [Header("状态")]
        [Tooltip("眩晕状态（服务器写，全端同步，驱动头顶星星表现）")]
        public readonly NetVar<bool> IsStunned = new NetVar<bool>();

        [Header("表现")]
        [Tooltip("按队伍/序号染色的渲染器（身体、手掌等）")]
        [SerializeField] private Renderer[] _tintRenderers;

        private static readonly Color[] Palette =
        {
            new Color(0.90f, 0.30f, 0.30f),
            new Color(0.30f, 0.60f, 0.95f),
            new Color(0.40f, 0.85f, 0.40f),
            new Color(0.95f, 0.80f, 0.30f),
            new Color(0.80f, 0.40f, 0.90f),
            new Color(0.95f, 0.50f, 0.20f),
        };

        protected override void Awake()
        {
            base.Awake();
            ColorIndex.OnChange += (prev, next) => ApplyColor();
            IsStunned.OnChange += (prev, next) => ApplyStunVisual(next);
        }

        public override void OnNetSpawnClient()
        {
            ApplyColor();
            // 后进玩家补应用当前眩晕状态。
            ApplyStunVisual(IsStunned.Value);
        }

        /// <summary>眩晕显隐表现（全端由 NetVar 变化驱动）。</summary>
        private void ApplyStunVisual(bool stunned)
        {
            var visual = GetComponent<StunVisual>();
            if (visual != null) visual.SetVisible(stunned);
        }

        /// <summary>受害者 Owner 上报眩晕状态变化，服务器写入 NetVar 广播全端。</summary>
        public void ReportStunned(bool stunned)
        {
            SendServerRpc(nameof(CmdStunned), stunned);
        }

        [NetRpc]
        private void CmdStunned(bool stunned)
        {
            // 仅服务器执行（ServerRpc 语义）。
            IsStunned.Value = stunned;
            // 眩晕开始时广播表现（音效/粒子）给所有观察者。
            if (stunned) SendObserversRpc(nameof(RpcStunFeedback), transform.position);
        }

        /// <summary>命中表现（全端）：发布 Fx 事件，由 HitFeedbackComponent 播音效/粒子。</summary>
        [NetRpc]
        private void RpcHitFeedback(Vector3 position, Vector3 direction, float force)
        {
            EventBus.Publish(new PlayerHitFxEvent { Position = position, Direction = direction, Force = force });
        }

        /// <summary>眩晕表现（全端）：发布 Fx 事件，由 HitFeedbackComponent 播音效/粒子。</summary>
        [NetRpc]
        private void RpcStunFeedback(Vector3 position)
        {
            EventBus.Publish(new PlayerStunFxEvent { Position = position });
        }

        private void ApplyColor()
        {
            if (_tintRenderers == null) return;
            Color color = Palette[Mathf.Abs(ColorIndex.Value) % Palette.Length];
            foreach (Renderer r in _tintRenderers)
            {
                if (r != null) r.material.color = color;
            }
        }

        /// <summary>
        /// 攻击者上报一次命中（任何端可发起）。服务器做距离校验后转发给受害者 Owner 执行。
        /// </summary>
        public void ReportHit(int victimNetId, Vector3 direction, float force, float maxRange)
        {
            SendServerRpc(nameof(CmdHit), victimNetId, direction, force, maxRange);
        }

        [NetRpc]
        private void CmdHit(int victimNetId, Vector3 direction, float force, float maxRange)
        {
            // 仅服务器执行（ServerRpc 语义）。
            if (!Net.Server.TryGetObject(victimNetId, out NetObject victim)) return;
            if (victim == NetObject) return;

            // 服务器端位置是各 Owner 同步而来的，做宽松距离校验以容忍同步延迟。
            float dist = Vector3.Distance(victim.transform.position, transform.position);
            if (dist > maxRange + 2f) return;

            var victimIdentity = victim.GetComponent<NetworkIdentity>();
            if (victimIdentity == null || victim.OwnerClientId < 0) return;

            victimIdentity.SendTargetRpc(victim.OwnerClientId, nameof(TargetApplyKnockback), direction, force);

            // 广播命中表现（音效/粒子）给所有观察者。
            SendObserversRpc(nameof(RpcHitFeedback), victim.transform.position, direction, force);
        }

        /// <summary>在受害者的 Owner 端执行击飞（该端是移动的权威端）。</summary>
        [NetRpc]
        private void TargetApplyKnockback(Vector3 direction, float force)
        {
            var movement = GetComponent<Movement>();
            if (movement != null) movement.ApplyLaunch(LaunchEffect.Slap(direction, force));
            EventBus.Publish(new PlayerHitEvent { VictimNetId = NetObj.NetId, Direction = direction, Force = force });
        }

        /// <summary>
        /// 攻击者上报一次重击击飞（拳套）：带竖直分量与滞空时间。
        /// 任何端可发起，服务器做距离校验后转发给受害者 Owner 执行。
        /// </summary>
        public void ReportLaunch(int victimNetId, Vector3 direction, float force, float upRatio, float airTime, float maxRange, float stunDuration)
        {
            SendServerRpc(nameof(CmdLaunch), victimNetId, direction, force, upRatio, airTime, maxRange, stunDuration);
        }

        [NetRpc]
        private void CmdLaunch(int victimNetId, Vector3 direction, float force, float upRatio, float airTime, float maxRange, float stunDuration)
        {
            // 仅服务器执行（ServerRpc 语义）。
            if (!Net.Server.TryGetObject(victimNetId, out NetObject victim)) return;
            if (victim == NetObject) return;

            // 服务器端位置是各 Owner 同步而来的，做宽松距离校验以容忍同步延迟。
            float dist = Vector3.Distance(victim.transform.position, transform.position);
            if (dist > maxRange + 2f) return;

            var victimIdentity = victim.GetComponent<NetworkIdentity>();
            if (victimIdentity == null || victim.OwnerClientId < 0) return;

            victimIdentity.SendTargetRpc(victim.OwnerClientId, nameof(TargetApplyLaunch), direction, force, upRatio, airTime, stunDuration);

            // 广播命中表现（音效/粒子）给所有观察者。
            SendObserversRpc(nameof(RpcHitFeedback), victim.transform.position, direction, force);
        }

        /// <summary>在受害者的 Owner 端执行重击击飞（该端是移动的权威端）。</summary>
        [NetRpc]
        private void TargetApplyLaunch(Vector3 direction, float force, float upRatio, float airTime, float stunDuration)
        {
            var movement = GetComponent<Movement>();
            if (movement != null)
                movement.ApplyLaunch(LaunchEffect.GlovePunch(direction, force, upRatio, airTime, stunDuration));
            EventBus.Publish(new PlayerHitEvent { VictimNetId = NetObj.NetId, Direction = direction, Force = force });
        }

        /// <summary>
        /// 服务器端转发入口（武器侧 EffectManager 的独立同步链路复用）：
        /// 把轻击退转发给本对象的 Owner 端执行。仅服务器调用。
        /// </summary>
        internal void ServerForwardKnockback(Vector3 direction, float force)
        {
            SendTargetRpc(NetObj.OwnerClientId, nameof(TargetApplyKnockback), direction, force);
        }

        /// <summary>
        /// 服务器端转发入口（武器侧 EffectManager 的独立同步链路复用）：
        /// 把重击击飞转发给本对象的 Owner 端执行。仅服务器调用。
        /// </summary>
        internal void ServerForwardLaunch(Vector3 direction, float force, float upRatio, float airTime, float stunDuration)
        {
            SendTargetRpc(NetObj.OwnerClientId, nameof(TargetApplyLaunch), direction, force, upRatio, airTime, stunDuration);
        }
    }
}
