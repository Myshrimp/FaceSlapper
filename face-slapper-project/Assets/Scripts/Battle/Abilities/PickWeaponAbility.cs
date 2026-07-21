using FaceSlapper.Networking;
using FaceSlapper.Weapon;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 拾取武器技能：拾取身边最近的一把闲置武器；已持有时再次触发则放下。
    /// 归属变更由服务器完成（WeaponBase.CmdPickup/CmdDrop）。
    /// </summary>
    public class PickWeaponAbility : AbilityBase
    {
        [SerializeField] private float _coolDown = 0.3f;
        [SerializeField] private float _pickupRange = 2.5f;

        /// <summary>当前持有的武器（由 WeaponBase 在归属同步后回写）。</summary>
        public WeaponBase HeldWeapon { get; private set; }

        public override float GetCoolDown() => _coolDown;

        /// <summary>由 WeaponBase 调用，同步持有状态。</summary>
        public void SetHeld(WeaponBase weapon) => HeldWeapon = weapon;

        public override void OnUse()
        {
            base.OnUse();

            if (HeldWeapon != null)
            {
                HeldWeapon.RequestDrop();
                return;
            }

            NetObject self = GetComponent<NetObject>();
            WeaponBase nearest = FindNearestFreeWeapon();
            if (nearest != null && self != null)
                nearest.RequestPickup(self.NetId);
        }

        private WeaponBase FindNearestFreeWeapon()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position + Vector3.up, _pickupRange);
            WeaponBase best = null;
            float bestSqrDist = float.MaxValue;

            foreach (Collider hit in hits)
            {
                WeaponBase weapon = hit.GetComponentInParent<WeaponBase>();
                if (weapon == null || weapon.IsHeld) continue;

                float sqrDist = (weapon.transform.position - transform.position).sqrMagnitude;
                if (sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    best = weapon;
                }
            }
            return best;
        }
    }
}
