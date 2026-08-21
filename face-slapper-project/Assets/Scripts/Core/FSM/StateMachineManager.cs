using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 局内状态机统一管理组件：集中注册并统一驱动所有实体的状态机，
    /// 避免每个实体各自 Update 的零散调用。支持全局暂停与局结束一键清理。
    /// 由 GameEntry 注册，经 GameManager 统一分发 Update / FixedUpdate / LateUpdate。
    /// </summary>
    public class StateMachineManager : MonoBehaviour, IGameComponent, IUpdatable, IFixedUpdatable, ILateUpdatable
    {
        private readonly List<StateMachineComponent> _machines = new List<StateMachineComponent>(32);

        /// <summary>全局暂停。暂停时所有实体状态机停表。</summary>
        public bool Paused { get; private set; }

        /// <summary>当前注册的状态机数量。</summary>
        public int Count => _machines.Count;

        public void OnInit() { }

        public void OnShutdown()
        {
            _machines.Clear();
            Paused = false;
        }

        /// <summary>注册实体状态机（幂等）。一般由 StateMachineComponent.OnEnable 自动调用。</summary>
        public void Register(StateMachineComponent component)
        {
            if (component == null || _machines.Contains(component)) return;
            _machines.Add(component);
        }

        /// <summary>反注册。一般由 StateMachineComponent.OnDisable 自动调用。</summary>
        public void Unregister(StateMachineComponent component)
        {
            if (component == null) return;
            _machines.Remove(component);
        }

        /// <summary>全局暂停/恢复（对局暂停菜单、结算展示等场景使用）。</summary>
        public void SetPaused(bool paused) => Paused = paused;

        /// <summary>局结束时调用：清空全部注册（实体即将销毁或回池）。</summary>
        public void ClearAll() => _machines.Clear();

        public void OnUpdate(float deltaTime)
        {
            if (Paused) return;
            for (int i = 0; i < _machines.Count; i++)
            {
                StateMachineComponent machine = _machines[i];
                if (machine == null) // 实体已销毁但未走 OnDisable 反注册，惰性清理
                {
                    _machines.RemoveAt(i--);
                    continue;
                }
                machine.TickUpdate(deltaTime);
            }
        }

        public void OnFixedUpdate(float deltaTime)
        {
            if (Paused) return;
            for (int i = 0; i < _machines.Count; i++)
            {
                StateMachineComponent machine = _machines[i];
                if (machine == null) continue;
                machine.TickFixedUpdate(deltaTime);
            }
        }

        public void OnLateUpdate(float deltaTime)
        {
            if (Paused) return;
            for (int i = 0; i < _machines.Count; i++)
            {
                StateMachineComponent machine = _machines[i];
                if (machine == null) continue;
                machine.TickLateUpdate(deltaTime);
            }
        }
    }
}
