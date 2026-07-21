using FaceSlapper.Battle;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 武器基类（服务器权威归属 + 持有者"虚拟挂载"）：
    /// - 闲置时在地面由服务器物理模拟（NetTransformSync 广播）；
    /// - 被拾取后服务器写 NetVar HolderNobId 并转移所有权给持有者连接，
    ///   持有者端每帧把武器对齐到手部挂点，其他端靠 NetTransformSync 插值；
    /// - 放下时清除归属、所有权交还服务器、恢复物理。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public abstract class WeaponBase : NetBehaviour, IWeapon
    {
        private readonly NetVar<int> _holderNobId = new NetVar<int>(-1);

        /// <summary>持有者的网络对象 Id，-1 表示闲置。</summary>
        public int HolderNobId => _holderNobId.Value;

        public bool IsHeld => HolderNobId >= 0;

        [Tooltip("攻击判定点（武器尖端）。不设置则用持有者面前位置。")]
        [SerializeField] protected Transform _tip;

        protected Rigidbody _rb;

        protected override void Awake()
        {
            base.Awake();
            _rb = GetComponent<Rigidbody>();
            _holderNobId.OnChange += (prev, next) => ApplyHolderState();
        }

        public override void OnNetSpawnServer() => ApplyHolderState();

        public override void OnNetSpawnClient() => ApplyHolderState();

        public override void OnNetOwnershipChanged(bool isOwner) => ApplyHolderState();

        /// <summary>根据归属状态更新本地表现：运动学开关 + 回写本地玩家的 HeldWeapon。</summary>
        private void ApplyHolderState()
        {
            // 持有时全端运动学（由持有者手部驱动）；闲置时非控制端运动学（跟随同步）。
            if (_rb != null)
                _rb.isKinematic = IsHeld || !NetObj.IsController;

            // 回写本地玩家（仅客户端关心）。
            if (!Net.IsClient) return;
            NetObject localPlayer = Net.Client.LocalPlayer;
            if (localPlayer == null) return;

            var pick = localPlayer.GetComponent<PickWeaponAbility>();
            if (pick == null) return;

            if (IsHeld && localPlayer.NetId == HolderNobId)
            {
                if (pick.HeldWeapon != this)
                {
                    pick.SetHeld(this);
                    OnActivate();
                }
            }
            else if (pick.HeldWeapon == this)
            {
                pick.SetHeld(null);
                OnDeactivate();
            }
        }

        protected virtual void Update()
        {
            if (!IsHeld || !NetObj.IsOwner) return;

            NetObject holder = FindHolder();
            if (holder == null) return;

            Transform socket = FindHandSocket(holder);
            transform.SetPositionAndRotation(socket.position, socket.rotation);
        }

        /// <summary>查找持有者对象（服务器/客户端各自的已生成对象表）。</summary>
        protected NetObject FindHolder()
        {
            if (HolderNobId < 0) return null;
            if (Net.IsServer && Net.Server.TryGetObject(HolderNobId, out NetObject n))
                return n;
            if (Net.IsClient && Net.Client.TryGetObject(HolderNobId, out NetObject n2))
                return n2;
            return null;
        }

        private static Transform FindHandSocket(NetObject holder)
        {
            Transform[] all = holder.GetComponentsInChildren<Transform>();
            foreach (Transform t in all)
            {
                if (t.name == "HandSocket") return t;
            }
            return holder.transform;
        }

        /// <summary>请求拾取（客户端发起，服务器校验距离并转移所有权）。</summary>
        public void RequestPickup(int playerNetId) => SendServerRpc(nameof(CmdPickup), playerNetId);

        /// <summary>请求放下（客户端发起，服务器清除归属并恢复物理）。</summary>
        public void RequestDrop() => SendServerRpc(nameof(CmdDrop));

        [NetRpc]
        private void CmdPickup(int playerNetId)
        {
            if (IsHeld) return;
            if (!Net.Server.TryGetObject(playerNetId, out NetObject player)) return;
            if (player.OwnerClientId < 0) return;

            if (Vector3.Distance(player.transform.position, transform.position) > 4f) return;

            _holderNobId.Value = playerNetId;
            Net.Server.GiveOwnership(NetObj, player.OwnerClientId);
        }

        [NetRpc]
        private void CmdDrop()
        {
            if (!IsHeld) return;

            Vector3 dropPosition = transform.position;
            Vector3 dropVelocity = Vector3.zero;
            NetObject holder = FindHolder();
            if (holder != null)
            {
                dropPosition = holder.transform.position + holder.transform.forward * 1f + Vector3.up * 0.5f;
                dropVelocity = holder.transform.forward * 2f;
            }

            _holderNobId.Value = -1;
            Net.Server.RemoveOwnership(NetObj);

            transform.position = dropPosition;
            if (_rb != null)
            {
                _rb.velocity = dropVelocity;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        public abstract void OnAttack();

        public virtual void OnActivate() { }

        public virtual void OnDeactivate() { }

        public virtual void OnHitPlayer(NetworkIdentity victim) { }
    }
}
