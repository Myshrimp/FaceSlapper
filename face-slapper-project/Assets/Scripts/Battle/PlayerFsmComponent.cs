using FaceSlapper.Core;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 玩家状态机组件：Normal（复合态，子机 Idle/Moving）/ Launched（重击飞行）/ Stunned（眩晕）。
    /// 移动、击飞、眩晕的节奏全部由状态机驱动，Movement 只提供状态原语与条件查询；
    /// 由 StateMachineManager 局内统一驱动。
    /// </summary>
    [RequireComponent(typeof(Movement))]
    public class PlayerFsmComponent : StateMachineComponent
    {
        /// <summary>重击击飞触发器（Movement.ApplyLaunch 发出）。</summary>
        public const string LaunchTrigger = "Launch";
        /// <summary>眩晕触发器（重击飞行中撞到障碍物时发出）。</summary>
        public const string StunTrigger = "Stun";
        /// <summary>眩晕结束触发器（StunBuff 到期时发出）。</summary>
        public const string StunEndTrigger = "StunEnd";
        /// <summary>冲刺触发器（拳套蓄力冲拳，Movement.ApplyDash 发出）。</summary>
        public const string DashTrigger = "Dash";

        protected override void BuildStateMachine(StateMachine root)
        {
            Movement movement = GetComponent<Movement>();

            var normal = root.AddState(new PlayerNormalState());
            root.AddState(new PlayerLaunchedState());
            root.AddState(new PlayerStunnedState());
            root.AddState(new PlayerDashState());
            root.SetInitial(PlayerNormalState.StateName);

            // Normal 子机：Idle <-> Moving，按是否有移动输入切换（表现层划分）。
            // 注意：必须先 AddState(normal) 再 CreateSubMachine（子机要求父状态已注册）。
            StateMachine sub = normal.CreateSubMachine();
            sub.AddState(new PlayerIdleState());
            sub.AddState(new PlayerMovingState());
            sub.SetInitial(PlayerIdleState.StateName);
            sub.AddTransition(PlayerIdleState.StateName, PlayerMovingState.StateName,
                () => movement.IsOwner && movement.HasMoveInput);
            sub.AddTransition(PlayerMovingState.StateName, PlayerIdleState.StateName,
                () => !movement.IsOwner || !movement.HasMoveInput);

            // 任意态 -> Launched：重击击飞（拳套）。
            root.AddTriggerTransition(null, LaunchTrigger, PlayerLaunchedState.StateName);
            // Launched -> Normal / Stunned：失控时间耗尽且已落地（条件转换）。
            root.AddTransition(PlayerLaunchedState.StateName, PlayerNormalState.StateName,
                () => movement.IsOwner && movement.LaunchEnded && !movement.IsStunned);
            root.AddTransition(PlayerLaunchedState.StateName, PlayerStunnedState.StateName,
                () => movement.IsOwner && movement.LaunchEnded && movement.IsStunned);
            // 任意态 -> Stunned：眩晕（目前为重击飞行撞障碍）。
            root.AddTriggerTransition(null, StunTrigger, PlayerStunnedState.StateName);
            // Stunned -> Normal：StunBuff 到期。
            root.AddTriggerTransition(PlayerStunnedState.StateName, StunEndTrigger, PlayerNormalState.StateName);
            // 任意态 -> Dash：拳套蓄力冲拳冲刺。
            root.AddTriggerTransition(null, DashTrigger, PlayerDashState.StateName);
            // Dash -> Normal：冲刺计时耗尽或命中急停（条件转换）。
            root.AddTransition(PlayerDashState.StateName, PlayerNormalState.StateName,
                () => movement.IsOwner && movement.DashEnded);
        }
    }
}
