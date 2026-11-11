namespace SpotifyLyricsWin.Core;

/// <summary>
/// 设置窗布局常量与卡序（v0.8.1，纯逻辑供无头测试断言）：
/// 窗口由 500×760 竖长条改近方形 920×660，八张卡双列排布——
/// 左列（外观主线）：外观与逐字 → 颜色 → 背景与时间 → 译文与翻译
/// （Sun 定案：译文卡在背景与时间下面，同列自上而下即阅读序）；
/// 右列（功能杂项）：字体与歌词源 → 纯音乐标记 → 诊断与缓存 → 更新与启动。
/// 窗口宽度 &lt; TwoColumnMinWidth 时回落单列：左列四卡 + 右列四卡顺排，
/// 保持与双列相同的阅读顺序，窄窗下不挤坏控件。
/// </summary>
public static class SettingsLayout
{
    public const double DefaultWidth = 920;
    public const double DefaultHeight = 660;
    /// <summary>双列下限：宽于此用双列（含等号）。两列各约 (760-28-10)/2 ≈ 361，与旧单列 440 宽相比略窄但卡内控件可承受。</summary>
    public const double TwoColumnMinWidth = 760;
    /// <summary>单列回落时的窗口最小宽（与旧版 440 同量级，卡内最宽控件组可放下）。</summary>
    public const double MinWidth = 460;
    public const double MinHeight = 480;
    /// <summary>双列时两列之间的间距（与卡片纵向间距 10 一致）。</summary>
    public const double ColumnGap = 10;

    public const string Look = "look";
    public const string Color = "color";
    public const string Time = "time";
    public const string Trans = "trans";
    public const string Font = "font";
    public const string Marks = "marks";
    public const string Log = "log";
    public const string Update = "update";

    /// <summary>左列自上而下（阅读序）。</summary>
    public static readonly string[] LeftColumn = { Look, Color, Time, Trans };
    /// <summary>右列自上而下。</summary>
    public static readonly string[] RightColumn = { Font, Marks, Log, Update };
    /// <summary>单列回落顺序 = 左列序 + 右列序。</summary>
    public static readonly string[] SingleColumn =
        { Look, Color, Time, Trans, Font, Marks, Log, Update };

    /// <summary>给定窗口宽度是否使用双列（纯函数）。</summary>
    public static bool UseTwoColumns(double windowWidth) => windowWidth >= TwoColumnMinWidth;
}
