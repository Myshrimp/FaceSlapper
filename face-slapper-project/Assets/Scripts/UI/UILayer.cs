namespace FaceSlapper.UI
{
    /// <summary>
    /// UI 层级。枚举值即该层子 Canvas 的 sortingOrder，
    /// 层与层之间绝不穿插；同层内部按打开先后排序（后打开的在上层）。
    /// 新增层级时注意给相邻层预留 sortingOrder 间隔。
    /// </summary>
    public enum UILayer
    {
        /// <summary>最底层：HUD、常驻背景。</summary>
        Background = 0,

        /// <summary>常规功能面板。</summary>
        Normal = 100,

        /// <summary>弹窗、确认框。</summary>
        Popup = 200,

        /// <summary>最顶层：全局提示、引导、加载遮罩。</summary>
        Top = 300,
    }
}
