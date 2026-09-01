using FaceSlapper.Battle;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 命中上下文：WeaponEffectManager 对每个命中目标构建一份，
    /// 传递给武器上所有 WeaponEffect。效果只读上下文，不持有跨帧状态。
    /// </summary>
    public class EffectContext
    {
        /// <summary>效果管理器（效果经它做网络上报/同步）。</summary>
        public WeaponEffectManager Manager;

        /// <summary>武器持有者。</summary>
        public NetObject Holder;

        /// <summary>持有者的玩家身份信息。</summary>
        public NetworkIdentity Attacker;

        /// <summary>命中的目标对象。</summary>
        public NetObject Victim;

        /// <summary>水平命中方向（持有者 → 目标，已归一化）。</summary>
        public Vector3 Direction;

        /// <summary>
        /// 效果强度 0-1（蓄力武器的蓄力等级，非蓄力武器恒为 0）。
        /// 需要随蓄力缩放的效果自行用 Mathf.Lerp(min, max, Power) 插值。
        /// </summary>
        public float Power;

        /// <summary>命中点（表现/特效定位用）。</summary>
        public Vector3 HitPoint;
    }
}
