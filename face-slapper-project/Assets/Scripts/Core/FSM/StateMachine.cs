using System;
using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 分层状态机（单层级）。
    /// 实体持有的根机由 StateMachineComponent / StateMachineManager 驱动；
    /// 作为子机时由父状态级联驱动（见 FsmState.EnterCascade 等）。
    /// 支持：条件转换（Update 前每帧求值）、触发器转换（Fire 沿激活分支逐层下发）、
    /// 任意态转换（from 传 null）。
    /// </summary>
    public class StateMachine
    {
        /// <summary>转换规则。Condition 与 Trigger 二选一。</summary>
        private struct Transition
        {
            public string From;             // null = 任意状态
            public string To;
            public Func<bool> Condition;    // 条件转换（每帧求值）
            public string Trigger;          // 触发器转换（Fire 时匹配）
        }

        /// <summary>状态机名（调试用）。</summary>
        public string Name { get; }

        /// <summary>拥有根机的实体组件（子机与根机共享同一 Owner）。</summary>
        public StateMachineComponent Owner { get; }

        /// <summary>父状态。本机为根机时为 null。</summary>
        public FsmState ParentState { get; }

        public FsmState CurrentState { get; private set; }
        public FsmState PreviousState { get; private set; }

        private readonly Dictionary<string, FsmState> _states = new Dictionary<string, FsmState>(16);
        private readonly List<Transition> _transitions = new List<Transition>(8);
        private string _initialStateName;
        private bool _entered;

        public StateMachine(string name, StateMachineComponent owner, FsmState parentState = null)
        {
            Name = name;
            Owner = owner;
            ParentState = parentState;
        }

        /// <summary>注册状态（幂等，重名返回已注册实例）。</summary>
        public T AddState<T>(T state) where T : FsmState
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (_states.TryGetValue(state.Name, out FsmState existing)) return (T)existing;
            state.Machine = this;
            _states.Add(state.Name, state);
            return state;
        }

        /// <summary>获取已注册状态，不存在返回 null。</summary>
        public FsmState GetState(string stateName)
        {
            return _states.TryGetValue(stateName, out FsmState state) ? state : null;
        }

        /// <summary>设置初始状态（进入状态机时的默认状态）。</summary>
        public void SetInitial(string stateName)
        {
            if (!_states.ContainsKey(stateName))
                throw new InvalidOperationException($"[FSM] 状态机 {Name} 中不存在初始状态 {stateName}。");
            _initialStateName = stateName;
        }

        /// <summary>条件转换：from 传 null 表示任意状态。condition 为 true 即切换（每帧至多切换一次）。</summary>
        public void AddTransition(string from, string to, Func<bool> condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            _transitions.Add(new Transition { From = from, To = to, Condition = condition });
        }

        /// <summary>触发器转换：收到 trigger 且当前处于 from（null=任意）时切换。</summary>
        public void AddTriggerTransition(string from, string trigger, string to)
        {
            if (string.IsNullOrEmpty(trigger)) throw new ArgumentNullException(nameof(trigger));
            _transitions.Add(new Transition { From = from, To = to, Trigger = trigger });
        }

        /// <summary>
        /// 切换到指定状态：先自底向上退出旧激活分支，再自顶向下进入新分支。
        /// 本层切换会自动拆除/重建其子状态机分支。
        /// </summary>
        public bool ChangeState(string stateName)
        {
            if (!_states.TryGetValue(stateName, out FsmState next))
            {
                Debug.LogWarning($"[FSM] 状态机 {Name} 中不存在状态 {stateName}。");
                return false;
            }
            if (CurrentState == next) return true;

            PreviousState = CurrentState;
            if (CurrentState != null) CurrentState.ExitCascade();
            CurrentState = next;
            _entered = true;
            CurrentState.EnterCascade();
            return true;
        }

        /// <summary>触发器沿激活分支逐层下发（本层命中切换即停止下发）。</summary>
        public void Fire(string trigger)
        {
            if (string.IsNullOrEmpty(trigger)) return;
            FireCascade(trigger);
        }

        private void FireCascade(string trigger)
        {
            for (int i = 0; i < _transitions.Count; i++)
            {
                Transition t = _transitions[i];
                if (t.Trigger != trigger) continue;
                if (t.From != null && (CurrentState == null || CurrentState.Name != t.From)) continue;
                if (ChangeState(t.To)) return; // 切换后整条分支已重建，停止下发
            }
            // 本层未命中，继续向子机下发
            if (CurrentState != null && CurrentState.SubMachine != null)
                CurrentState.SubMachine.FireCascade(trigger);
        }

        /// <summary>本层当前是否处于指定状态。</summary>
        public bool IsIn(string stateName)
        {
            return CurrentState != null && CurrentState.Name == stateName;
        }

        // ---------------- 框架驱动（Manager / 父状态级联调用） ----------------

        /// <summary>进入初始状态（幂等，未设置初始状态则空转）。</summary>
        internal void EnterInitial()
        {
            if (_entered || _initialStateName == null) return;
            ChangeState(_initialStateName);
        }

        /// <summary>退出整条激活分支并复位（局结束/实体回收时可用）。</summary>
        internal void ExitCurrentCascade()
        {
            if (CurrentState != null) CurrentState.ExitCascade();
            PreviousState = CurrentState;
            CurrentState = null;
            _entered = false;
        }

        internal void UpdateCascade(float deltaTime)
        {
            CheckConditionTransitions();
            if (CurrentState != null) CurrentState.UpdateCascade(deltaTime);
        }

        internal void FixedUpdateCascade(float deltaTime)
        {
            if (CurrentState != null) CurrentState.FixedUpdateCascade(deltaTime);
        }

        internal void LateUpdateCascade(float deltaTime)
        {
            if (CurrentState != null) CurrentState.LateUpdateCascade(deltaTime);
        }

        /// <summary>条件转换只在 Update 前求值；每帧至多切换一次，防止连锁抖动。</summary>
        private void CheckConditionTransitions()
        {
            if (CurrentState == null) return;
            for (int i = 0; i < _transitions.Count; i++)
            {
                Transition t = _transitions[i];
                if (t.Condition == null) continue;
                if (t.From != null && CurrentState.Name != t.From) continue;
                if (t.Condition())
                {
                    ChangeState(t.To);
                    break;
                }
            }
        }
    }
}
