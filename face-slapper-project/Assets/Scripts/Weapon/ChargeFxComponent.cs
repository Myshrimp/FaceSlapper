using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 拳套蓄力特效（固定 prefab 资产，见 Prefabs/ChargeFx.prefab）：
    /// 子物体 Distortion = 透明空间扭曲球，Particles = 向内收缩粒子。
    /// SetCharge 由 BoxingGloveWeapon 的蓄力 NetVar 驱动，全端可见；
    /// 内部对网络量化后的蓄力值做平滑跟随，脉冲频率随蓄力加快；
    /// 结束时先停发射，残留粒子飞入中心后再隐藏。
    /// </summary>
    public class ChargeFxComponent : MonoBehaviour
    {
        [Header("引用（prefab 内子物体）")]
        [SerializeField] private Renderer _distortionRenderer;
        [SerializeField] private ParticleSystem _particles;

        [Header("空间扭曲")]
        [SerializeField] private float _distortionMinScale = 0.5f;
        [SerializeField] private float _distortionMaxScale = 1f;
        [SerializeField] private float _maxDistortion = 0.18f;

        [Header("粒子")]
        [SerializeField] private float _minEmission = 25f;
        [SerializeField] private float _maxEmission = 120f;
        [SerializeField] private float _maxSimSpeed = 2.2f;

        [Tooltip("网络量化更新后的蓄力值平滑跟随速度")]
        [SerializeField] private float _followSpeed = 10f;

        private Material _distortionMat;
        private float _targetCharge;
        private float _charge;
        private float _pulseTime;
        private bool _fxActive;

        /// <summary>设置蓄力进度 0-1（由武器蓄力 NetVar 驱动，全端调用）。</summary>
        public void SetCharge(float charge01) => _targetCharge = Mathf.Clamp01(charge01);

        private void Awake()
        {
            // 实例化材质副本，避免运行时改动共享材质资产。
            if (_distortionRenderer != null)
            {
                _distortionMat = _distortionRenderer.material;
                _distortionMat.SetFloat("_Distortion", 0f);
            }
            else
            {
                Debug.LogWarning("[ChargeFx] prefab 未配置 _distortionRenderer。");
            }

            if (_particles != null)
            {
                ParticleSystem.EmissionModule emission = _particles.emission;
                emission.rateOverTime = 0f;
            }
            else
            {
                Debug.LogWarning("[ChargeFx] prefab 未配置 _particles。");
            }

            SetChildrenActive(false);
        }

        private void OnDestroy()
        {
            if (_distortionMat != null) Destroy(_distortionMat);
        }

        private void SetChildrenActive(bool active)
        {
            if (_fxActive == active) return;
            _fxActive = active;
            if (_distortionRenderer != null) _distortionRenderer.gameObject.SetActive(active);
            if (_particles != null) _particles.gameObject.SetActive(active);
        }

        private void Update()
        {
            // 网络量化更新（16 级）后的本地平滑跟随。
            _charge = Mathf.Lerp(_charge, _targetCharge, 1f - Mathf.Exp(-_followSpeed * Time.deltaTime));

            bool wantActive = _targetCharge > 0.001f || _charge > 0.01f;
            if (!wantActive && _fxActive && _particles != null)
            {
                // 收尾：停止发射，等残留粒子飞入中心后再隐藏。
                ParticleSystem.EmissionModule tail = _particles.emission;
                tail.rateOverTime = 0f;
                wantActive = _particles.particleCount > 0;
            }
            SetChildrenActive(wantActive);
            if (!_fxActive) return;

            // 蓄力越深脉冲越快，幅度随蓄力放大。
            _pulseTime += Time.deltaTime * (2f + _charge * 8f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(_pulseTime * Mathf.PI);
            float breathe = 0.95f + 0.1f * pulse;

            if (_distortionRenderer != null)
            {
                float scale = Mathf.Lerp(_distortionMinScale, _distortionMaxScale, _charge) * breathe;
                _distortionRenderer.transform.localScale = Vector3.one * scale;
                _distortionMat.SetFloat("_Distortion", _maxDistortion * _charge * (0.8f + 0.4f * pulse));
            }

            if (_particles != null && _targetCharge > 0.001f)
            {
                ParticleSystem.EmissionModule emission = _particles.emission;
                emission.rateOverTime = Mathf.Lerp(_minEmission, _maxEmission, _charge);
                ParticleSystem.MainModule main = _particles.main;
                main.simulationSpeed = Mathf.Lerp(1f, _maxSimSpeed, _charge);
            }
        }
    }
}
