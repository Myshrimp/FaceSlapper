using FaceSlapper.Battle;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 重击击飞效果（拳套蓄力冲拳）：力度随 Power（蓄力等级）在 min/max 间插值，
    /// 目标进入重击飞行，飞行途中撞墙陷入眩晕。
    /// 权威链路：EffectManager 上报 → 服务器校验 → 受害者 Owner 执行；
    /// 本地立即做位移预测（与权威执行共享 LaunchEffect 参数，两端手感一致）。
    /// </summary>
    public class LaunchKnockbackEffect : WeaponEffect
    {
        [Tooltip("击飞力度（蓄力 0 / 满 之间插值）。")]
        [SerializeField] private float _minForce = 10f;
        [SerializeField] private float _maxForce = 22f;

        [Tooltip("竖直分量比例（越大抛得越高）。")]
        [SerializeField] private float _upRatio = 0.8f;

        [Tooltip("失控/滞空时间（撞障碍判定窗口，秒）。")]
        [SerializeField] private float _airTime = 0.8f;

        [Tooltip("撞障碍后的眩晕时长（秒）。")]
        [SerializeField] private float _stunDuration = 1.6f;

        [Tooltip("服务器端命中距离校验上限（米）。")]
        [SerializeField] private float _serverMaxRange = 3.2f;

        public override void Apply(EffectContext ctx)
        {
            float force = Mathf.Lerp(_minForce, _maxForce, ctx.Power);

            ctx.Manager.ReportLaunch(ctx.Victim.NetId, ctx.Direction, force,
                _upRatio, _airTime, _serverMaxRange, _stunDuration);

            // 击飞位移预测：本地命中立即让敌人镜像飞出（只预测位移，不预测状态）。
            Movement victimMove = ctx.Victim.GetComponent<Movement>();
            if (victimMove != null)
                victimMove.PredictLaunch(LaunchEffect.GlovePunch(ctx.Direction, force,
                    _upRatio, _airTime, _stunDuration));
        }
    }
}
