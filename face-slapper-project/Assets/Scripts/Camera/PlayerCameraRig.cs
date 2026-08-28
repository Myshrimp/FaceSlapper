using Cinemachine;
using FaceSlapper.Battle;
using FaceSlapper.Core;
using FaceSlapper.Input;
using UnityEngine;

namespace FaceSlapper.Camera
{
    /// <summary>
    /// 玩家相机（Cinemachine 管理，类猛兽派对俯视角）：
    /// 运行时创建 VirtualCamera（Transposer 跟随 + Perlin 噪声抖动），主相机由 CinemachineBrain 驱动；
    /// 固定偏航角不随鼠标旋转，滚轮调节视野距离（同时微调俯仰角），平滑跟随本地玩家。
    /// 抖动接口：蓄力轻微持续抖动（SetChargeShake），命中敌人强烈抖动、随时间衰减（HitShake）。
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class PlayerCameraRig : MonoBehaviour
    {
        [Header("跟随")]
        [SerializeField] private float _smoothTime = 0.12f;
        [SerializeField] private Vector3 _lookAtOffset = new Vector3(0f, 1f, 0f);

        [Header("缩放")]
        [SerializeField] private float _distance = 12f;
        [SerializeField] private float _minDistance = 6f;
        [SerializeField] private float _maxDistance = 22f;
        [SerializeField] private float _zoomSpeed = 2.5f;

        [Header("俯仰（随缩放插值）")]
        [SerializeField] private float _minPitch = 45f;
        [SerializeField] private float _maxPitch = 65f;

        [Header("蓄力抖动（轻微持续）")]
        [SerializeField] private float _chargeMaxAmplitude = 0.3f;
        [SerializeField] private float _chargeFrequency = 1.3f;

        [Header("命中抖动（强烈，指数衰减）")]
        [SerializeField] private float _hitAmplitude = 1.6f;
        [SerializeField] private float _hitFrequency = 2.2f;
        [Tooltip("命中抖动强度每秒衰减量（1 约 0.4 秒归零）")]
        [SerializeField] private float _hitDecay = 2.5f;

        /// <summary>场景内唯一实例（武器等系统经此触发抖动，可空）。</summary>
        public static PlayerCameraRig Instance { get; private set; }

        private CinemachineVirtualCamera _vcam;
        private CinemachineTransposer _transposer;
        private CinemachineBasicMultiChannelPerlin _perlin;
        private NoiseSettings _noiseProfile;

        private Transform _target;
        private float _chargeShake; // 蓄力持续抖动强度 0-1（满蓄力最强）
        private float _hitShake;    // 命中抖动残余强度 0-1

        private void Awake()
        {
            Instance = this;
            BuildRig();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_vcam != null) Destroy(_vcam.gameObject);
            if (_noiseProfile != null) Destroy(_noiseProfile);
        }

        /// <summary>主相机挂 CinemachineBrain，运行时创建并配置 VirtualCamera。</summary>
        private void BuildRig()
        {
            if (GetComponent<CinemachineBrain>() == null)
                gameObject.AddComponent<CinemachineBrain>();

            var vcamGo = new GameObject("PlayerVcam");
            // 初始位姿接管主相机当前机位，避免绑定目标前画面跳到原点。
            vcamGo.transform.SetPositionAndRotation(transform.position, transform.rotation);
            _vcam = vcamGo.AddComponent<CinemachineVirtualCamera>();
            _vcam.Priority = 10;

            // Body：Transposer（世界空间偏移跟随），旋转不由 CM 驱动（Aim 留空 Do Nothing），
            // 俯仰角由本组件直接写 vcam 变换，保持固定偏航。
            _transposer = _vcam.AddCinemachineComponent<CinemachineTransposer>();
            _transposer.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;
            _transposer.m_XDamping = _smoothTime;
            _transposer.m_YDamping = _smoothTime;
            _transposer.m_ZDamping = _smoothTime;

            // Noise：运行时构建 NoiseSettings，无需预置资产。
            _noiseProfile = CreateNoiseProfile();
            _perlin = _vcam.AddCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
            _perlin.m_NoiseProfile = _noiseProfile;
            _perlin.m_AmplitudeGain = 0f;
            _perlin.m_FrequencyGain = 1f;
        }

