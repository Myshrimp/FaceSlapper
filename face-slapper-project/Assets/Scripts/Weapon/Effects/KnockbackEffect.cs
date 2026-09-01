using FaceSlapper.Battle;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 轻击退效果（大巴掌/拍子）：目标被小力度击退，不进入重击飞行、不触发撞墙眩晕。
    /// 权威链路：EffectManager 上报 → 服务器校验 → 受害者 Owner 执行；
    /// 本地立即做位移预测（只预测位移，不预测状态）。
    /// </summary>
    public class KnockbackEffect : WeaponEffect
    {
        [Tooltip("击退力度（速度变化量）。")]
        [SerializeField] private float _force = 12f;

        [Tooltip("服务器端命中距离校验上限（米）。")]
        [SerializeField] private float _serverMaxRange = 6f;

        public override void Apply(EffectContext ctx)
        {
            ctx.Manager.ReportHit(ctx.Victim.NetId, ctx.Direction, _force, _serverMaxRange);

            // 击飞位移预测：本地命中立即表现敌人位移。
            Movement victimMove = ctx.Victim.GetComponent<Movement>();
            if (victimMove != null)
                victimMove.PredictLaunch(LaunchEffect.Slap(ctx.Direction, _force));
        }
    }
}
