namespace SpotifyLyricsWin.Core;

/// <summary>
/// 透明自检日志行格式化（v0.8.3，纯函数、无头可测）。
/// 探针（Ui.TransparencyProbe）用 CopyFromScreen 采到悬浮窗在**屏幕上实际呈现**
/// 的像素后，拼成这一行落日志：窗内四角（左上/右上/左下/右下）+ 窗外参考点，
/// 再附当时设置应有的背景。诊断属性层（TransparencyDiag）与屏幕实测层两份证据
/// 对照，涂色层到底在哪一层即可定案。
/// </summary>
public static class TransparencyProbeText
{
    /// <summary>RGB → #RRGGBB（大写十六进制）。</summary>
    public static string Hex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <param name="cornerHexes">窗内四角采样（顺序：左上、右上、左下、右下），每项 #RRGGBB。</param>
    /// <param name="outsideHex">窗外参考点采样，#RRGGBB。</param>
    /// <param name="expectedBg">设置应有的背景描述（如 Transparent / 半透明黑）。</param>
    /// <param name="alphaPercent">背景透明度设置值（BgAlphaPercent）。</param>
    public static string Format(IReadOnlyList<string> cornerHexes, string outsideHex,
        string expectedBg, int alphaPercent)
        => $"[透明自检] 窗内四角={string.Join(",", cornerHexes)} 窗外={outsideHex} 应为背景={expectedBg}/alpha={alphaPercent}";
}
