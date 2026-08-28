using FaceSlapper.Core;
using FaceSlapper.Input;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 人物移动/旋转脚本（客户端 Owner 权威）：
    /// Owner 端读取输入模拟刚体运动，由 NetTransformSync 广播到其他端。
    /// 非权威端刚体设为运动学，完全由同步驱动。
    /// 移动/击飞/眩晕的节奏由 PlayerFsmComponent 状态机驱动，
    /// 本组件只提供状态原语（TickLocomotion/TickAirControl/TickStunned 等）与条件查询。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetObject))]
    public class Movement : MonoBehaviour
    {
        /// <summary>本机玩家的 Movement（相机、输入系统通过它绑定）。</summary>
        public static Movement LocalInstance { get; private set; }

        [Header("移动")]
        [SerializeField] private float _moveSpeed = 6f;
        [SerializeField] private float _turnSpeed = 720f;

        [Header("击飞")]
        [Tooltip("失控时间下限（秒），实际失控时间取击飞效果 AirTime 与该值的较大者")]
        [SerializeField] private float _knockbackRecoverTime = 0.35f;

        [Tooltip("接触点平均法线 Y 大于该值视为地面/可站立面，不触发眩晕")]
        [SerializeField] private float _groundNormalY = 0.7f;

        [Header("跳跃")]
        [Tooltip("起跳瞬间的竖直速度（米/秒）")]
        [SerializeField] private float _jumpSpeed = 7f;
        [Tooltip("地面检测射线长度（从脚底向下）")]
        [SerializeField] private float _groundCheckDistance = 0.15f;

        private Rigidbody _rb;
        private NetObject _netObject;
        private BuffComponent _buffs;
        private PlayerFsmComponent _fsm;
        private SlimeSquashDriver _squashDriver;   // 史莱姆挤压拉伸（无该组件时跳过）
        private float _speedMultiplier = 1f;
        private float _knockbackTimer;
        private bool _isLaunched;   // 重击飞行中（撞障碍可触发眩晕）
        private Vector2 _moveAxis;  // Update 中按帧缓存的移动输入，状态机 Tick 时消费。
        private bool _jumpQueued;   // Update 中缓存跳跃输入，状态机 Tick 时消费，避免低物理频率下丢输入。
        private float _stunDuration = 1.6f;

        // 击飞位移预测（仅非 Owner 端）：攻击者本地命中立即表现敌人位移，
        // 不预测任何状态（眩晕/状态机仍等服务器链路）。
        private NetTransformSync _transformSync;
        private bool _predictingLaunch;
        private Vector3 _predictedVelocity;
        private float _predictedTimer;

        // 冲刺（拳套蓄力冲拳带动持有者前冲）。
        private float _dashTimer;
        private Vector3 _dashDir;
        private float _dashSpeed;

        public float SpeedMultiplier => _speedMultiplier;

        // ---------------- 状态机查询接口 ----------------

        /// <summary>是否本机权威端（刚体只在 Owner 端模拟）。</summary>
        public bool IsOwner => _netObject != null && _netObject.IsSpawned && _netObject.IsOwner;

        /// <summary>是否有移动输入（Normal 子机 Idle/Moving 切换条件）。</summary>
        public bool HasMoveInput => _moveAxis.sqrMagnitude > 0.001f;

        /// <summary>是否处于击退/击飞硬直（失控计时未耗尽）。</summary>
        public bool KnockbackActive => _knockbackTimer > 0f;

        /// <summary>是否眩晕中（权威来源是 StunBuff，重复获得刷新时长）。</summary>
        public bool IsStunned => _buffs != null && _buffs.HasBuff<StunBuff>();

        /// <summary>重击飞行是否结束：失控时间耗尽且已落地（Launched 状态退出条件）。</summary>
        public bool LaunchEnded => _isLaunched && _knockbackTimer <= 0f && IsGrounded();

        /// <summary>冲刺是否结束（Dash 状态退出条件）。</summary>
        public bool DashEnded => _dashTimer <= 0f;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _squashDriver = GetComponent<SlimeSquashDriver>();
            _buffs = GetComponent<BuffComponent>();
            _fsm = GetComponent<PlayerFsmComponent>();
            _transformSync = GetComponent<NetTransformSync>();
            _netObject = GetComponent<NetObject>();
            _netObject.OnSpawnServer += RefreshKinematic;
            _netObject.OnSpawnClient += RefreshKinematic;
            _netObject.OnOwnershipChanged += OnOwnershipChanged;
            _netObject.OnDespawnClient += OnDespawnClient;
        }

        private void OnDestroy()
        {
            if (_netObject != null)
            {
                _netObject.OnSpawnServer -= RefreshKinematic;
                _netObject.OnSpawnClient -= RefreshKinematic;
                _netObject.OnOwnershipChanged -= OnOwnershipChanged;
                _netObject.OnDespawnClient -= OnDespawnClient;
            }
            if (LocalInstance == this) LocalInstance = null;
        }

        /// <summary>非权威端不模拟物理，只跟随同步数据。</summary>
        private void RefreshKinematic()
        {
            _rb.isKinematic = !_netObject.IsController;
        }

        private void OnOwnershipChanged(bool isOwner)
        {
            StopPrediction();
            RefreshKinematic();
            if (isOwner)
            {
                LocalInstance = this;
                EventBus.Publish(new LocalPlayerSpawnedEvent { Player = _netObject });
            }
            else if (LocalInstance == this)
            {
                LocalInstance = null;
            }
        }

        private void OnDespawnClient()
        {
            StopPrediction();
            if (LocalInstance == this)
            {
                LocalInstance = null;
                EventBus.Publish(new LocalPlayerDespawnedEvent());
            }
        }

        private void Update()
        {
            if (!_netObject.IsSpawned || !_netObject.IsOwner) return;
            if (!GameManager.HasInstance) return;

            // 移动轴与跳跃都在 Update 按帧缓存：FixedUpdate 频率低，状态机 Tick 时直接读
            // 会采样不齐；GetKeyDown 更会在高帧率下被下一帧的空快照覆盖而丢失。
            InputComponent input = GameManager.Instance.Get<InputComponent>();
            if (input == null) return;

            _moveAxis = input.Current.MoveAxis;
            if (input.Current.JumpPressed)
                _jumpQueued = true;
        }

        // ---------------- 状态原语（由玩家状态机各状态调用，仅 Owner 端） ----------------

        /// <summary>正常移动 Tick：清残余角速度、消费跳跃、全速移动、转向。</summary>
        public void TickLocomotion(float dt)
        {
            ClearResidualAngularVelocity();

            Vector3 dir = new Vector3(_moveAxis.x, 0f, _moveAxis.y);
            float speed = _moveSpeed * _speedMultiplier;
            Vector3 velocity = _rb.velocity;

            // 起跳：仅在地面上生效，直接给竖直速度；不满足条件则丢弃本次输入。
            if (_jumpQueued)
            {
                _jumpQueued = false;
                if (IsGrounded())
                    velocity.y = _jumpSpeed;
            }

            Vector3 desired = dir * speed;
            desired.y = velocity.y;
            _rb.velocity = desired;

            TurnTowards(dir, dt);
        }

        /// <summary>击退/击飞硬直 Tick：丢弃跳跃，只给很弱的空中控制，保留击退手感。</summary>
        public void TickAirControl(float dt)
        {
            ClearResidualAngularVelocity();
            _jumpQueued = false;

            Vector3 dir = new Vector3(_moveAxis.x, 0f, _moveAxis.y);
            float speed = _moveSpeed * _speedMultiplier;

            _knockbackTimer -= dt;
            Vector3 velocity = _rb.velocity;
            Vector3 desired = dir * (speed * 0.3f);
            desired.y = velocity.y;
            _rb.velocity = Vector3.Lerp(velocity, desired, 2f * dt);

            TurnTowards(dir, dt);
        }

        /// <summary>眩晕 Tick：丢弃跳跃，水平速度归零（保留竖直速度，避免悬空定格）。</summary>
        public void TickStunned(float dt)
        {
            ClearResidualAngularVelocity();
            StopHorizontal();
        }

        /// <summary>水平急停（保留竖直速度）并丢弃缓存的跳跃。进入眩晕状态时调用。</summary>
        public void StopHorizontal()
        {
            _jumpQueued = false;
            Vector3 velocity = _rb.velocity;
            _rb.velocity = new Vector3(0f, velocity.y, 0f);
        }

        /// <summary>
        /// 退出重击飞行（LaunchedState.OnExit 调用）：清除飞行标记；
        /// 正常落地时播放史莱姆压扁脉冲（撞墙进眩晕的情况不播，眩晕有自身表现）。
        /// </summary>
        public void EndLaunch()
        {
            if (!_isLaunched) return;
            _isLaunched = false;
            if (!IsStunned && _squashDriver != null)
                _squashDriver.PulseSquash(-0.7f);
        }

        /// <summary>眩晕结束（StunBuff.OnDetach 调用）：驱动状态机回到正常状态。</summary>
        public void EndStun()
        {
            if (_fsm != null) _fsm.Fire(PlayerFsmComponent.StunEndTrigger);
        }

        /// <summary>
        /// 施加冲刺（拳套蓄力冲拳带动持有者，仅 Owner 端有效）：
        /// 持续 duration 秒保持 dashSpeed 的平坦前冲速度，由 Dash 状态驱动。
        /// </summary>
        public void ApplyDash(Vector3 direction, float speed, float duration)
        {
            if (!_netObject.IsOwner) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) direction = transform.forward;

            _dashDir = direction.normalized;
            _dashSpeed = speed;
            _dashTimer = duration;
            _jumpQueued = false;
            // 史莱姆前冲拉伸脉冲。
            if (_squashDriver != null) _squashDriver.PulseSquash(0.6f);
            // 驱动状态机进入冲刺状态（从 Launched 进入时会先走 OnExit 收尾飞行）。
            if (_fsm != null) _fsm.Fire(PlayerFsmComponent.DashTrigger);
        }

        /// <summary>冲刺 Tick：保持平坦前冲速度（保留竖直速度），朝向冲刺方向。</summary>
        public void TickDash(float dt)
        {
            ClearResidualAngularVelocity();
            _jumpQueued = false;

            _dashTimer -= dt;
            Vector3 velocity = _rb.velocity;
            _rb.velocity = new Vector3(_dashDir.x * _dashSpeed, velocity.y, _dashDir.z * _dashSpeed);

            TurnTowards(_dashDir, dt);
        }

        /// <summary>提前结束冲刺（冲拳命中敌人时的急停手感）。</summary>
        public void EndDash()
        {
            _dashTimer = 0f;
        }

        // ---------------- 击飞位移预测（仅非 Owner 端） ----------------

        /// <summary>
        /// 击飞位移预测：攻击者本地命中敌人时立即调用，让敌人的本地镜像立刻飞出。
        /// 只模拟弹道位移（水平匀速 + 竖直重力），不改任何状态——
        /// 眩晕/状态机/Buff 仍等受害者 Owner → 服务器的权威链路；
        /// 权威击飞经 NetTransformSync 到达后预测结束，平滑混合回同步流。
        /// 与权威执行共享 LaunchEffect 参数，两端手感一致。
        /// </summary>
        public void PredictLaunch(LaunchEffect effect)
        {
            if (_netObject == null || !_netObject.IsSpawned || _netObject.IsOwner) return;
            if (effect == null) return;

            _predictedVelocity = effect.Impulse(transform.forward);
            _predictedTimer = effect.AirTime;
            _predictingLaunch = true;
            if (_transformSync != null) _transformSync.PredictionActive = true;
            // 史莱姆受击拉伸脉冲（与权威端一致的表现）。
            if (_squashDriver != null) _squashDriver.PulseSquash(effect.SquashIntensity);
        }

        /// <summary>结束位移预测：交还 NetTransformSync 插值（自动平滑混合）。</summary>
        private void StopPrediction()
        {
            if (!_predictingLaunch) return;
            _predictingLaunch = false;
            if (_transformSync != null) _transformSync.PredictionActive = false;
        }

        private void FixedUpdate()
        {
            if (!_predictingLaunch) return;

            // 变成 Owner（所有权迁移）或对象消失时立即终止预测。
            if (_netObject == null || !_netObject.IsSpawned || _netObject.IsOwner)
            {
                StopPrediction();
                return;
            }

            float dt = Time.fixedDeltaTime;
            _predictedTimer -= dt;
            _predictedVelocity += Physics.gravity * dt;

            Vector3 step = _predictedVelocity * dt;

            // 扫掠检测：撞上障碍（非玩家、非地面）时提前结束预测，
            // 与 Owner 端 OnCollisionEnter 的撞墙判定一致，防止预测穿墙；
            // 此处只停位移，眩晕表现仍等服务器 NetVar。
            if (_rb.SweepTest(step.normalized, out RaycastHit hit, step.magnitude))
            {
                bool isPlayer = hit.collider.GetComponentInParent<NetworkIdentity>() != null;
                if (!isPlayer && hit.normal.y < _groundNormalY)
                {
                    StopPrediction();
                    return;
                }
            }

            _rb.MovePosition(transform.position + step);

            // 落地或滞空时间耗尽：结束预测。
            if (_predictedTimer <= 0f || (_predictedVelocity.y < 0f && IsGrounded()))
                StopPrediction();
        }

        /// <summary>清除碰撞带来的残余角速度（双保险，Prefab 上已冻结旋转）。</summary>
        private void ClearResidualAngularVelocity()
        {
            if (_rb.angularVelocity.sqrMagnitude > 0.0001f)
                _rb.angularVelocity = Vector3.zero;
        }

        private void TurnTowards(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude <= 0.001f) return;
            Quaternion target = Quaternion.LookRotation(dir);
            _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, target, _turnSpeed * dt));
        }

        // ---------------- 外部施加的击飞/眩晕 ----------------

        /// <summary>
        /// 施加击飞（仅 Owner 端有效）：普通拍击与拳套重击的统一入口，
        /// 效果参数见 LaunchEffect（与位移预测共享同一组参数）。
        /// 重击（Heavy）进入 Launched 状态，飞行途中撞墙陷入眩晕（见 OnCollisionEnter）。
        /// </summary>
        public void ApplyLaunch(LaunchEffect effect)
        {
            if (!_netObject.IsOwner || effect == null) return;

            _knockbackTimer = Mathf.Max(effect.AirTime, _knockbackRecoverTime);
            _isLaunched = effect.Heavy;
            if (effect.StunDuration > 0f) _stunDuration = effect.StunDuration;
            _rb.AddForce(effect.Impulse(transform.forward), ForceMode.VelocityChange);
            // 史莱姆受击拉伸脉冲。
            if (_squashDriver != null) _squashDriver.PulseSquash(effect.SquashIntensity);
            // 重击驱动状态机进入重击飞行状态；轻击退留在正常状态走弱空控。
            if (effect.Heavy && _fsm != null) _fsm.Fire(PlayerFsmComponent.LaunchTrigger);
        }

        /// <summary>
        /// 重击飞行中撞到障碍物（非玩家、非地面）时陷入眩晕。
        /// 刚体只在 Owner 端模拟物理，天然只判定一次，无需额外网络同步。
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!_isLaunched || _netObject == null || !_netObject.IsOwner) return;

            // 排除玩家（撞人不算撞障碍）。
            if (collision.gameObject.GetComponentInParent<NetworkIdentity>() != null) return;

            // 依据接触点平均法线排除地面/可站立面（落地不算撞障碍）。
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < collision.contactCount; i++)
                normal += collision.GetContact(i).normal;
            if (normal.sqrMagnitude < 0.001f) return;
            normal.Normalize();
            if (normal.y >= _groundNormalY) return;

            // 命中障碍：结束飞行并陷入眩晕。
            _isLaunched = false;
            _knockbackTimer = 0f;
            _rb.velocity = new Vector3(0f, _rb.velocity.y, 0f);

            if (_buffs != null) _buffs.AddBuff(new StunBuff(_stunDuration));

            // 上报服务器写 NetVar，让全端播放眩晕表现。
            NetworkIdentity identity = GetComponent<NetworkIdentity>();
            if (identity != null) identity.ReportStunned(true);

            EventBus.Publish(new PlayerStunnedEvent { NetId = _netObject.NetId, Duration = _stunDuration });

            // 驱动状态机进入眩晕状态（StunBuff 已挂，Launched.OnExit 不会播落地脉冲）。
            if (_fsm != null) _fsm.Fire(PlayerFsmComponent.StunTrigger);
        }

        /// <summary>设置移速倍率（SpeedUp 技能用）。</summary>
        public void SetSpeedMultiplier(float multiplier)
        {
            _speedMultiplier = Mathf.Max(0f, multiplier);
        }

        /// <summary>
        /// 地面检测：从脚底略高处向下打射线。
        /// 射线起点在自身胶囊体内部，射线不会命中自身（背面不命中），因此无需 LayerMask 排除。
        /// </summary>
        public bool IsGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * 0.05f;
            return Physics.Raycast(origin, Vector3.down, _groundCheckDistance + 0.05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
