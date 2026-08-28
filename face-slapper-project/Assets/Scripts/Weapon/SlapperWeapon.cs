using FaceSlapper.Battle;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 初始武器"大巴掌/拍子"：
    /// 攻击由状态机驱动（见 WeaponBase），挥动动画由 WeaponAnimComponent 承担，
    /// 本类只保留命中参数与命中检测。击飞经服务器校验后执行。
    /// </summary>
    public class SlapperWeapon : WeaponBase
    {
        [Header("命中")]
        [SerializeField] private float _force = 12f;
        [SerializeField] private float _hitRadius = 1.4f;

        protected override void DoHitCheck()
        {
            NetObject holder = FindHolder();
            if (holder == null) return;

            NetworkIdentity attacker = holder.GetComponent<NetworkIdentity>();
            if (attacker == null) return;

            Vector3 center = _tip != null
                ? _tip.position
                : holder.transform.position + Vector3.up + holder.transform.forward * 1.2f;

            Collider[] hits = Physics.OverlapSphere(center, _hitRadius);
            foreach (Collider hit in hits)
            {
                NetObject nob = hit.GetComponentInParent<NetObject>();
                if (nob == null || nob == holder) continue;

                NetworkIdentity victim = nob.GetComponent<NetworkIdentity>();
                if (victim == null) continue;

                Vector3 dir = nob.transform.position - holder.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = holder.transform.forward;
                dir.Normalize();

                attacker.ReportHit(nob.NetId, dir, _force, 6f);
                OnHitPlayer(victim);

                // 击飞位移预测：本地命中立即表现（与拳套共享 LaunchEffect 流程，预测位移不预测状态）。
                Movement victimMove = nob.GetComponent<Movement>();
                if (victimMove != null) victimMove.PredictLaunch(LaunchEffect.Slap(dir, _force));
            }
        }
    }
}
