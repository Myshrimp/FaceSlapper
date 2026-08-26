using FaceSlapper.Core;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 武器状态机组件：待机 / 攻击 两态。
    /// 攻击切换由 WeaponBase 的攻击序号 NetVar 广播驱动（全端一致切入 Attack），
    /// 攻击动画播完自动回到待机。由 StateMachineManager 局内统一驱动。
    /// </summary>
    [RequireComponent(typeof(WeaponBase))]
    public class WeaponFsmComponent : StateMachineComponent
    {
        protected override void BuildStateMachine(StateMachine root)
        {
            var attack = root.AddState(new WeaponAttackState());
            root.AddState(new WeaponIdleState());
            root.SetInitial(WeaponIdleState.StateName);

            // 待机 -> 攻击：收到攻击触发器（攻击序号广播）。
            root.AddTriggerTransition(WeaponIdleState.StateName, WeaponBase.AttackTrigger, WeaponAttackState.StateName);
            // 攻击 -> 待机：攻击动画播完（条件转换）。
            root.AddTransition(WeaponAttackState.StateName, WeaponIdleState.StateName, () => attack.IsFinished);
        }
    }
}
