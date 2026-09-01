using FaceSlapper.Battle;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 命中急停效果：持有者在冲刺中命中敌人时立刻停下（末日铁拳手感）。
    /// 持有者不在冲刺时调用无害。
    /// </summary>
    public class StopHolderDashEffect : WeaponEffect
    {
        public override void Apply(EffectContext ctx)
        {
            Movement holderMove = ctx.Holder != null ? ctx.Holder.GetComponent<Movement>() : null;
            if (holderMove != null) holderMove.EndDash();
        }
    }
}
