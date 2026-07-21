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
        [SerializeField] private float _knockbackRecoverTime = 0.35f;
        [SerializeField] private float _knockbackUpRatio = 0.5f;

        private Rigidbody _rb;
        private NetObject _netObject;
        private float _speedMultiplier = 1f;
        private float _knockbackTimer;

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

        private void FixedUpdate()
        {
            if (!_netObject.IsSpawned || !_netObject.IsOwner) return;

            Vector2 axis = Vector2.zero;
            if (GameManager.HasInstance)
            {
                InputComponent input = GameManager.Instance.Get<InputComponent>();
                if (input != null) axis = input.Current.MoveAxis;
            }

            float dt = Time.fixedDeltaTime;
            Vector3 dir = new Vector3(axis.x, 0f, axis.y);
            float speed = _moveSpeed * _speedMultiplier;

            Vector3 velocity = _rb.velocity;
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
    }
}
