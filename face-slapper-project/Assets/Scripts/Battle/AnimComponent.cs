using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 玩家动画组件：驱动玩家身上的 Animator，并把攻击（耳光）动画同步到所有端。
    /// 链路：攻击方 Owner 发起 → 服务器中转 → 广播给所有观察者播放。
    /// 眨眼是纯本地表现，各端随机播放，不占用带宽。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class AnimComponent : NetBehaviour
    {
        [Header("动画状态")]
        [Tooltip("攻击（耳光）动画状态名")]
        [SerializeField] private string _slapState = "Slap";

        [Tooltip("待机动画状态名（攻击动画播完后自动切回）")]

        [Header("眨眼（本地表现，不同步）")]
        [SerializeField] private float _blinkIntervalMin = 2f;
        [SerializeField] private float _blinkIntervalMax = 5f;
        [Tooltip("眨眼时长（秒），应与 IdleEyes 剪辑长度一致。Animator 中 IdleEyes 状态的时间由 blinkTime 参数驱动")]
        [SerializeField] private float _blinkDuration = 1f;

        [Tooltip("眨眼时长（秒），应与 IdleEyes 剪辑长度一致。Animator 中 IdleEyes 状态的时间由 blinkTime 参数驱动")]
        [SerializeField] private float _slapDuration = 1f;

        private static readonly int BlinkTimeHash = Animator.StringToHash("blinkTime");
        private static readonly int SlapTriggerHash = Animator.StringToHash("slapTrigger");
        private static readonly int SlapEndTriggerHash = Animator.StringToHash("slapEndTrigger");

        private static readonly int BaseLayer = 0;
        private static readonly int EyesLayer = 1;
        private static readonly int HandsLayer = 2;


        private Animator _animator;
        private float _nextBlinkTime;
        private float _blinkTimer = -1f; // >=0 表示正在眨眼

        protected override void Awake()
        {
            base.Awake();
            _animator = GetComponent<Animator>();
            _nextBlinkTime = Time.time + Random.Range(_blinkIntervalMin, _blinkIntervalMax);
        }

        private void Update()
        {
            // 眨眼：纯表现，各端本地随机即可。
            // IdleEyes 状态开启了 Time Parameter（blinkTime），需手动推进参数值来播放。
            if (_blinkTimer >= 0f)
            {
                _blinkTimer += Time.deltaTime;
                if (_blinkTimer >= _blinkDuration)
                {
                    _blinkTimer = -1f;
                    _animator.SetFloat(BlinkTimeHash, 0f); // 回到第 0 帧（睁眼姿势）
                }
                else
                {
                    _animator.SetFloat(BlinkTimeHash, _blinkTimer);
                }
            }
            else if (Time.time >= _nextBlinkTime)
            {
                _nextBlinkTime = Time.time + Random.Range(_blinkIntervalMin, _blinkIntervalMax);
                _blinkTimer = 0f;
            }

            // 攻击动画播完后自动切回待机（Animator 里未配置 Slap→Idle 的退出过渡）。
            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(HandsLayer);
            if (state.IsName(_slapState) && state.normalizedTime >= 1f)
            {
                _animator.SetTrigger(SlapEndTriggerHash);
            }
        }

        /// <summary>播放耳光/攻击动画（由攻击方 Owner 端调用，经服务器中转广播到所有端）。</summary>
        public void PlaySlap()
        {
            if (IsServer) SendObserversRpc(nameof(RpcPlaySlap));
            else SendServerRpc(nameof(CmdPlaySlap));
        }

        /// <summary>服务器中转（ServerRpc 语义，仅服务器执行）。</summary>
        [NetRpc]
        private void CmdPlaySlap()
        {
            SendObserversRpc(nameof(RpcPlaySlap));
        }

        /// <summary>在所有客户端上实际播放动画。</summary>
        [NetRpc]
        private void RpcPlaySlap()
        {
            _animator.SetTrigger(SlapTriggerHash);
        }
    }
}
