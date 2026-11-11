namespace SpotifyLyricsWin.Core;

/// <summary>
/// 悬浮窗位置计算（v0.8 抽出共用）：首次启动位置与托盘「重置歌词窗位置」用同一公式。
/// v0.8.1 定案（Sun）：默认落点改**屏幕底部居中**——主屏工作区水平居中、
/// 贴工作区底部上方（距任务栏留 BottomMargin 边距），不再用 v0.8 前的「距顶 120」。
/// 已存位置记忆的用户不受影响，本公式只管首开与复位。
/// 纯数值函数（不依赖 WPF），供无头测试。
/// </summary>
public static class WindowPlacement
{
    /// <summary>默认位置距工作区底部的边距（避开任务栏上方贴边，留呼吸感）。</summary>
    public const double BottomMargin = 48;

    /// <summary>默认（Left, Top)：工作区内水平居中 + 底部对齐（底边距 BottomMargin）。</summary>
    public static (double Left, double Top) DefaultFloatingPosition(
        double workLeft, double workTop, double workWidth, double workHeight,
        double windowWidth, double windowHeight) =>
        (workLeft + (workWidth - windowWidth) / 2.0,
         workTop + workHeight - windowHeight - BottomMargin);
}
