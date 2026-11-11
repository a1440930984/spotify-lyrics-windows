namespace SpotifyLyricsWin.Core;

/// <summary>
/// 下拉框滚轮守卫的决策与滚动量计算（v0.7 重做，纯逻辑供无头测试）。
/// v0.6 的做法是未展开时直接 e.Handled=true ——值是不改了，但滚轮事件也被吞掉，
/// 设置页滚动时鼠标一经过字体/主题框页面就「卡住」不动（用户真机反馈）。
/// v0.7：未展开 → 不改选，但把滚动量**转发给祖先 ScrollViewer**（页面照常滚）；
/// 展开 → 不拦，滚轮归下拉列表自己。
/// </summary>
public static class WheelForward
{
    /// <summary>决策：下拉已展开时放行给列表本身；未展开时转发给页面滚动容器。</summary>
    public enum Action
    {
        /// <summary>不处理（滚轮归下拉列表）。</summary>
        LetComboHandle,
        /// <summary>转发给祖先 ScrollViewer 后标记 Handled（防 ComboBox 改选）。</summary>
        ForwardToScrollViewer
    }

    public static Action Decide(bool dropDownOpen) =>
        dropDownOpen ? Action.LetComboHandle : Action.ForwardToScrollViewer;

    /// <summary>一格滚轮（|Delta|=120）对应的滚动像素数，与常见桌面应用一致。</summary>
    public const double PixelsPerNotch = 48.0;

    /// <summary>
    /// 转发后的新垂直偏移：Delta 为正（向上滚）→ 偏移减小；结果钳制在 [0, maxOffset]。
    /// </summary>
    public static double NewOffset(double currentOffset, int delta, double maxOffset)
    {
        double next = currentOffset - delta * (PixelsPerNotch / 120.0);
        return Math.Clamp(next, 0, Math.Max(0, maxOffset));
    }
}
