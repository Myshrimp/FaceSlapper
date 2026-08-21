using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.FrameSync
{
    /// <summary>
    /// 帧同步玩家实体（PlayerFrameSync.prefab）：持有确定性模拟状态，
    /// 由 FrameSyncManager 按 tick 驱动。不读取本地输入、不做物理模拟——
    /// 所有状态变化只能来自"全员一致的输入序列"。
    /// </summary>
    [RequireComponent(typeof(NetObject))]
    public class FrameSyncMovement : MonoBehaviour
    {
        /// <summary>当前模拟状态（本 tick）。</summary>
        public PlayerSimState State;

        /// <summary>上一 tick 的模拟状态（渲染插值起点）。</summary>
        public PlayerSimState PrevState;

        /// <summary>该实体对应的玩家 clientId（等于网络对象的所有者 Id）。</summary>
        public int SimClientId { get; private set; } = -1;

        public NetObject NetObject => _netObject;

        private NetObject _netObject;
        private bool _registered;

        private void Awake()
        {
            _netObject = GetComponent<NetObject>();
        }

        private void Update()
        {
            // 等待网络生成 + 所有权信息就位后注册到管理器。
            if (_registered || !_netObject.IsSpawned) return;
            int owner = _netObject.OwnerClientId;
            if (owner < 0 || FrameSyncManager.Instance == null) return;

            SimClientId = owner;
            FrameSyncManager.Instance.Register(this);
            _registered = true;
            Debug.Log($"[FrameSync] 实体注册: SimClientId={SimClientId} netId={_netObject.NetId} pos={transform.position}");
        }

        private void OnDestroy()
        {
            if (_registered && FrameSyncManager.Instance != null)
                FrameSyncManager.Instance.Unregister(this);
        }

        /// <summary>开局初始化（各端使用广播的同一份起始状态）。</summary>
        public void InitSimState(PlayerSimState state)
        {
            State = state;
            PrevState = state;
            transform.position = ToVector3(state.Position);
        }

        /// <summary>写回一个 tick 的模拟结果（仅由 FrameSyncManager 调用；自动记录上 tick 状态供渲染插值）。</summary>
        public void ApplyTickResult(PlayerSimState result)
        {
            PrevState = State;
            State = result;
        }

        public static Vector3 ToVector3(FPVec3 v) => new Vector3(v.X.ToFloat(), v.Y.ToFloat(), v.Z.ToFloat());
    }
}
