using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 武器效果基类（组件化）：
    /// 武器的行为 = 检测器（HitDetector）+ 若干 WeaponEffect 的组合，
    /// 由 WeaponEffectManager 在命中时统一逐个应用。
    /// 新武器只需在 Prefab 上挑选/调整组件，无需改代码（除非引入新行为）。
    /// </summary>
    public abstract class WeaponEffect : MonoBehaviour
    {
        /// <summary>对单个命中目标应用效果（仅攻击者 Owner 端调用）。</summary>
        public abstract void Apply(EffectContext ctx);
    }
}
