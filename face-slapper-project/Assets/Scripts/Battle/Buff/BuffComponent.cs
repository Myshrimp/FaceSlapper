using System.Collections.Generic;
using FaceSlapper.TL;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// Buff 管理组件：挂在角色身上，维护 Buff 列表并驱动其生命周期。
    /// 角色使用 Ability 时由 AbilityComponent 回调 OnAbilityUsed，
    /// Buff 据此为角色提供增益（技能威力倍率见 GetPowerMultiplier）。
    /// Buff 倒计时统一由场景级 TimelineManager 的权威 tick 驱动（见 Ticked 订阅）。
    /// </summary>
    public class BuffComponent : MonoBehaviour
    {
        private readonly List<IBuff> _buffs = new List<IBuff>(8);
        private bool _subscribed;

        /// <summary>当前生效的 Buff 列表（只读）。</summary>
        public IReadOnlyList<IBuff> Buffs => _buffs;

        private void OnEnable() => TrySubscribeTick();

        private void OnDisable() => UnsubscribeTick();

        /// <summary>TimelineManager 为场景对象，正常先于本组件存在；仍做一次空值守卫与懒重试。</summary>
        private void TrySubscribeTick()
        {
            if (_subscribed || TimelineManager.Instance == null) return;
            TimelineManager.Instance.Ticked += OnTimelineTick;
            _subscribed = true;
        }

        private void UnsubscribeTick()
        {
            if (!_subscribed) return;
            if (TimelineManager.Instance != null)
                TimelineManager.Instance.Ticked -= OnTimelineTick;
            _subscribed = false;
        }

        /// <summary>服务器权威 tick：驱动所有 Buff 的倒计时。</summary>
        private void OnTimelineTick(int tick)
        {
            for (int i = 0; i < _buffs.Count; i++)
            {
                if (_buffs[i] is BuffBase buff)
                    buff.TickDown();
            }
        }

        /// <summary>
        /// 添加一个 Buff；同类型 Buff 已存在时刷新其持续时间并返回已存在的实例。
        /// </summary>
        public T AddBuff<T>(T buff) where T : class, IBuff
        {
            if (buff == null) return null;

            IBuff existing = _buffs.Find(b => b != null && b.GetType() == buff.GetType());
            if (existing != null)
            {
                if (existing is BuffBase buffBase) buffBase.Refresh();
                return existing as T;
            }

            _buffs.Add(buff);
            buff.OnAttach(this);
            return buff;
        }

        /// <summary>移除指定 Buff。</summary>
        public bool RemoveBuff(IBuff buff)
        {
            if (buff == null || !_buffs.Remove(buff)) return false;
            buff.OnDetach();
            return true;
        }

        /// <summary>是否持有指定类型的 Buff。</summary>
        public bool HasBuff<T>() where T : IBuff
        {
            for (int i = 0; i < _buffs.Count; i++)
                if (_buffs[i] is T) return true;
            return false;
        }

        /// <summary>技能威力总倍率（所有有效 Buff 的乘积，无 Buff 时为 1）。</summary>
        public float GetPowerMultiplier()
        {
            float multiplier = 1f;
            for (int i = 0; i < _buffs.Count; i++)
            {
                if (_buffs[i] is BuffBase buff && buff.IsValid())
                    multiplier *= buff.PowerMultiplier;
            }
            return multiplier;
        }

        /// <summary>角色使用技能时由 AbilityComponent 调用，触发所有 Buff 的 OnUse 增益。</summary>
        public void OnAbilityUsed(IAbility ability)
        {
            for (int i = 0; i < _buffs.Count; i++)
            {
                if (_buffs[i] != null && _buffs[i].IsValid())
                    _buffs[i].OnUse();
            }
        }

        private void Update()
        {
            if (!_subscribed) TrySubscribeTick();

            // 清理已失效的 Buff。
            for (int i = _buffs.Count - 1; i >= 0; i--)
            {
                if (_buffs[i] == null || !_buffs[i].IsValid())
                {
                    _buffs[i]?.OnDetach();
                    _buffs.RemoveAt(i);
                }
            }
        }
    }
}
