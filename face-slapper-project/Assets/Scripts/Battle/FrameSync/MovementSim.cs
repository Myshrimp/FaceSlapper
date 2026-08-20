using FaceSlapper.Core;
using FaceSlapper.Networking;
using FaceSlapper.TestFrameSync;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 人物移动/旋转脚本（客户端 Owner 权威）：
    /// Owner 端读取输入模拟刚体运动，由 NetTransformSync 广播到其他端。
    /// 非权威端刚体设为运动学，完全由同步驱动。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetObject))]
    [RequireComponent(typeof(FrameComponent))]
    public class MovementSim : MonoBehaviour
    {
        /// <summary>本机玩家的 Movement（相机、输入系统通过它绑定）。</summary>
        public static MovementSim LocalInstance { get; private set; }

        [Header("移动")]
        [SerializeField] private float _moveSpeed = 6f;
        [SerializeField] private float _turnSpeed = 720f;

        [Header("击飞")]
        [SerializeField] private float _knockbackRecoverTime = 0.35f;
        [SerializeField] private float _knockbackUpRatio = 0.5f;

        [Header("跳跃")]
        [Tooltip("起跳瞬间的竖直速度（米/秒）")]
        [SerializeField] private float _jumpSpeed = 7f;
        [Tooltip("地面检测射线长度（从脚底向下）")]
        [SerializeField] private float _groundCheckDistance = 0.15f;

        private Rigidbody _rb;
        private NetObject _netObject;
        private FrameComponent _frameComp;
        private float _speedMultiplier = 1f;
        private float _knockbackTimer;
        private bool _jumpQueued; // Update 中缓存跳跃输入，FixedUpdate 消费，避免低物理频率下丢输入。

        public float SpeedMultiplier => _speedMultiplier;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _netObject = GetComponent<NetObject>();
            _netObject.OnSpawnServer += RefreshKinematic;
            _netObject.OnSpawnClient += RefreshKinematic;
            _netObject.OnOwnershipChanged += OnOwnershipChanged;
            _netObject.OnDespawnClient += OnDespawnClient;
        }

        private void Start()
        {
            _frameComp = GetComponent<FrameComponent>();
            _frameComp.RegisterInputFrameFunc("Movement", Simulate);
            _frameComp.RegisterInputFrameRevertFunc("Movement", Revert);
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
            if (LocalInstance == this)
            {
                LocalInstance = null;
                EventBus.Publish(new LocalPlayerDespawnedEvent());
            }
        }

        public void Simulate(InputFrame frame)
        {
            if (!_netObject.IsSpawned || !_netObject.IsOwner) return;

            Vector2 axis = Vector2.zero;
            if (GameManager.HasInstance)
            {
                axis = new Vector2(frame.MoveX, frame.MoveZ);
            }

            float dt = Time.fixedDeltaTime;
            Vector3 dir = new Vector3(axis.x, 0f, axis.y);
            float speed = _moveSpeed * _speedMultiplier;

            // 清除碰撞带来的残余角速度（双保险，Prefab 上已冻结旋转）。
            if (_rb.angularVelocity.sqrMagnitude > 0.0001f)
                _rb.angularVelocity = Vector3.zero;

            Vector3 velocity = _rb.velocity;

            // 起跳：仅在地面上且非击飞硬直时生效，直接给竖直速度；不满足条件则丢弃本次输入。
            if (_jumpQueued)
            {
                _jumpQueued = false;
                if (_knockbackTimer <= 0f && IsGrounded())
                    velocity.y = _jumpSpeed;
            }
            if (_knockbackTimer > 0f)
            {
                // 击飞中：只给很弱的空中控制，保留击退手感。
                _knockbackTimer -= dt;
                Vector3 desired = dir * (speed * 0.3f);
                desired.y = velocity.y;
                _rb.velocity = Vector3.Lerp(velocity, desired, 2f * dt);
            }
            else
            {
                Vector3 desired = dir * speed;
                desired.y = velocity.y;
                _rb.velocity = desired;
            }

            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion target = Quaternion.LookRotation(dir);
                _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, target, _turnSpeed * dt));
            }
        }

        public void Revert(InputFrame frame)
        {
            frame.MoveX *= -1;
            frame.MoveZ *= -1;
            Simulate(frame);
        }

        /// <summary>施加击飞冲量（仅 Owner 端有效）。</summary>
        public void ApplyKnockback(Vector3 direction, float force)
        {
            if (!_netObject.IsOwner) return;

            Vector3 flat = direction;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.001f) flat = transform.forward;
            flat.Normalize();

            Vector3 impulse = (flat + Vector3.up * _knockbackUpRatio).normalized * force;
            _knockbackTimer = _knockbackRecoverTime;
            _rb.AddForce(impulse, ForceMode.VelocityChange);
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
        private bool IsGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * 0.05f;
            return Physics.Raycast(origin, Vector3.down, _groundCheckDistance + 0.05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
