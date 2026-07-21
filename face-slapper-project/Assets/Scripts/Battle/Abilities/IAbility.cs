using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 技能接口。技能以 MonoBehaviour 形式挂在玩家物体上，由 AbilityComponent 统一管理。
    /// </summary>
    public interface IAbility
    {
        /// <summary>技能名（用于 UseAbility 查找）。</summary>
        string GetName();

        /// <summary>冷却时间（秒）。</summary>
        float GetCoolDown();

        /// <summary>当前是否可用（冷却完毕）。</summary>
        bool CanUse { get; }

        /// <summary>被 AbilityComponent 装备/启用时调用。</summary>
        void OnActivate(AbilityComponent owner);

        /// <summary>被 AbilityComponent 卸下/禁用时调用。</summary>
        void OnDeactivate();

        /// <summary>触发技能。</summary>
        void OnUse();

        /// <summary>技能结束（松开按键/持续时间到）。</summary>
        void OnUseEnd();
    }

    /// <summary>技能基类：处理冷却计时的通用样板。</summary>
    public abstract class AbilityBase : MonoBehaviour, IAbility
    {
        protected float _lastUseTime = float.NegativeInfinity;

        /// <summary>拥有该技能的组件。</summary>
        protected AbilityComponent Owner { get; private set; }

        public abstract float GetCoolDown();

        public virtual string GetName() => GetType().Name.Replace("Ability", string.Empty);

        public virtual bool CanUse => Time.time - _lastUseTime >= GetCoolDown();

        public virtual void OnActivate(AbilityComponent owner) => Owner = owner;

        public virtual void OnDeactivate() => Owner = null;

        public virtual void OnUse() => _lastUseTime = Time.time;

        public virtual void OnUseEnd() { }
    }
}
