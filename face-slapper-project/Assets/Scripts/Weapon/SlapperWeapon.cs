namespace FaceSlapper.Weapon
{
    /// <summary>
    /// 初始武器"大巴掌/拍子"：无专属逻辑，行为完全由组件组合决定——
    /// SphereHitDetector（球形范围检测）+ KnockbackEffect（轻击退）
    /// + WeaponEffectManager（效果应用与网络同步）。
    /// 参数在 Prefab 的组件上配置（见 FaceSlapperSetup.CreateWeaponPrefab）。
    /// </summary>
    public class SlapperWeapon : WeaponBase
    {
    }
}
