using FaceSlapper.Battle;
using FaceSlapper.Core;
using FaceSlapper.Networking;
using UnityEngine;

namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 武器效果管理器（挂在武器上，与 WeaponBase 并列）：
    /// - 收集本武器所有 WeaponEffect 组件，命中时逐目标、逐效果应用——
    ///   武器的各种效果就是各种 Effect 的组合，武器本身不感知具体效果；
    /// - 独立向玩家同步信息：自带 击飞上报 → 服务器校验 → 受害者 Owner 执行
    ///   的 RPC 链路（不借道持有者的 NetworkIdentity），
    ///   命中表现（音效/粒子）由本组件直接广播给武器的所有观察者。
    /// </summary>
    public class WeaponEffectManager : NetBehaviour
    {
        private WeaponEffect[] _effects;
        private WeaponBase _weapon;

        protected override void Awake()
        {
            base.Awake();
            _effects = GetComponents<WeaponEffect>();
            _weapon = GetComponent<WeaponBase>();
            if (_effects.Length == 0)
                Debug.LogWarning($"[EffectManager] {name} 上没有任何 WeaponEffect 组件，命中将无效果。");
        }

        /// <summary>
        /// 对一批命中目标应用全部效果（仅攻击者 Owner 端调用）。
        /// 方向在这里统一计算（持有者 → 目标的水平向量），效果不各自重算。
        /// </summary>
        public void ApplyHits(NetObject holder, NetworkIdentity attacker, HitResult[] hits, int hitCount, float power)
        {
            if (_effects == null || _effects.Length == 0) return;

            Vector3 holderPos = holder.transform.position;
            for (int i = 0; i < hitCount; i++)
            {
                Vector3 dir = hits[i].Target.transform.position - holderPos;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) dir = holder.transform.forward;
                dir.Normalize();

                var ctx = new EffectContext
                {
                    Manager = this,
                    Holder = holder,
                    Attacker = attacker,
                    Victim = hits[i].Target,
                    Direction = dir,
                    Power = power,
                    HitPoint = hits[i].Point,
                };

                for (int e = 0; e < _effects.Length; e++)
                    _effects[e].Apply(ctx);
            }
        }

        // ---------------- 网络同步（独立上报链路） ----------------

        /// <summary>上报一次轻击退（Owner 发起）。服务器做距离校验后转发受害者 Owner 执行。</summary>
        public void ReportHit(int victimNetId, Vector3 direction, float force, float maxRange)
        {
            SendServerRpc(nameof(CmdHit), victimNetId, direction, force, maxRange);
        }

        [NetRpc]
        private void CmdHit(int victimNetId, Vector3 direction, float force, float maxRange)
        {
            // 仅服务器执行（ServerRpc 语义）。
            if (!ServerValidateVictim(victimNetId, maxRange, out NetworkIdentity victimIdentity, out NetObject victim))
                return;

            victimIdentity.ServerForwardKnockback(direction, force);
            // 广播命中表现（音效/粒子）给武器的所有观察者。
            SendObserversRpc(nameof(RpcHitFeedback), victim.transform.position, direction, force);
        }

        /// <summary>上报一次重击击飞（Owner 发起）：带竖直分量、滞空与撞墙眩晕参数。</summary>
        public void ReportLaunch(int victimNetId, Vector3 direction, float force, float upRatio, float airTime, float maxRange, float stunDuration)
        {
            SendServerRpc(nameof(CmdLaunch), victimNetId, direction, force, upRatio, airTime, maxRange, stunDuration);
        }

        [NetRpc]
        private void CmdLaunch(int victimNetId, Vector3 direction, float force, float upRatio, float airTime, float maxRange, float stunDuration)
        {
            // 仅服务器执行（ServerRpc 语义）。
            if (!ServerValidateVictim(victimNetId, maxRange, out NetworkIdentity victimIdentity, out NetObject victim))
                return;

            victimIdentity.ServerForwardLaunch(direction, force, upRatio, airTime, stunDuration);
            // 广播命中表现（音效/粒子）给武器的所有观察者。
            SendObserversRpc(nameof(RpcHitFeedback), victim.transform.position, direction, force);
        }

        /// <summary>命中表现（全端）：发布 Fx 事件，由 HitFeedbackComponent 播音效/粒子。</summary>
        [NetRpc]
        private void RpcHitFeedback(Vector3 position, Vector3 direction, float force)
        {
            EventBus.Publish(new PlayerHitFxEvent { Position = position, Direction = direction, Force = force });
        }

        /// <summary>
        /// 服务器端命中校验：目标存在、不是持有者本人、距离在宽松范围内
        /// （服务器端武器位置≈持有者手部位置，各端位置由同步而来，+2m 容忍同步延迟）。
        /// </summary>
        private bool ServerValidateVictim(int victimNetId, float maxRange, out NetworkIdentity victimIdentity, out NetObject victim)
        {
            victimIdentity = null;
            if (!Net.Server.TryGetObject(victimNetId, out victim)) return false;

            // 目标不能是武器当前持有者。
            if (_weapon != null && victim.NetId == _weapon.HolderNobId) return false;

            float dist = Vector3.Distance(victim.transform.position, transform.position);
            if (dist > maxRange + 2f) return false;

            victimIdentity = victim.GetComponent<NetworkIdentity>();
            return victimIdentity != null && victim.OwnerClientId >= 0;
        }
    }
}
