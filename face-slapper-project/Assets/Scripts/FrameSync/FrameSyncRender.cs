using UnityEngine;

namespace FaceSlapper.FrameSync
{
    /// <summary>
    /// 帧同步渲染层：在相邻两个模拟状态之间插值，使 30Hz 的逻辑帧在渲染帧率下平滑显示。
    /// 本组件只写 transform，不参与任何模拟状态计算（浮点插值/朝向换算不影响确定性）。
    /// 回滚平滑：回滚修正会使目标位置瞬间跳变（正常逐帧插值的位移远小于阈值），
    /// 检测到跳变时进入修正模式，显示位置按指数收敛追上目标，避免瞬移穿模。
    /// </summary>
    [RequireComponent(typeof(FrameSyncMovement))]
    public class FrameSyncRender : MonoBehaviour
    {
        // 回滚平滑参数（纯渲染，不进模拟）。
        private const float CorrectionThreshold = 0.01f;   // 目标跳变超过 0.25m 判定为回滚修正
        private const float CorrectionSnapEpsilon = 0.02f; // 收敛到 2cm 内则贴合并退出修正
        private const float CorrectionSpeed = 8f;         // 指数收敛速率（约 0.2s 收敛）

        private FrameSyncMovement _sim;

        // 挥击表现（纯渲染）：驱动模型上的 HandR 子节点，不参与模拟。
        private Transform _handR;
        private Quaternion _handRBaseLocalRot;

        // 回滚平滑状态。
        private Vector3 _visualPos;
        private bool _visualInit;
        private bool _correcting;

        private void Awake()
        {
            _sim = GetComponent<FrameSyncMovement>();
            _handR = FindChildByName(transform, "HandR");
            if (_handR != null) _handRBaseLocalRot = _handR.localRotation;
        }

        private void LateUpdate()
        {
            FrameSyncManager mgr = FrameSyncManager.Instance;
            if (mgr == null || !mgr.IsRunning)
            {
                _visualInit = false; // 会话外/结束后下一帧重新贴合，避免沿用旧视觉位置
                return;
            }

            float alpha = mgr.RenderAlpha;
            Vector3 prev = FrameSyncMovement.ToVector3(_sim.PrevState.Position);
            Vector3 cur = FrameSyncMovement.ToVector3(_sim.State.Position);
            Vector3 target = Vector3.LerpUnclamped(prev, cur, alpha);
            transform.position = SmoothToward(target);

            // 朝向仅作渲染表现，可用浮点三角函数。
            FPVec2 facing = _sim.State.Facing;
            if (facing.SqrMagnitude.Raw > 0)
            {
                float yaw = Mathf.Atan2(facing.X.ToFloat(), facing.Y.ToFloat()) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            ApplySwing();
        }

        /// <summary>
        /// 回滚平滑：正常情况下显示位置直接等于插值目标（每帧位移极小）；
        /// 目标瞬间跳变超阈值（回滚修正）时进入修正模式，指数收敛追上目标。
        /// </summary>
        private Vector3 SmoothToward(Vector3 target)
        {
            if (!_visualInit)
            {
                _visualPos = target;
                _visualInit = true;
                _correcting = false;
                return _visualPos;
            }

            if (!_correcting && Vector3.Distance(_visualPos, target) > CorrectionThreshold)
                _correcting = true;

            if (_correcting)
            {
                float t = 1f - Mathf.Exp(-CorrectionSpeed * Time.deltaTime);
                _visualPos = Vector3.Lerp(_visualPos, target, t);
                if (Vector3.Distance(_visualPos, target) < CorrectionSnapEpsilon)
                {
                    _visualPos = target;
                    _correcting = false;
                }
            }
            else
            {
                _visualPos = target;
            }
            return _visualPos;
        }

        /// <summary>按模拟状态中的挥击倒计时驱动手臂摆动（对齐状态同步版 -80° 挥角）。</summary>
        private void ApplySwing()
        {
            if (_handR == null) return;

            int swing = _sim.State.SwingTicks;
            if (swing <= 0)
            {
                _handR.localRotation = _handRBaseLocalRot;
                return;
            }
            float progress = 1f - swing / (float)FrameSyncSim.SwingTotalTicks;
            float angle = Mathf.Sin(progress * Mathf.PI) * -80f;
            _handR.localRotation = _handRBaseLocalRot * Quaternion.Euler(angle, 0f, 0f);
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildByName(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
