using System;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 分层有限状态机（HFSM）状态基类。
    /// 一个状态可通过 <see cref="CreateSubMachine"/> 持有子状态机形成层级：
    /// 进入父状态时自动进入子机初始状态，退出父状态时先自底向上退出整条激活分支。
    /// 生命周期分两层：
    /// - OnEnter/OnUpdate 等：子类实现的本层逻辑；
    /// - EnterCascade/UpdateCascade 等：框架调用的级联入口，子类不要直接调用。
    /// </summary>
    public abstract class FsmState
    {
        /// <summary>状态名（同层状态机内唯一）。</summary>
        public string Name { get; }

        /// <summary>所属状态机（框架内部赋值）。</summary>
        public StateMachine Machine { get; internal set; }

        /// <summary>父状态。根层状态为 null。</summary>
        public FsmState Parent => Machine != null ? Machine.ParentState : null;

        /// <summary>当前是否在本层处于激活态。</summary>
        public bool IsActive => Machine != null && Machine.CurrentState == this;

        /// <summary>子状态机（分层组合），未创建时为 null。</summary>
        public StateMachine SubMachine { get; private set; }

        protected FsmState(string name = null)
        {
            Name = name ?? GetType().Name;
        }

        /// <summary>为本状态创建子状态机（重复调用返回已有实例）。</summary>
        public StateMachine CreateSubMachine(string name = null)
        {
            if (SubMachine != null) return SubMachine;
            if (Machine == null)
                throw new InvalidOperationException($"[FSM] 状态 {Name} 尚未注册到状态机，不能创建子状态机。");
            SubMachine = new StateMachine(name ?? $"{Name}.Sub", Machine.Owner, this);
            return SubMachine;
        }

        // ---------------- 子类生命周期（本层逻辑） ----------------

        public virtual void OnEnter() { }
        public virtual void OnExit() { }
        public virtual void OnUpdate(float deltaTime) { }
        public virtual void OnFixedUpdate(float deltaTime) { }
        public virtual void OnLateUpdate(float deltaTime) { }

        // ---------------- 框架级联入口（子类勿调） ----------------

        /// <summary>进入：先本层 OnEnter，再进入子机初始状态。</summary>
        internal void EnterCascade()
        {
            OnEnter();
            if (SubMachine != null) SubMachine.EnterInitial();
        }

        /// <summary>退出：先退出整条子机激活分支，再本层 OnExit。</summary>
        internal void ExitCascade()
        {
            if (SubMachine != null) SubMachine.ExitCurrentCascade();
            OnExit();
        }

        internal void UpdateCascade(float deltaTime)
        {
            OnUpdate(deltaTime);
            if (SubMachine != null) SubMachine.UpdateCascade(deltaTime);
        }

        internal void FixedUpdateCascade(float deltaTime)
        {
            OnFixedUpdate(deltaTime);
            if (SubMachine != null) SubMachine.FixedUpdateCascade(deltaTime);
        }

        internal void LateUpdateCascade(float deltaTime)
        {
            OnLateUpdate(deltaTime);
            if (SubMachine != null) SubMachine.LateUpdateCascade(deltaTime);
        }

        public override string ToString() => Name;
    }
}
