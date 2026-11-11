namespace SpotifyLyricsWin.Core;

/// <summary>
/// 悬浮窗位置计算（v0.8 抽出共用）：首次启动位置与托盘「重置歌词窗位置」用同一公式——
/// 主屏工作区水平居中、距顶部 120（与 FloatingWindow 首开逻辑、安卓 floatingY 惯例一致）。
/// 纯数值函数（不依赖 WPF），供无头测试。
/// </summary>
public static class WindowPlacement
{
    /// <summary>默认位置距工作区顶部的距离。</summary>
    public const double DefaultTopOffset = 120;

    /// <summary>默认（Left, Top)：工作区内水平居中 + 顶部偏移。</summary>
    public static (double Left, double Top) DefaultFloatingPosition(
        double workLeft, double workTop, double workWidth, double windowWidth) =>
        (workLeft + (workWidth - windowWidth) / 2.0, workTop + DefaultTopOffset);
}
