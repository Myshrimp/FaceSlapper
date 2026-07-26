namespace FaceSlapper.Battle
{
    /// <summary>
    /// Buff 接口：由 BuffComponent 统一管理生命周期，
    /// 角色使用技能（Ability）时通过 OnUse 为角色提供增益。
    /// </summary>
    public interface IBuff
    {
        /// <summary>被添加到 BuffComponent 时调用。</summary>
        void OnAttach(BuffComponent owner);

        /// <summary>从 BuffComponent 移除时调用。</summary>
        void OnDetach();

        /// <summary>角色使用技能时触发（技能增益钩子）。</summary>
        void OnUse();

        /// <summary>是否仍然有效（失效后由 BuffComponent 自动移除）。</summary>
        bool IsValid();

        /// <summary>Buff 链上下文（调试信息）。</summary>
        string[] GetContext();
    }
}
