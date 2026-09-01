using FaceSlapper.Battle;
using FaceSlapper.Camera;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 拳套武器：末日铁拳式蓄力冲拳。
    /// 右键按住蓄力（Owner 本地进度 0-1，拳套后拉表现经武器变换同步全端可见），
    /// 松开后向前冲拳并带着持有者一起冲刺（玩家 Dash 状态）；
    /// 冲刺期间持续命中检测，命中急停冲刺。
    /// 命中效果由组件组合承担（LaunchKnockbackEffect 大力击飞并随蓄力缩放、
    /// HitShakeEffect 相机抖动、StopHolderDashEffect 命中急停），
    /// 敌人击飞途中撞上障碍物会陷入眩晕（见 Movement.OnCollisionEnter）。
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

        /// <summary>
        /// 冲拳广播（服务器写）：高 24 位为序号，低 8 位为蓄力百分比（0-255）。
        /// 单字段打包，避免"蓄力值"与"攻击序号"两个 NetVar 之间的同步时序问题。
        /// </summary>
        private readonly NetVar<int> _punchBroadcast = new NetVar<int>(0);

        /// <summary>蓄力特效等级（服务器写，0-16 量化）：驱动全端 ChargeFxComponent。</summary>
        private readonly NetVar<int> _chargeFxLevel = new NetVar<int>(0);

        [Header("蓄力特效")]
        [Tooltip("ChargeFx prefab（空间扭曲 + 内缩粒子），生成时实例化为武器子物体")]
        [SerializeField] private ChargeFxComponent _chargeFxPrefab;

        private bool _charging;     // Owner 端蓄力中
        private float _charge;      // Owner 端本地蓄力进度 0-1
        private float _lastCharge;  // 最近一次出拳的蓄力等级（全端可读，动画/命中缩放）
        private ChargeFxComponent _fx;
        private int _lastFxLevel = -1;  // Owner 端已上报的特效等级（变化时才发 RPC）

        protected override void Awake()
        {
            base.Awake();
            _punchBroadcast.OnChange += OnPunchBroadcast;
            if (_chargeFxPrefab != null)
            {
                _fx = Instantiate(_chargeFxPrefab, transform);
                _chargeFxLevel.OnChange += (prev, next) => _fx.SetCharge(next / 16f);
            }
            else
            {
                Debug.LogWarning("[BoxingGlove] 未配置蓄力特效 prefab（_chargeFxPrefab）。");
            }
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
            // 蓄力期间相机轻微持续抖动（随蓄力进度增强）。
            if (PlayerCameraRig.Instance != null) PlayerCameraRig.Instance.SetChargeShake(_charge);
            // 蓄力特效等级量化上报（16 级，变化才发 RPC），服务器经 NetVar 广播全端。
            int fxLevel = Mathf.RoundToInt(_charge * 16f);
            if (fxLevel != _lastFxLevel)
            {
                _lastFxLevel = fxLevel;
                SendServerRpc(nameof(CmdSetChargeFx), fxLevel);
            }
        }

        private void CancelCharge()
        {
            _charging = false;
            _charge = 0f;
            if (Anim != null) Anim.ChargeAmount = 0f;
            if (PlayerCameraRig.Instance != null) PlayerCameraRig.Instance.SetChargeShake(0f);
            if (_lastFxLevel != 0)
            {
                _lastFxLevel = 0;
                SendServerRpc(nameof(CmdSetChargeFx), 0);
            }
        }

        [NetRpc]
        private void CmdSetChargeFx(int level)
        {
            // 仅服务器执行：写 NetVar 广播全端驱动蓄力特效。
            _chargeFxLevel.Value = Mathf.Clamp(level, 0, 16);
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

        /// <summary>本次攻击的效果强度：最近一次出拳的蓄力等级（供效果随蓄力缩放）。</summary>
        protected override float CurrentPower => _lastCharge;

        /// <summary>按蓄力等级缩放播冲拳动画（全端）。</summary>
        protected override void PlayAttackAnim()
        {
            if (Anim != null) Anim.Play(Mathf.Lerp(0.6f, 1.5f, _lastCharge));
        }

        /// <summary>冲拳时把持有者送入冲刺（仅 Owner 端，速度随蓄力缩放）。</summary>
        protected override void OnAttackStateEnterOwner()
        {
            NetObject holder = FindHolder();
            if (holder == null) return;

            Movement move = holder.GetComponent<Movement>();
            if (move != null)
                move.ApplyDash(holder.transform.forward,
                    Mathf.Lerp(_minDashSpeed, _maxDashSpeed, _lastCharge), _dashDuration);
        }

        /// <summary>冲刺期间持续命中检测（攻击状态每物理帧调用，仅 Owner 端）。</summary>
        internal override void HandleAttackStateFixedUpdate()
        {
            if (IsOwner) DoHitCheck();
        }
    }
}
