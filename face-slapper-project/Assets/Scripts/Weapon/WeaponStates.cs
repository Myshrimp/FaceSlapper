using FaceSlapper.Core;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>武器待机状态：无行为，等待攻击触发器（攻击序号广播）。</summary>
    public class WeaponIdleState : FsmState
    {
        public const string StateName = "Idle";

        public WeaponIdleState() : base(StateName) { }
    }

    /// <summary>
    /// 武器攻击状态：进入时通知武器播动画（全端）并做命中检测（Owner 端），
    /// 持续 WeaponBase.AttackDuration 秒后由条件转换自动回到待机。
    /// </summary>
    public class WeaponAttackState : FsmState
    {
        public const string StateName = "Attack";

        private WeaponBase _weapon;
        private float _enterTime;

        public WeaponAttackState() : base(StateName) { }

        public override void OnEnter()
        {
            _enterTime = Time.time;
            if (_weapon == null && Machine != null && Machine.Owner != null)
                _weapon = Machine.Owner.GetComponent<WeaponBase>();
            if (_weapon != null) _weapon.HandleAttackStateEnter();
        }

        /// <summary>攻击状态期间每物理帧回调武器（拳套冲刺中的持续命中检测用）。</summary>
        public override void OnFixedUpdate(float deltaTime)
        {
            if (_weapon != null) _weapon.HandleAttackStateFixedUpdate();
        }

        /// <summary>攻击状态是否结束（供条件转换每帧求值）。</summary>
        public bool IsFinished =>
            _weapon == null || Time.time - _enterTime >= _weapon.AttackDuration;
    }

    public class PulseGloveChargeUpState : FsmState
    {
        public const string StateName = "PulseGloveChargeUpState";
        private WeaponBase _weapon;
        private float _enterTime;

        public PulseGloveChargeUpState() : base(StateName) { }
        public override void OnEnter()
        {
            _enterTime = Time.time;
            if (_weapon == null && Machine != null && Machine.Owner != null)
                _weapon = Machine.Owner.GetComponent<WeaponBase>();
            if (_weapon != null) _weapon.HandleAttackStateEnter();
        }

        /// <summary>攻击状态是否结束（供条件转换每帧求值）。</summary>
        public bool IsFinished =>
            _weapon == null || Time.time - _enterTime >= _weapon.AttackDuration;
    }
}
