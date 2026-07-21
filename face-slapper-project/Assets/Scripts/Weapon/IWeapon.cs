using FaceSlapper.Battle;

namespace FaceSlapper.Weapon
{
    /// <summary>武器接口。</summary>
    public interface IWeapon
    {
        /// <summary>发起一次攻击。</summary>
        void OnAttack();

        /// <summary>被拾取（激活）时调用。</summary>
        void OnActivate();

        /// <summary>被放下（停用）时调用。</summary>
        void OnDeactivate();

        /// <summary>确认命中玩家时调用（表现/数值钩子）。</summary>
        void OnHitPlayer(NetworkIdentity victim);
    }
}
