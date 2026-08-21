using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 实体状态机组件：挂在需要状态机的实体（玩家/AI/武器等）上，持有根状态机。
    /// 本组件不自行 Update，由 StateMachineManager 局内统一驱动；
    /// OnEnable/OnDisable 自动向管理器注册/反注册。
    /// 用法：继承本组件并重写 <see cref="BuildStateMachine"/> 注册状态与转换规则，
    /// 或在外部直接操作 <see cref="Root"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public class StateMachineComponent : MonoBehaviour
    {
        /// <summary>根状态机（Awake 时创建并调用 BuildStateMachine）。</summary>
        public StateMachine Root { get; private set; }

        /// <summary>是否参与 Manager 统一驱动（可单独停表本实体而不注销）。</summary>
        public bool TickEnabled = true;

        protected virtual void Awake()
        {
            Root = new StateMachine($"{name}.Root", this);
            BuildStateMachine(Root);
        }

        /// <summary>子类在此注册状态/子状态机/转换规则，并调用 root.SetInitial。</summary>
        protected virtual void BuildStateMachine(StateMachine root) { }

        protected virtual void OnEnable()
        {
            if (GameManager.HasInstance)
                GameManager.Instance.Get<StateMachineManager>()?.Register(this);
        }

        protected virtual void OnDisable()
        {
            if (GameManager.HasInstance)
                GameManager.Instance.Get<StateMachineManager>()?.Unregister(this);
        }

        /// <summary>事件驱动切换（根层）。</summary>
        public bool ChangeState(string stateName) => Root.ChangeState(stateName);

        /// <summary>触发器沿激活分支逐层下发。</summary>
        public void Fire(string trigger) => Root.Fire(trigger);

        /// <summary>整条激活分支上是否存在指定状态名（自根机沿激活链逐层下钻）。</summary>
        public bool IsInBranch(string stateName)
        {
            StateMachine machine = Root;
            while (machine != null)
            {
                if (machine.CurrentState == null) return false;
                if (machine.CurrentState.Name == stateName) return true;
                machine = machine.CurrentState.SubMachine;
            }
            return false;
        }

        /// <summary>当前激活分支路径（调试用，如 "Root/Alive/Moving"）。</summary>
        public string GetBranchPath()
        {
            var sb = new System.Text.StringBuilder(name);
            StateMachine machine = Root;
            while (machine != null && machine.CurrentState != null)
            {
                sb.Append('/').Append(machine.CurrentState.Name);
                machine = machine.CurrentState.SubMachine;
            }
            return sb.ToString();
        }

        // ---------------- 由 StateMachineManager 统一驱动 ----------------

        internal void TickUpdate(float deltaTime)
        {
            if (!CanTick()) return;
            Root.EnterInitial(); // 幂等：首帧懒进入初始状态
            Root.UpdateCascade(deltaTime);
        }

        internal void TickFixedUpdate(float deltaTime)
        {
            if (!CanTick()) return;
            Root.FixedUpdateCascade(deltaTime);
        }

        internal void TickLateUpdate(float deltaTime)
        {
            if (!CanTick()) return;
            Root.LateUpdateCascade(deltaTime);
        }

        private bool CanTick() => TickEnabled && isActiveAndEnabled;
    }
}
