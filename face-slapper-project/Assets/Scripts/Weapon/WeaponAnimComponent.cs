using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 武器动画组件：攻击动作表现（挥舞 / 直拳前冲），与武器逻辑解耦。
    /// 播放时机由武器状态机（Attack 状态进入）触发；
    /// WeaponBase.Update 在跟随手部挂点之后调用 Apply 叠加位移/旋转，
    /// 表现经 NetTransformSync 随武器变换同步给其他端。
    /// </summary>
    public class WeaponAnimComponent : MonoBehaviour
    {
        /// <summary>攻击动画样式。</summary>
        public enum AnimStyle
        {
            /// <summary>绕持有者右轴挥动（拍子）。</summary>
            Swing,
            /// <summary>向前快速伸缩（拳套）。</summary>
            Lunge,
        }

        [SerializeField] private AnimStyle _style = AnimStyle.Swing;
        [SerializeField] private float _duration = 0.22f;
        [SerializeField] private float _swingAngle = -80f;
        [SerializeField] private float _lungeDistance = 0.5f;
        [Tooltip("蓄力满时武器后拉的距离（蓄力表现，经武器变换同步广播全端）")]
        [SerializeField] private float _chargePullBack = 0.35f;

        private float _timer = -1f;
        private float _intensity = 1f;

        /// <summary>蓄力进度 0-1（Owner 端蓄力时驱动后拉表现）。</summary>
        public float ChargeAmount { get; set; }

        /// <summary>播放一次攻击动画（播放中重调会重新开始）；intensity 缩放动作幅度（蓄力等级）。</summary>
        public void Play(float intensity = 1f)
        {
            _intensity = Mathf.Max(0f, intensity);
            _timer = 0f;
        }

        public bool IsPlaying => _timer >= 0f;

        public float Duration => _duration;

        /// <summary>把当前动画偏移叠加到武器变换（每帧由 WeaponBase 调用）。</summary>
        public void Apply(Transform weapon)
        {
            // 蓄力后拉：与攻击动画独立，蓄力期间持续生效。
            if (ChargeAmount > 0f)
                weapon.position -= weapon.forward * (ChargeAmount * _chargePullBack);

            if (_timer < 0f) return;

            _timer += Time.deltaTime;
            float progress = Mathf.Clamp01(_timer / _duration);
            float curve = Mathf.Sin(progress * Mathf.PI);

            switch (_style)
            {
                case AnimStyle.Swing:
                    weapon.rotation *= Quaternion.Euler(curve * _swingAngle * _intensity, 0f, 0f);
                    break;
                case AnimStyle.Lunge:
                    weapon.position += weapon.forward * (curve * _lungeDistance * _intensity);
                    break;
            }

            if (progress >= 1f) _timer = -1f;
        }
    }
}
