using FaceSlapper.Networking;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 眩晕 Buff：持续时间内禁止移动/跳跃/攻击（输入层在 Movement/AbilityComponent 中各自检查）。
    /// 由 Movement 在"重击飞行中撞到障碍物"时添加；重复获得刷新持续时间。
    /// 用法：GetComponent&lt;BuffComponent&gt;().AddBuff(new StunBuff(1.6f));
    /// </summary>
    public class StunBuff : BuffBase
    {
        private readonly float _duration;

        public StunBuff(float duration = 1.6f)
        {
            _duration = duration;
            buffName = "Stun";
        }

        public override float Duration => _duration;

        public override void OnDetach()
        {
            // 先恢复移动，再走基类清理（_owner 在基类 OnDetach 中置空）。
            if (_owner != null)
            {
                // 眩晕进入由 Movement 撞墙时 Fire(StunTrigger) 驱动，
                // 这里到期后 Fire(StunEndTrigger) 让状态机回到正常状态。
                Movement movement = _owner.GetComponent<Movement>();
                if (movement != null) movement.EndStun();

                // 眩晕解除：Owner 端上报服务器清除 NetVar，全端关闭眩晕表现。
                NetworkIdentity identity = _owner.GetComponent<NetworkIdentity>();
                NetObject netObject = _owner.GetComponent<NetObject>();
                if (identity != null && netObject != null && netObject.IsOwner)
                    identity.ReportStunned(false);
            }
            base.OnDetach();
        }
    }
}
