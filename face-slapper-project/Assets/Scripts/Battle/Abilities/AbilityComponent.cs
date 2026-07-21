using System.Collections.Generic;
using FaceSlapper.Core;
using FaceSlapper.Input;
using FaceSlapper.Networking;
using FaceSlapper.Weapon;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 技能管理组件：收集玩家身上的所有 IAbility，并把输入映射为技能触发。
    /// 仅 Owner 端处理输入；技能的网络效果由各技能内部经 NetRpc 完成。
    /// </summary>
    [RequireComponent(typeof(NetObject))]
    public class AbilityComponent : MonoBehaviour
    {
        private readonly List<IAbility> _abilities = new List<IAbility>(8);
        private NetObject _netObject;
        private PickWeaponAbility _pickWeapon;
        private SpeedUpAbility _speedUp;

        /// <summary>当前持有的武器（可能为 null）。</summary>
        public WeaponBase HeldWeapon => _pickWeapon != null ? _pickWeapon.HeldWeapon : null;

        private void Awake()
        {
            _netObject = GetComponent<NetObject>();
            GetComponents(_abilities);
            foreach (IAbility ability in _abilities)
                ability.OnActivate(this);

            _pickWeapon = GetComponent<PickWeaponAbility>();
            _speedUp = GetComponent<SpeedUpAbility>();
        }

        private void OnDestroy()
        {
            foreach (IAbility ability in _abilities)
                ability.OnDeactivate();
            _abilities.Clear();
        }

        /// <summary>按名字触发技能（受冷却限制）。</summary>
        public bool UseAbility(string abilityName)
        {
            IAbility ability = _abilities.Find(a => a.GetName() == abilityName);
            if (ability == null || !ability.CanUse) return false;
            ability.OnUse();
            return true;
        }

        public T GetAbility<T>() where T : class, IAbility => GetComponent<T>();

        private void Update()
        {
            if (_netObject == null || !_netObject.IsOwner) return;
            if (!GameManager.HasInstance) return;
            InputComponent input = GameManager.Instance.Get<InputComponent>();
            if (input == null) return;

            InputSnapshot snapshot = input.Current;

            // 攻击：仅在持有武器且拥有武器所有权时生效。
            if (snapshot.AttackPressed && HeldWeapon != null && HeldWeapon.IsOwner)
                HeldWeapon.OnAttack();

            // 击飞技能（空手"耳光"）。
            if (snapshot.HitbackPressed)
                UseAbility("Hitback");

            // 拾取/放下武器。
            if (snapshot.PickupPressed)
                UseAbility("PickWeapon");

            // 加速：按住生效，松开结束。
            if (_speedUp != null)
            {
                if (snapshot.SpeedUpHeld)
                {
                    if (!_speedUp.IsActive && _speedUp.CanUse)
                        _speedUp.OnUse();
                }
                else
                {
                    _speedUp.OnUseEnd();
                }
            }
        }
    }
}