        /// <summary>位置低频大振幅 + 旋转高频小振幅的手持感噪声。</summary>
        private static NoiseSettings CreateNoiseProfile()
        {
            var profile = ScriptableObject.CreateInstance<NoiseSettings>();
            profile.PositionNoise = new[]
            {
                new NoiseSettings.TransformNoiseParams
                {
                    X = new NoiseSettings.NoiseParams { Amplitude = 1f, Frequency = 2.6f },
                    Y = new NoiseSettings.NoiseParams { Amplitude = 1f, Frequency = 3.1f },
                    Z = new NoiseSettings.NoiseParams { Amplitude = 0.6f, Frequency = 2.2f },
                },
            };
            profile.OrientationNoise = new[]
            {
                new NoiseSettings.TransformNoiseParams
                {
                    X = new NoiseSettings.NoiseParams { Amplitude = 0.6f, Frequency = 2.4f },
                    Y = new NoiseSettings.NoiseParams { Amplitude = 0.6f, Frequency = 2.9f },
                    Z = new NoiseSettings.NoiseParams { Amplitude = 0.4f, Frequency = 3.3f },
                },
            };
            return profile;
        }

        /// <summary>拳套蓄力抖动：蓄力进度 0-1 映射为轻微持续抖动；传 0 表示停止（松开/取消）。</summary>
        public void SetChargeShake(float charge01) => _chargeShake = Mathf.Clamp01(charge01);

        /// <summary>命中敌人抖动：一次性强烈抖动，随时间衰减；连续命中取更强者。</summary>
        public void HitShake(float intensity = 1f) => _hitShake = Mathf.Max(_hitShake, Mathf.Clamp01(intensity));

        private void OnEnable()
        {
            EventBus.Subscribe<LocalPlayerSpawnedEvent>(OnLocalPlayerSpawned);
            EventBus.Subscribe<LocalPlayerDespawnedEvent>(OnLocalPlayerDespawned);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<LocalPlayerSpawnedEvent>(OnLocalPlayerSpawned);
            EventBus.Unsubscribe<LocalPlayerDespawnedEvent>(OnLocalPlayerDespawned);
        }

        private void OnLocalPlayerSpawned(LocalPlayerSpawnedEvent e) => _target = e.Player.transform;

        private void OnLocalPlayerDespawned(LocalPlayerDespawnedEvent e) => _target = null;

        /// <summary>手动设置跟随目标（可选，默认自动绑定本地玩家）。</summary>
        public void SetTarget(Transform target) => _target = target;

        private void LateUpdate()
        {
            if (_vcam == null) return;

            if (_target == null)
            {
                // 兜底：本地玩家已存在但事件错过（例如相机后启用）。
                if (Movement.LocalInstance != null) _target = Movement.LocalInstance.transform;
            }

            if (_target != null)
            {
                float scroll = 0f;
                if (GameManager.HasInstance)
                {
                    InputComponent input = GameManager.Instance.Get<InputComponent>();
                    if (input != null) scroll = input.Current.ScrollDelta;
                }

                _distance = Mathf.Clamp(_distance - scroll * _zoomSpeed, _minDistance, _maxDistance);
                float t = Mathf.InverseLerp(_minDistance, _maxDistance, _distance);
                float pitch = Mathf.Lerp(_minPitch, _maxPitch, t);

                Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
                _vcam.transform.rotation = rotation;
                _transposer.m_FollowOffset = _lookAtOffset + rotation * Vector3.back * _distance;
                if (_vcam.Follow != _target) _vcam.Follow = _target;
            }

            // 抖动合成：蓄力持续（随进度增强）+ 命中冲击（随时间衰减）。
            _hitShake = Mathf.Max(0f, _hitShake - _hitDecay * Time.deltaTime);
            if (_perlin != null)
            {
                _perlin.m_AmplitudeGain = _chargeShake * _chargeMaxAmplitude + _hitShake * _hitAmplitude;
                _perlin.m_FrequencyGain = 1f
                    + _chargeShake * (_chargeFrequency - 1f)
                    + _hitShake * (_hitFrequency - 1f);
            }
        }
    }
}
