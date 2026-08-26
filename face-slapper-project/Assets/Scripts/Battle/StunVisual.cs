using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 眩晕表现：头顶若干颗星星环绕旋转（贴花 Quad，运行时自建）。
    /// 显隐由 NetworkIdentity 的 IsStunned NetVar 驱动（全端同步），
    /// 本组件只负责纯表现，不参与逻辑判定。
    /// </summary>
    public class StunVisual : MonoBehaviour
    {
        [Tooltip("星星贴花材质（编辑器搭建脚本注入）")]
        [SerializeField] private Material _starMaterial;
        [SerializeField] private int _starCount = 3;
        [Tooltip("环绕中心高度（球心上方）")]
        [SerializeField] private float _height = 1.35f;
        [SerializeField] private float _radius = 0.35f;
        [SerializeField] private float _starSize = 0.18f;
        [Tooltip("环绕速度（度/秒）")]
        [SerializeField] private float _rotateSpeed = 240f;

        private Transform[] _stars;
        private float _angle;
        private bool _visible;

        private void Awake()
        {
            _stars = new Transform[Mathf.Max(_starCount, 1)];
            for (int i = 0; i < _stars.Length; i++)
            {
                GameObject star = GameObject.CreatePrimitive(PrimitiveType.Quad);
                star.name = $"Star{i}";
                Destroy(star.GetComponent<Collider>());
                star.transform.SetParent(transform, false);
                star.transform.localScale = new Vector3(_starSize, _starSize, 1f);
                if (_starMaterial != null)
                    star.GetComponent<Renderer>().sharedMaterial = _starMaterial;
                _stars[i] = star.transform;
            }
            SetVisible(false);
        }

        /// <summary>显示/隐藏眩晕星星（由 NetworkIdentity 的 NetVar 变化驱动，全端调用）。</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_stars == null) return;
            foreach (Transform star in _stars)
                if (star != null) star.gameObject.SetActive(visible);
        }

        private void Update()
        {
            if (!_visible || _stars == null) return;

            _angle += _rotateSpeed * Time.deltaTime;
            float step = 360f / _stars.Length;
            for (int i = 0; i < _stars.Length; i++)
            {
                float rad = (_angle + step * i) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * _radius;
                // 头顶环绕，星面朝外（与眼睛贴花同一套路，俯视相机可见）。
                _stars[i].localPosition = new Vector3(offset.x, _height, offset.z);
                _stars[i].localRotation = Quaternion.LookRotation(offset.normalized) * Quaternion.Euler(0f, 180f, 0f);
            }
        }
    }
}
