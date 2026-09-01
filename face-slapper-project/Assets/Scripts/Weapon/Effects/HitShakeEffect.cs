using FaceSlapper.Camera;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 命中相机抖动效果（攻击者本端）：命中敌人时相机强烈抖动，
    /// 强度随 Power（蓄力等级）在 min/max 间插值。
    /// </summary>
    public class HitShakeEffect : WeaponEffect
    {
        [Tooltip("抖动强度（蓄力 0 / 满 之间插值）。")]
        [SerializeField] private float _minIntensity = 0.6f;
        [SerializeField] private float _maxIntensity = 1f;

        public override void Apply(EffectContext ctx)
        {
            if (PlayerCameraRig.Instance != null)
                PlayerCameraRig.Instance.HitShake(Mathf.Lerp(_minIntensity, _maxIntensity, ctx.Power));
        }
    }
}
