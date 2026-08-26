using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 史莱姆挤压拉伸驱动：读取刚体速度，把"本地空间运动方向 + 形变量"
    /// 经 MaterialPropertyBlock 喂给 FaceSlapper/ToonSlime Shader 做顶点形变。
    /// 速度越快沿运动方向拉伸越多；击飞/落地等瞬间可用 PulseSquash 叠加脉冲。
    /// 只写 MaterialPropertyBlock，不修改共享材质，多角色互不串扰。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SlimeSquashDriver : MonoBehaviour
    {
        [Tooltip("形变目标渲染器（不设置则自动取子物体 Body）")]
        [SerializeField] private Renderer _target;
        [Tooltip("达到最大拉伸时的速度（米/秒）")]
        [SerializeField] private float _maxSpeed = 10f;
        [Tooltip("形变量平滑速度")]
        [SerializeField] private float _smooth = 12f;
        [Tooltip("脉冲回弹速度")]
        [SerializeField] private float _pulseRecover = 3f;

        private static readonly int SquashDirId = Shader.PropertyToID("_SquashDir");
        private static readonly int SquashAmountId = Shader.PropertyToID("_SquashAmount");

        private Rigidbody _rb;
        private MaterialPropertyBlock _mpb;
        private Vector3 _dir = Vector3.forward;  // 本地空间形变轴
        private float _amount;                    // 平滑后的形变量
        private float _pulse;                     // 脉冲（正=拉伸，负=压扁）

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _mpb = new MaterialPropertyBlock();
            if (_target == null)
            {
                Transform body = transform.Find("Body");
                if (body != null) _target = body.GetComponent<Renderer>();
            }
        }

        /// <summary>手动形变脉冲（正=瞬间拉伸，负=瞬间压扁；击飞/落地时调用）。</summary>
        public void PulseSquash(float strength)
        {
            _pulse = Mathf.Clamp(_pulse + strength, -1.2f, 1.2f);
        }

        private void Update()
        {
            Vector3 velocity = _rb != null ? _rb.velocity : Vector3.zero;
            float speed01 = Mathf.Clamp01(velocity.magnitude / _maxSpeed);

            _pulse = Mathf.MoveTowards(_pulse, 0f, _pulseRecover * Time.deltaTime);
            float target = speed01 + _pulse;
            _amount = Mathf.Lerp(_amount, target, _smooth * Time.deltaTime);

            if (velocity.sqrMagnitude > 0.01f)
                _dir = transform.InverseTransformDirection(velocity.normalized);

            if (_target == null) return;
            _mpb.SetVector(SquashDirId, _dir);
            _mpb.SetFloat(SquashAmountId, _amount);
            _target.SetPropertyBlock(_mpb);
        }
    }
}
