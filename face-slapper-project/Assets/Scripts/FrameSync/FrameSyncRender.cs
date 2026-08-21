using UnityEngine;

namespace FaceSlapper.FrameSync
{
    /// <summary>
    /// 帧同步渲染层：在相邻两个模拟状态之间插值，使 30Hz 的逻辑帧在渲染帧率下平滑显示。
    /// 本组件只写 transform，不参与任何模拟状态计算（浮点插值/朝向换算不影响确定性）。
    /// </summary>
    [RequireComponent(typeof(FrameSyncMovement))]
    public class FrameSyncRender : MonoBehaviour
    {
        private FrameSyncMovement _sim;

        private void Awake()
        {
            _sim = GetComponent<FrameSyncMovement>();
        }

        private void LateUpdate()
        {
            FrameSyncManager mgr = FrameSyncManager.Instance;
            if (mgr == null || !mgr.IsRunning) return;

            float alpha = mgr.RenderAlpha;
            Vector3 prev = FrameSyncMovement.ToVector3(_sim.PrevState.Position);
            Vector3 cur = FrameSyncMovement.ToVector3(_sim.State.Position);
            transform.position = Vector3.LerpUnclamped(prev, cur, alpha);

            // 朝向仅作渲染表现，可用浮点三角函数。
            FPVec2 facing = _sim.State.Facing;
            if (facing.SqrMagnitude.Raw > 0)
            {
                float yaw = Mathf.Atan2(facing.X.ToFloat(), facing.Y.ToFloat()) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }
    }
}
