using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>一次命中的结果（已过滤、已去重）。</summary>
    public struct HitResult
    {
        /// <summary>命中的网络对象。</summary>
        public NetObject Target;

        /// <summary>目标的玩家身份信息（过滤阶段已保证非空）。</summary>
        public NetworkIdentity Identity;

        /// <summary>命中点（表现/特效定位用）。</summary>
        public Vector3 Point;
    }

    /// <summary>命中检测上下文：谁发起的、从哪里检测。</summary>
    public struct HitDetectContext
    {
        /// <summary>持有者（检测时排除自身，同时提供水平朝向）。</summary>
        public NetObject Holder;

        /// <summary>检测原点（武器判定点 Tip）。为空时回退到持有者面前位置。</summary>
        public Transform Origin;
    }

    /// <summary>
    /// 命中检测器基类（组件化，可复用）：
    /// 子类只负责"怎么查"（球/方/射线），本类统一负责"怎么过滤"——
    /// 排除持有者、要求目标带 NetworkIdentity、按 NetObject 去重。
    /// 全部走 NonAlloc 物理查询，调用方传入结果缓冲，热路径零 GC。
    /// 武器与技能（如空手耳光）均可挂载复用。
    /// </summary>
    public abstract class HitDetector : MonoBehaviour
    {
        [Tooltip("检测中心偏移（以持有者为系：x 右、y 上、z 前）。设置 Origin 时叠加在 Origin 上。")]
        [SerializeField] protected Vector3 _offset = Vector3.zero;

        [Tooltip("参与检测的层。默认全部（与原 OverlapSphere 行为一致）。")]
        [SerializeField] protected LayerMask _layerMask = ~0;

        /// <summary>物理查询缓冲（非分配）。</summary>
        protected readonly Collider[] _colliderBuffer = new Collider[32];

        /// <summary>
        /// 执行一次检测：把满足条件的目标写入 results，返回数量。
        /// 仅做物理查询与过滤，不含任何效果逻辑。
        /// </summary>
        public int Detect(HitDetectContext ctx, HitResult[] results)
        {
            Vector3 center = ResolveCenter(ctx, out Vector3 forward);
            int raw = QueryRaw(center, forward, ctx);
            return Filter(ctx.Holder, raw, results);
        }

        /// <summary>子类实现：原始物理查询，命中 Collider 写入 _colliderBuffer，返回数量。</summary>
        protected abstract int QueryRaw(Vector3 center, Vector3 forward, HitDetectContext ctx);

        /// <summary>
        /// 解析检测中心与水平前向：
        /// 有 Origin 用 Origin 位置，否则回退到持有者面前（胸口高度 + 前方 1.2m，与原逻辑一致）。
        /// 偏移在持有者水平系内施加（y 直接为世界竖直方向）。
        /// </summary>
        protected Vector3 ResolveCenter(HitDetectContext ctx, out Vector3 forward)
        {
            Transform holderT = ctx.Holder != null ? ctx.Holder.transform : transform;
            forward = holderT.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 basePos = ctx.Origin != null
                ? ctx.Origin.position
                : holderT.position + Vector3.up + forward * 1.2f;

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return basePos + right * _offset.x + Vector3.up * _offset.y + forward * _offset.z;
        }

        /// <summary>统一过滤：排除持有者、要求 NetworkIdentity、按 NetObject 去重。</summary>
        private int Filter(NetObject holder, int rawCount, HitResult[] results)
        {
            int count = 0;
            for (int i = 0; i < rawCount && count < results.Length; i++)
            {
                Collider hit = _colliderBuffer[i];
                if (hit == null) continue;

                NetObject nob = hit.GetComponentInParent<NetObject>();
                if (nob == null || nob == holder) continue;

                NetworkIdentity identity = nob.GetComponent<NetworkIdentity>();
                if (identity == null) continue;

                // 同一 NetObject 多个 Collider 只保留第一次命中。
                bool duplicate = false;
                for (int j = 0; j < count; j++)
                {
                    if (results[j].Target == nob) { duplicate = true; break; }
                }
                if (duplicate) continue;

                results[count] = new HitResult
                {
                    Target = nob,
                    Identity = identity,
                    Point = hit.ClosestPoint(nob.transform.position),
                };
                count++;
            }
            return count;
        }
    }
}
