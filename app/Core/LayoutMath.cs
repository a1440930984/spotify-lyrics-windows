namespace SpotifyLyricsWin.Core;

/// <summary>
/// 悬浮窗布局的纯计算（与 WPF 解耦，供渲染代码与无头测试共用同一份公式）。
/// v0.3 单行模式「水平+垂直双居中」的依据：
/// - 行高盒 = 字号 × 1.45（与 GradientLyricLine 的行高系数一致）；
/// - 单行窗口内容高 = 行高盒 + 上下内边距 6+6（对称）；
/// - 文本绘制原点 = (容器尺寸 − 内容尺寸) / 2，双向同理。
/// </summary>
public static class LayoutMath
{
    /// <summary>行高系数（与 GradientLyricLine.BuildWrapped 的行高估算一致）。</summary>
    public const double LineHeightFactor = 1.45;

    /// <summary>悬浮窗根内边距（照复刻规格 6dp，四边）。</summary>
    public const double RootPadding = 6.0;

    /// <summary>一行文本的行高盒高度。</summary>
    public static double LineBoxHeight(double fontSize) => fontSize * LineHeightFactor;

    /// <summary>把内容居中于容器时，内容左/上边缘的偏移。容器小于内容时为 0。</summary>
    public static double CenterOffset(double container, double content) =>
        Math.Max(0, (container - content) / 2.0);

    /// <summary>单行模式：窗口内容区高度 = 一行行高盒 + 上下对称内边距。</summary>
    public static double SingleLineContentHeight(double fontSize) =>
        LineBoxHeight(fontSize) + RootPadding * 2;

    /// <summary>
    /// 单行模式：文本行高盒在窗口内容区内的顶部偏移。
    /// 由公式可知恒等于 RootPadding，且 文本中心 == 窗口中心（垂直居中）。
    /// </summary>
    public static double SingleLineTextTop(double fontSize) =>
        CenterOffset(SingleLineContentHeight(fontSize), LineBoxHeight(fontSize));

    /// <summary>水平居中时文本左边缘 x（容器宽、文本实测宽）。</summary>
    public static double CenteredTextX(double containerWidth, double textWidth) =>
        CenterOffset(containerWidth, textWidth);

    // ---------- 悬浮窗按钮带安全区（v0.8） ----------
    // v0.7 按钮带是 Overlay 顶部居中，但锚点就贴在歌词块顶部——双行时胶囊（28 高按钮 +
    // 内边距 = 32）正好压住第一行字形上沿（Sun 真机截图实锤）。v0.8 起悬浮窗顶部预留
    // 一段透明留白专供按钮带：留白高 = 胶囊高 + 安全间隙；歌词栈从留白下方开始排，
    // 于是「按钮带底边 ≤ 第一行顶边」由构造保证，与单/双行、字号无关。

    /// <summary>按钮带胶囊总高（按钮 28 + 胶囊上下内边距 2×2）。</summary>
    public const double BandHeight = 32.0;

    /// <summary>按钮带底边与第一行歌词顶边之间的安全间隙。</summary>
    public const double BandGap = 4.0;

    /// <summary>悬浮窗顶部为按钮带预留的透明留白高。</summary>
    public const double BandReserveTop = BandHeight + BandGap;

    /// <summary>按钮带在留白区内贴顶放置时的底边 y（相对歌词内容区顶部）。</summary>
    public static double BandBottom() => BandHeight;

    /// <summary>第一行歌词的顶边 y（= 留白高，歌词栈从这里开始）。</summary>
    public static double FirstLineTop() => BandReserveTop;

    /// <summary>
    /// 译文字号（v0.7 改比例跟随，取代 v0.6 的「原文 −6」固定差）：
    /// 译文字号 = round（原文字号 × 0.62)，下限 11、上限不超过原文 −4；
    /// 原文很小时（原文 −4 &lt; 11）上下限冲突，以下限让位、保底 8（字号区间 12~60 内只在 12~14 触发）。
    /// 主流桌面歌词软件（BetterLyrics/桌面 QQ 音乐等）译文都是按原文比例缩小，不用固定差——
    /// 固定差在大字号时译文显得过大、小字号时又过小。
    /// </summary>
    public static double TranslationFontSize(double originalSize)
    {
        double v = Math.Round(originalSize * 0.62);
        double hi = Math.Max(8, originalSize - 4);
        double lo = Math.Min(11, hi);
        return Math.Clamp(v, lo, hi);
    }
}
