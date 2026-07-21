using FaceSlapper.Battle;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 初始武器"大巴掌/拍子"：
    /// 攻击时本地做挥动表现（随 NetTransformSync 同步给其他端），
    /// 并对尖端范围内的玩家做命中检测，击飞经服务器校验后执行。
    /// </summary>
    public class SlapperWeapon : WeaponBase
    {
        [Header("攻击")]
        [SerializeField] private float _force = 12f;
        [SerializeField] private float _hitRadius = 1.4f;
        [SerializeField] private float _attackInterval = 0.6f;

        [Header("挥动表现")]
        [SerializeField] private float _swingTime = 0.22f;
        [SerializeField] private float _swingAngle = -80f;

        private float _lastAttackTime = float.NegativeInfinity;
        private float _swingTimer = -1f;

        public override void OnAttack()
        {
            if (!IsHeld || !IsOwner) return;
            if (Time.time - _lastAttackTime < _attackInterval) return;

            _lastAttackTime = Time.time;
            _swingTimer = 0f;
            DoHitCheck();
        }

        protected override void Update()
        {
            base.Update(); // 先跟随手部挂点。

            // 挥动表现：在跟随之后叠加一个绕持有者右轴的摆动角。
            if (_swingTimer >= 0f && IsHeld && IsOwner)
            {
                _swingTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(_swingTimer / _swingTime);
                transform.rotation *= Quaternion.Euler(Mathf.Sin(progress * Mathf.PI) * _swingAngle, 0f, 0f);
                if (progress >= 1f) _swingTimer = -1f;
            }
        }

        private void DoHitCheck()
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
            }
        }
    }
}
