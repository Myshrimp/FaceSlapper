using FaceSlapper.Core;
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
        }

        public override void OnNetSpawnClient() => ApplyColor();

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
        }

        /// <summary>在受害者的 Owner 端执行击飞（该端是移动的权威端）。</summary>
        [NetRpc]
        private void TargetApplyKnockback(Vector3 direction, float force)
        {
            var movement = GetComponent<Movement>();
            if (movement != null) movement.ApplyKnockback(direction, force);
            EventBus.Publish(new PlayerHitEvent { VictimNetId = NetObj.NetId, Direction = direction, Force = force });
        }
    }
}
