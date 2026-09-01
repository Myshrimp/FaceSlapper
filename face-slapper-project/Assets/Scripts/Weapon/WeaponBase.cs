using System.Collections.Generic;
using FaceSlapper.Battle;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 武器基类（服务器权威归属 + 持有者"虚拟挂载" + 状态机驱动攻击）：
    /// - 闲置时在地面由服务器物理模拟（NetTransformSync 广播）；
    /// - 被拾取后服务器写 NetVar HolderNobId 并转移所有权给持有者连接，
    ///   持有者端每帧把武器对齐到手部挂点，其他端靠 NetTransformSync 插值；
    /// - 放下时清除归属、所有权交还服务器、恢复物理。
    /// 攻击链路（状态机驱动，动画广播全端）：
    ///   Owner 输入 OnAttack → 服务器冷却校验 → 攻击序号 NetVar 自增（广播全端）
    ///   → 各端 WeaponFsmComponent 切入 Attack 状态 → WeaponAnimComponent 播动画，
    ///   Owner 端在状态进入时做命中检测（DoHitCheck）。
    /// 命中与效果（组件组合）：
    ///   HitDetector（球/方/射线检测器）负责范围查询与过滤，
    ///   WeaponEffectManager 收集本武器所有 WeaponEffect 逐目标应用并独立做网络同步，
    ///   武器子类只保留自身特有行为（如拳套蓄力），新武器 = 检测器 + Effect 组合。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(WeaponFsmComponent))]
    public abstract class WeaponBase : NetBehaviour, IWeapon
    {
        /// <summary>攻击触发器（武器状态机用）。</summary>
        public const string AttackTrigger = "Attack";

        [Header("攻击")]
        [SerializeField] protected float _attackInterval = 0.6f;
        [Tooltip("攻击状态时长（等于攻击动画时长）")]
        [SerializeField] protected float _attackDuration = 0.25f;

        [Tooltip("攻击判定点（武器尖端）。不设置则用持有者面前位置。")]
        [SerializeField] protected Transform _tip;

        [Header("命中")]
        [Tooltip("命中检测器（球/方/射线等可复用组件）。不设置则取本物体上的 HitDetector。")]
        [SerializeField] protected HitDetector _detector;

        /// <summary>攻击状态时长（攻击状态据此自动回到待机）。</summary>
        public float AttackDuration => _attackDuration;

        private readonly NetVar<int> _holderNobId = new NetVar<int>(-1);
        /// <summary>攻击序号（服务器写，广播全端驱动状态机切入攻击）。</summary>
        private readonly NetVar<int> _attackSeq = new NetVar<int>(0);

        /// <summary>持有者的网络对象 Id，-1 表示闲置。</summary>
        public int HolderNobId => _holderNobId.Value;

        public bool IsHeld => HolderNobId >= 0;

        protected Rigidbody _rb;
        private WeaponFsmComponent _fsm;
        private WeaponAnimComponent _anim;
        private WeaponEffectManager _effectManager;
        private readonly HitResult[] _hitBuffer = new HitResult[32];
        private readonly HashSet<int> _hitVictims = new HashSet<int>(8);  // 单次攻击已命中的目标
        private float _lastAttackTime = float.NegativeInfinity;
        private float _serverLastAttackTime = float.NegativeInfinity;

        /// <summary>动画组件（子类做蓄力表现/强度缩放用）。</summary>
        protected WeaponAnimComponent Anim => _anim;

        /// <summary>本次攻击的效果强度 0-1（蓄力武器重写返回蓄力等级，非蓄力武器恒为 0）。</summary>
        protected virtual float CurrentPower => 0f;

        /// <summary>本地冷却是否就绪（Owner 端输入预判）。</summary>
        protected bool LocalAttackReady => Time.time - _lastAttackTime >= _attackInterval;

        /// <summary>标记本地攻击时刻（发起攻击/冲拳时调用）。</summary>
        protected void MarkLocalAttack() => _lastAttackTime = Time.time;

        /// <summary>服务器冷却校验（防作弊连发），通过后才能广播攻击。</summary>
        protected bool ServerValidateAttack()
        {
            if (Time.time - _serverLastAttackTime < _attackInterval) return false;
            _serverLastAttackTime = Time.time;
            return true;
        }

        /// <summary>服务器递增攻击序号（NetVar 广播全端驱动状态机）。</summary>
        protected void ServerBroadcastAttack() => _attackSeq.Value++;

        /// <summary>驱动武器状态机切入攻击状态（全端广播到达时调用）。</summary>
        protected void FireAttackTrigger()
        {
            if (_fsm != null) _fsm.Fire(AttackTrigger);
        }

        protected override void Awake()
        {
            base.Awake();
            _rb = GetComponent<Rigidbody>();
            _fsm = GetComponent<WeaponFsmComponent>();
            _anim = GetComponent<WeaponAnimComponent>();
            _effectManager = GetComponent<WeaponEffectManager>();
            if (_detector == null) _detector = GetComponent<HitDetector>();
            if (_effectManager == null)
                Debug.LogWarning($"[Weapon] {name} 缺少 WeaponEffectManager 组件，命中将无效果。");
            if (_detector == null)
                Debug.LogWarning($"[Weapon] {name} 缺少 HitDetector 组件，将无法做命中检测。");
            _holderNobId.OnChange += (prev, next) => ApplyHolderState();
            _attackSeq.OnChange += (prev, next) => { if (next > prev) OnAttackBroadcast(); };
        }

        public override void OnNetSpawnServer() => ApplyHolderState();

        public override void OnNetSpawnClient() => ApplyHolderState();

        public override void OnNetOwnershipChanged(bool isOwner) => ApplyHolderState();

        /// <summary>根据归属状态更新本地表现：运动学开关 + 回写本地玩家的 HeldWeapon。</summary>
        private void ApplyHolderState()
        {
            // 持有时全端运动学（由持有者手部驱动）；闲置时非控制端运动学（跟随同步）。
            if (_rb != null)
                _rb.isKinematic = IsHeld || !NetObj.IsController;

            // 回写本地玩家（仅客户端关心）。
            if (!Net.IsClient) return;
            NetObject localPlayer = Net.Client.LocalPlayer;
            if (localPlayer == null) return;

            var pick = localPlayer.GetComponent<PickWeaponAbility>();
            if (pick == null) return;

            if (IsHeld && localPlayer.NetId == HolderNobId)
            {
                if (pick.HeldWeapon != this)
                {
                    pick.SetHeld(this);
                    OnActivate();
                }
            }
            else if (pick.HeldWeapon == this)
            {
                pick.SetHeld(null);
                OnDeactivate();
            }
        }

        protected virtual void Update()
        {
            if (!IsHeld || !NetObj.IsOwner) return;

            NetObject holder = FindHolder();
            if (holder == null) return;

            Transform socket = FindHandSocket(holder);
            transform.SetPositionAndRotation(socket.position, socket.rotation);

            // 攻击动画叠加在跟随之后（Owner 端表现，经 NetTransformSync 同步给其他端）。
            if (_anim != null) _anim.Apply(transform);
        }

        /// <summary>查找持有者对象（服务器/客户端各自的已生成对象表）。</summary>
        protected NetObject FindHolder()
        {
            if (HolderNobId < 0) return null;
            if (Net.IsServer && Net.Server.TryGetObject(HolderNobId, out NetObject n))
                return n;
            if (Net.IsClient && Net.Client.TryGetObject(HolderNobId, out NetObject n2))
                return n2;
            return null;
        }

        private static Transform FindHandSocket(NetObject holder)
        {
            Transform[] all = holder.GetComponentsInChildren<Transform>();
            foreach (Transform t in all)
            {
                if (t.name == "HandSocket") return t;
            }
            return holder.transform;
        }

        /// <summary>请求拾取（客户端发起，服务器校验距离并转移所有权）。</summary>
        public void RequestPickup(int playerNetId) => SendServerRpc(nameof(CmdPickup), playerNetId);

        /// <summary>请求放下（客户端发起，服务器清除归属并恢复物理）。</summary>
        public void RequestDrop() => SendServerRpc(nameof(CmdDrop));

        [NetRpc]
        private void CmdPickup(int playerNetId)
        {
            if (IsHeld) return;
            if (!Net.Server.TryGetObject(playerNetId, out NetObject player)) return;
            if (player.OwnerClientId < 0) return;

            if (Vector3.Distance(player.transform.position, transform.position) > 4f) return;

            _holderNobId.Value = playerNetId;
            Net.Server.GiveOwnership(NetObj, player.OwnerClientId);
        }

        [NetRpc]
        private void CmdDrop()
        {
            if (!IsHeld) return;

            Vector3 dropPosition = transform.position;
            Vector3 dropVelocity = Vector3.zero;
            NetObject holder = FindHolder();
            if (holder != null)
            {
                dropPosition = holder.transform.position + holder.transform.forward * 1f + Vector3.up * 0.5f;
                dropVelocity = holder.transform.forward * 2f;
            }

            _holderNobId.Value = -1;
            Net.Server.RemoveOwnership(NetObj);

            transform.position = dropPosition;
            if (_rb != null)
            {
                _rb.velocity = dropVelocity;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        // ---------------- 攻击（状态机驱动，广播全端） ----------------

        /// <summary>攻击入口（Owner 输入）：本地冷却检查 → 请求服务器广播。</summary>
        public virtual void OnAttack()
        {
            if (!IsHeld || !IsOwner) return;
            if (!LocalAttackReady) return;

            MarkLocalAttack();
            SendServerRpc(nameof(CmdAttack));
        }

        /// <summary>蓄力开始（Owner 输入，右键按下）。子类按需重写，默认无行为。</summary>
        public virtual void OnChargeStart() { }

        /// <summary>蓄力释放（Owner 输入，右键松开）。子类按需重写，默认无行为。</summary>
        public virtual void OnChargeRelease() { }

        [NetRpc]
        private void CmdAttack()
        {
            // 仅服务器执行：冷却校验（防作弊连发）后递增攻击序号，NetVar 广播全端。
            if (!ServerValidateAttack()) return;
            ServerBroadcastAttack();
        }

        /// <summary>攻击广播到达（全端）：驱动状态机切入攻击状态。</summary>
        private void OnAttackBroadcast()
        {
            FireAttackTrigger();
        }

        /// <summary>
        /// 攻击状态进入（全端，由 WeaponAttackState.OnEnter 调用）：
        /// 清命中记录、播攻击动画；Owner 端额外执行攻击钩子并做命中检测。
        /// </summary>
        internal virtual void HandleAttackStateEnter()
        {
            _hitVictims.Clear();
            PlayAttackAnim();
            if (!IsOwner) return;
            OnAttackStateEnterOwner();
            DoHitCheck();
        }

        /// <summary>播攻击动画（全端）。子类可重写做蓄力强度缩放。</summary>
        protected virtual void PlayAttackAnim()
        {
            if (_anim != null) _anim.Play();
        }

        /// <summary>攻击状态进入时的 Owner 端钩子（如拳套把持有者送入冲刺），默认无行为。</summary>
        protected virtual void OnAttackStateEnterOwner() { }

        /// <summary>
        /// 攻击状态期间每物理帧调用（WeaponAttackState.OnFixedUpdate）：
        /// 子类可重写做持续命中检测（如拳套冲刺中的扫击），默认无行为。
        /// </summary>
        internal virtual void HandleAttackStateFixedUpdate() { }

        /// <summary>
        /// 命中检测（仅 Owner 端）：检测器查询 → 单次攻击去重 → EffectManager 应用全部效果。
        /// 武器不再感知具体效果，效果组合由 WeaponEffect 组件决定。
        /// </summary>
        protected virtual void DoHitCheck()
        {
            if (_detector == null || _effectManager == null) return;

            NetObject holder = FindHolder();
            if (holder == null) return;

            NetworkIdentity attacker = holder.GetComponent<NetworkIdentity>();
            if (attacker == null) return;

            var ctx = new HitDetectContext { Holder = holder, Origin = _tip };
            int count = _detector.Detect(ctx, _hitBuffer);

            // 单次攻击每个目标只命中一次（拳套冲刺持续检测依赖该去重）。
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                if (_hitVictims.Add(_hitBuffer[i].Target.NetId))
                    _hitBuffer[kept++] = _hitBuffer[i];
            }
            if (kept == 0) return;

            _effectManager.ApplyHits(holder, attacker, _hitBuffer, kept, CurrentPower);
        }

        public virtual void OnActivate() { }

        public virtual void OnDeactivate() { }

        public virtual void OnHitPlayer(NetworkIdentity victim) { }
    }
}
