using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>球形范围检测：以检测中心为球心做 OverlapSphere。</summary>
    public class SphereHitDetector : HitDetector
    {
        [Tooltip("检测球半径（米）。")]
        [SerializeField] private float _radius = 1.4f;

        /// <summary>检测半径（效果计算服务器校验距离时可能需要读取）。</summary>
        public float Radius => _radius;

        protected override int QueryRaw(Vector3 center, Vector3 forward, HitDetectContext ctx)
        {
            return Physics.OverlapSphereNonAlloc(center, _radius, _colliderBuffer, _layerMask);
        }
    }
}
