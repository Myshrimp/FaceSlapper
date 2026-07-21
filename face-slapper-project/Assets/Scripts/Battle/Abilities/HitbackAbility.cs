using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 击飞技能：空手"耳光"，对面前短距离内的玩家施加小力度击飞。
    /// 命中检测在使用者本地进行，击飞效果经 NetworkIdentity.ReportHit 由服务器校验后执行。
    /// </summary>
    public class HitbackAbility : AbilityBase
    {
        [SerializeField] private float _coolDown = 3f;
        [SerializeField] private float _range = 2.2f;
        [SerializeField] private float _radius = 1.1f;
        [SerializeField] private float _force = 8f;

        public override float GetCoolDown() => _coolDown;

        public override void OnUse()
        {
            base.OnUse();

            // 播放耳光动画（经 AnimComponent 同步到所有端）。
            var anim = GetComponent<AnimComponent>();
            if (anim != null) anim.PlaySlap();

            NetworkIdentity self = GetComponent<NetworkIdentity>();
            if (self == null) return;

            Transform t = transform;
            Vector3 center = t.position + Vector3.up * 1f + t.forward * (_range * 0.5f);
            Collider[] hits = Physics.OverlapSphere(center, _radius);

            foreach (Collider hit in hits)
            {
                NetObject nob = hit.GetComponentInParent<NetObject>();
                if (nob == null || nob == self.NetObject) continue;
                if (nob.GetComponent<NetworkIdentity>() == null) continue;

                Vector3 dir = nob.transform.position - t.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = t.forward;
                dir.Normalize();

                self.ReportHit(nob.NetId, dir, _force, _range + 1.5f);
            }
        }
    }
}
