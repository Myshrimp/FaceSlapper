using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 击飞效果（技能效果类）：普通拍击与拳套重击的统一抽象。
    /// 只描述"如何被击飞"（方向/力度/竖直分量/滞空/撞墙眩晕），
    /// 权威执行（Owner，Movement.ApplyLaunch）与位移预测（非 Owner，Movement.PredictLaunch）
    /// 共享同一组参数，保证两端手感一致。
    /// </summary>
    public class LaunchEffect
    {
        /// <summary>击飞方向（取水平分量，零向量时用目标朝向兜底）。</summary>
        public Vector3 Direction;

        /// <summary>击飞力度（速度变化量）。</summary>
        public float Force;

        /// <summary>竖直分量比例（越大抛得越高）。</summary>
        public float UpRatio;

        /// <summary>失控/滞空时间（秒），重击时也是撞墙眩晕的判定窗口。</summary>
        public float AirTime;

        /// <summary>撞墙眩晕时长（秒）；&lt;=0 表示该击飞不触发撞墙眩晕。</summary>
        public float StunDuration;

        /// <summary>史莱姆受击拉伸脉冲强度。</summary>
        public float SquashIntensity;

        /// <summary>重击：进入 Launched 状态并允许撞墙眩晕；轻击退留在正常状态走弱空控。</summary>
        public bool Heavy;

        /// <summary>普通拍击（轻击退）：不进入 Launched 状态、不触发撞墙眩晕。</summary>
        public static LaunchEffect Slap(Vector3 direction, float force) => new LaunchEffect
        {
            Direction = direction,
            Force = force,
            UpRatio = 0.5f,
            AirTime = 0.35f,
            StunDuration = 0f,
            SquashIntensity = 0.5f,
            Heavy = false,
        };

        /// <summary>拳套重击（蓄力冲拳）：进入 Launched 状态，飞行中撞墙陷入眩晕。</summary>
        public static LaunchEffect GlovePunch(Vector3 direction, float force, float upRatio, float airTime, float stunDuration) => new LaunchEffect
        {
            Direction = direction,
            Force = force,
            UpRatio = upRatio,
            AirTime = airTime,
            StunDuration = stunDuration,
            SquashIntensity = 0.9f,
            Heavy = true,
        };

        /// <summary>计算击飞冲量（速度变化量向量）。</summary>
        public Vector3 Impulse(Vector3 fallbackForward)
        {
            Vector3 flat = Direction;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.001f) flat = fallbackForward;
            flat.Normalize();
            return (flat + Vector3.up * UpRatio).normalized * Force;
        }
    }
}
