using System.Collections.Generic;
using FaceSlapper.Battle;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 拳套武器：末日铁拳式蓄力冲拳。
    /// 右键按住蓄力（Owner 本地进度 0-1，拳套后拉表现经武器变换同步全端可见），
    /// 松开后向前冲拳并带着持有者一起冲刺（玩家 Dash 状态）；
    /// 冲刺期间持续命中检测，命中敌人大力击飞（力度随蓄力缩放）并急停冲刺，
    /// 敌人击飞途中撞上障碍物（非玩家、非地面）会陷入眩晕（见 Movement.OnCollisionEnter）。
    /// 蓄力中持有者被眩晕或武器脱手会取消蓄力。击飞经服务器校验后执行。
    /// </summary>
    public class BoxingGloveWeapon : WeaponBase
    {
        [Header("蓄力")]
        [Tooltip("蓄力从 0 到满所需秒数")]
        [SerializeField] private float _chargeTime = 0.9f;

        [Header("冲刺")]
        [Tooltip("冲刺速度（蓄力 0 / 满 之间插值）")]
        [SerializeField] private float _minDashSpeed = 8f;
        [SerializeField] private float _maxDashSpeed = 20f;
        [Tooltip("冲刺持续时长（秒），也是持续命中检测窗口")]
        [SerializeField] private float _dashDuration = 0.28f;

        [Header("命中")]
        [Tooltip("击飞力度（蓄力 0 / 满 之间插值）")]
        [SerializeField] private float _minForce = 10f;
        [SerializeField] private float _maxForce = 22f;
        [Tooltip("竖直分量比例（越大抛得越高）")]
        [SerializeField] private float _upRatio = 0.8f;
        [Tooltip("失控/滞空时间（撞障碍判定窗口，秒）")]
        [SerializeField] private float _airTime = 0.8f;
        [Tooltip("撞障碍后的眩晕时长（秒）")]
        [SerializeField] private float _stunDuration = 1.6f;
        [SerializeField] private float _hitRadius = 1.2f;

        /// <summary>
        /// 冲拳广播（服务器写）：高 24 位为序号，低 8 位为蓄力百分比（0-255）。
        /// 单字段打包，避免"蓄力值"与"攻击序号"两个 NetVar 之间的同步时序问题。
        /// </summary>
        private readonly NetVar<int> _punchBroadcast = new NetVar<int>(0);

        private bool _charging;     // Owner 端蓄力中
        private float _charge;      // Owner 端本地蓄力进度 0-1
        private float _lastCharge;  // 最近一次出拳的蓄力等级（全端可读，动画/命中缩放）
        private readonly HashSet<int> _hitVictims = new HashSet<int>(8);  // 单次冲拳已命中的目标

        protected override void Awake()
        {
            base.Awake();
            _punchBroadcast.OnChange += OnPunchBroadcast;
        }

        /// <summary>拳套不响应左键普攻，攻击只有蓄力冲拳一种。</summary>
        public override void OnAttack() { }

        /// <summary>右键按下：开始蓄力（本地冷却预判）。</summary>
        public override void OnChargeStart()
        {
            if (!IsHeld || !IsOwner) return;
            if (!LocalAttackReady) return;
            _charging = true;
            _charge = 0f;
        }

        /// <summary>右键松开：按当前蓄力等级请求服务器广播冲拳。</summary>
        public override void OnChargeRelease()
        {
            if (!_charging) return;
            float charge = _charge;
            CancelCharge();
            MarkLocalAttack();
            SendServerRpc(nameof(CmdRocketPunch), charge);
        }

        protected override void Update()
        {
            base.Update();
            if (!_charging) return;

            // 武器脱手/失去所有权时取消蓄力。
            if (!IsHeld || !IsOwner)
            {
                CancelCharge();
                return;
            }

            // 持有者被眩晕时取消蓄力（眩晕中输入层被封锁，松开事件到不了这里）。
            NetObject holder = FindHolder();
            Movement move = holder != null ? holder.GetComponent<Movement>() : null;
            if (move == null || move.IsStunned)
            {
                CancelCharge();
                return;
            }

            _charge = Mathf.Clamp01(_charge + Time.deltaTime / _chargeTime);
            // 蓄力后拉表现（Owner 每帧驱动武器变换，经 NetTransformSync 全端可见）。
            if (Anim != null) Anim.ChargeAmount = _charge;
        }

        private void CancelCharge()
        {
            _charging = false;
            _charge = 0f;
            if (Anim != null) Anim.ChargeAmount = 0f;
        }

        [NetRpc]
        private void CmdRocketPunch(float charge)
        {
            // 仅服务器执行：冷却校验（防作弊连发）后打包 序号+蓄力百分比 广播。
            if (!ServerValidateAttack()) return;
            int seq = (_punchBroadcast.Value >> 8) + 1;
            int pct = Mathf.Clamp(Mathf.RoundToInt(charge * 255f), 0, 255);
            _punchBroadcast.Value = (seq << 8) | pct;
        }

        /// <summary>冲拳广播到达（全端）：记录蓄力等级并驱动状态机切入攻击状态。</summary>
        private void OnPunchBroadcast(int prev, int next)
        {
            if ((next >> 8) <= (prev >> 8)) return;
            _lastCharge = (next & 0xFF) / 255f;
            FireAttackTrigger();
        }

        /// <summary>
        /// 攻击状态进入（全端）：清命中记录、按蓄力缩放播冲拳动画；
        /// Owner 端额外把持有者送入冲刺并立即做一次命中检测。
        /// </summary>
        internal override void HandleAttackStateEnter()
        {
            _hitVictims.Clear();
            if (Anim != null) Anim.Play(Mathf.Lerp(0.6f, 1.5f, _lastCharge));
            if (!IsOwner) return;

            NetObject holder = FindHolder();
            if (holder == null) return;

            Movement move = holder.GetComponent<Movement>();
            if (move != null)
                move.ApplyDash(holder.transform.forward,
                    Mathf.Lerp(_minDashSpeed, _maxDashSpeed, _lastCharge), _dashDuration);

            DoHitCheck();
        }

        /// <summary>冲刺期间持续命中检测（攻击状态每物理帧调用，仅 Owner 端）。</summary>
        internal override void HandleAttackStateFixedUpdate()
        {
            if (IsOwner) DoHitCheck();
        }

        protected override void DoHitCheck()
        {
            NetObject holder = FindHolder();
            if (holder == null) return;

            NetworkIdentity attacker = holder.GetComponent<NetworkIdentity>();
            if (attacker == null) return;

            Vector3 center = _tip != null
                ? _tip.position
                : holder.transform.position + Vector3.up + holder.transform.forward * 1.2f;

            float force = Mathf.Lerp(_minForce, _maxForce, _lastCharge);

            Collider[] hits = Physics.OverlapSphere(center, _hitRadius);
            foreach (Collider hit in hits)
            {
                NetObject nob = hit.GetComponentInParent<NetObject>();
                if (nob == null || nob == holder) continue;
                // 单次冲拳每个目标只命中一次。
                if (!_hitVictims.Add(nob.NetId)) continue;

                NetworkIdentity victim = nob.GetComponent<NetworkIdentity>();
                if (victim == null) continue;

                Vector3 dir = nob.transform.position - holder.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = holder.transform.forward;
                dir.Normalize();

                attacker.ReportLaunch(nob.NetId, dir, force, _upRatio, _airTime, _hitRadius + 2f, _stunDuration);
                OnHitPlayer(victim);

                // 命中急停：冲刺在撞到敌人时停下（末日铁拳手感）。
                Movement move = holder.GetComponent<Movement>();
                if (move != null) move.EndDash();
            }
        }
    }
}
