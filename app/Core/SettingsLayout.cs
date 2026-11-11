namespace SpotifyLyricsWin.Core;

/// <summary>
/// 设置窗布局常量与卡序（v0.8.2 网格化，纯逻辑供无头测试断言）：
/// 八张卡排成 **2 列 × 4 行** 的方格，同行左右等高（行高取同行较高者、卡片拉伸填满），
/// 重要卡靠前——行序定案：
///   行1 外观与逐字 | 译文与翻译   （天天要看的显示与译文）
///   行2 颜色       | 字体与歌词源
///   行3 背景与时间 | 纯音乐标记
///   行4 诊断与缓存 | 更新与启动   （低频维护类垫底）
/// 默认尺寸 980×730（近方形，1080p 工作区能全放下）；窗口可缩放，尺寸不足时
/// 外层 ScrollViewer 滚动兜底；窗宽 &lt; TwoColumnMinWidth 时回落单列，
/// 单列顺序 = 网格逐行先左后右（与网格阅读序一致）。
/// v0.8.1 的左右双列堆叠（各列内部不等高、两列总高还不一致）就此退役。
/// </summary>
public static class SettingsLayout
{
    public const double DefaultWidth = 980;
    public const double DefaultHeight = 730;
    /// <summary>双列（网格）下限：宽于此用 2×4 网格（含等号）。</summary>
    public const double TwoColumnMinWidth = 760;
    /// <summary>单列回落时的窗口最小宽（与旧版 440 同量级，卡内最宽控件组可放下）。</summary>
    public const double MinWidth = 460;
    public const double MinHeight = 480;
    /// <summary>同行左右两卡之间的间距。</summary>
    public const double ColumnGap = 10;
    /// <summary>相邻两行之间的间距（与卡片原纵向间距 10 一致）。</summary>
    public const double RowGap = 10;

    public const string Look = "look";
    public const string Trans = "trans";
    public const string Color = "color";
    public const string Font = "font";
    public const string Time = "time";
    public const string Marks = "marks";
    public const string Log = "log";
    public const string Update = "update";

    /// <summary>网格行（双列模式）：自上而下四行，每行 [左卡, 右卡]。</summary>
    public static readonly string[][] GridRows =
    {
        new[] { Look, Trans },
        new[] { Color, Font },
        new[] { Time, Marks },
        new[] { Log, Update },
    };

    /// <summary>单列回落顺序 = 网格逐行先左后右展开。</summary>
    public static readonly string[] SingleColumn =
        { Look, Trans, Color, Font, Time, Marks, Log, Update };

    /// <summary>给定窗口宽度是否使用双列网格（纯函数）。</summary>
    public static bool UseTwoColumns(double windowWidth) => windowWidth >= TwoColumnMinWidth;
}
