using FaceSlapper.Core;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 玩家状态基类：缓存 Movement 并提供 Owner 守卫。
    /// 刚体只在 Owner 端模拟（非权威端为运动学，由同步驱动），
    /// 因此各状态的 FixedUpdate / Enter / Exit 都必须先过 IsOwner。
    /// </summary>
    public abstract class PlayerStateBase : FsmState
    {
        private Movement _movement;

        protected Movement Move
        {
            get
            {
                if (_movement == null && Machine != null && Machine.Owner != null)
                    _movement = Machine.Owner.GetComponent<Movement>();
                return _movement;
            }
        }

        /// <summary>仅 Owner 端执行移动逻辑。</summary>
        protected bool IsOwner => Move != null && Move.IsOwner;

        protected PlayerStateBase(string name) : base(name) { }
    }

    /// <summary>
    /// 正常状态（复合态）：持有子状态机区分 Idle / Moving。
    /// 子状态只做表现层划分，移动逻辑一致（见 PlayerLocomotionState）。
    /// </summary>
    public class PlayerNormalState : PlayerStateBase
    {
        public const string StateName = "Normal";

        public PlayerNormalState() : base(StateName) { }
    }

    /// <summary>
    /// 正常移动子状态基类（Idle / Moving 共用）：
    /// 轻击退硬直中走弱空控，否则全速移动 + 跳跃。
    /// </summary>
    public abstract class PlayerLocomotionState : PlayerStateBase
    {
        protected PlayerLocomotionState(string name) : base(name) { }

        public override void OnFixedUpdate(float deltaTime)
        {
            if (!IsOwner) return;
            if (Move.KnockbackActive) Move.TickAirControl(deltaTime);
            else Move.TickLocomotion(deltaTime);
        }
    }

    /// <summary>待机子状态：无移动输入。</summary>
    public class PlayerIdleState : PlayerLocomotionState
    {
        public const string StateName = "Idle";

        public PlayerIdleState() : base(StateName) { }
    }

    /// <summary>移动子状态：有移动输入。</summary>
    public class PlayerMovingState : PlayerLocomotionState
    {
        public const string StateName = "Moving";

        public PlayerMovingState() : base(StateName) { }
    }

    /// <summary>
    /// 重击飞行状态（拳套 ApplyLaunch 触发进入）：
    /// 仅保留弱空控；退出时（落地或撞墙眩晕）由 Movement.EndLaunch 统一收尾
    /// （清除飞行标记，正常落地播放史莱姆压扁脉冲）。
    /// </summary>
    public class PlayerLaunchedState : PlayerStateBase
    {
        public const string StateName = "Launched";

        public PlayerLaunchedState() : base(StateName) { }

        public override void OnFixedUpdate(float deltaTime)
        {
            if (!IsOwner) return;
            Move.TickAirControl(deltaTime);
        }

        public override void OnExit()
        {
            if (!IsOwner) return;
            Move.EndLaunch();
        }
    }

    /// <summary>
    /// 冲刺状态（拳套蓄力冲拳触发进入）：
    /// 持续期间保持平坦前冲速度，计时耗尽或命中急停（EndDash）后回到正常状态。
    /// </summary>
    public class PlayerDashState : PlayerStateBase
    {
        public const string StateName = "Dash";

        public PlayerDashState() : base(StateName) { }

        public override void OnFixedUpdate(float deltaTime)
        {
            if (!IsOwner) return;
            Move.TickDash(deltaTime);
        }
    }

    /// <summary>
    /// 眩晕状态（重击飞行撞障碍触发进入，StunBuff 到期触发退出）：
    /// 进入瞬间水平急停，持续期间禁止移动/跳跃输入。
    /// </summary>
    public class PlayerStunnedState : PlayerStateBase
    {
        public const string StateName = "Stunned";

        public PlayerStunnedState() : base(StateName) { }

        public override void OnEnter()
        {
            if (!IsOwner) return;
            Move.StopHorizontal();
        }

        public override void OnFixedUpdate(float deltaTime)
        {
            if (!IsOwner) return;
            Move.TickStunned(deltaTime);
        }
    }
}
