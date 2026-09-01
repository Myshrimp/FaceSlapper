using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>方形范围检测：以检测中心为盒心、朝向持有者前向做 OverlapBox。</summary>
    public class BoxHitDetector : HitDetector
    {
        [Tooltip("盒体半尺寸（米）：x 宽、y 高、z 纵深（沿前向）。")]
        [SerializeField] private Vector3 _halfExtents = new Vector3(0.8f, 1f, 1.2f);

        protected override int QueryRaw(Vector3 center, Vector3 forward, HitDetectContext ctx)
        {
            Quaternion orientation = Quaternion.LookRotation(forward, Vector3.up);
            return Physics.OverlapBoxNonAlloc(center, _halfExtents, _colliderBuffer, orientation, _layerMask);
        }
    }
}
