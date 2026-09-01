using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 射线检测：从检测中心沿持有者前向打射线（_radius &gt; 0 时为球扫 SphereCast），
    /// 适合长条形判定（突刺、激光、直线冲击）。命中按距离从近到远写入。
    /// </summary>
    public class RayHitDetector : HitDetector
    {
        [Tooltip("射线长度（米）。")]
        [SerializeField] private float _distance = 3f;

        [Tooltip("球扫半径（米）。0 为细射线，&gt;0 为 SphereCast。")]
        [SerializeField] private float _radius = 0f;

        private readonly RaycastHit[] _rayBuffer = new RaycastHit[32];

        protected override int QueryRaw(Vector3 center, Vector3 forward, HitDetectContext ctx)
        {
            int n = _radius > 0f
                ? Physics.SphereCastNonAlloc(center, _radius, forward, _rayBuffer, _distance, _layerMask)
                : Physics.RaycastNonAlloc(center, forward, _rayBuffer, _distance, _layerMask);

            // 转成基类统一的 Collider 缓冲，复用过滤逻辑。
            int count = Mathf.Min(n, _colliderBuffer.Length);
            for (int i = 0; i < count; i++)
                _colliderBuffer[i] = _rayBuffer[i].collider;
            return count;
        }
    }
}
