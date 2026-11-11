namespace SpotifyLyricsWin.Core;

/// <summary>
/// 标题栏小 logo 的解码宽度（v0.8.4，纯逻辑、无头可测）。
///
/// 背景：WPF 窗口的标题栏 ImageSource 若直接吃 256px 大帧、再由布局硬缩到
/// 约 20 DIP 显示，真机反馈「失真发糊」。给 BitmapImage 设 DecodePixelWidth
/// 后，解码器会挑最接近该宽度的帧并按档解码，缩放链短、字形边缘干净。
/// 基准取 20 DIP（WPF-UI 标题栏图标区的大致显示尺寸）× DPI 缩放；
/// 取不到/非法缩放（≤0、NaN、无穷）回退 32；结果钳在 [16, 96] 防极端值。
/// </summary>
public static class IconSizing
{
    /// <summary>标题栏图标的目标解码像素宽。dpiScale 为 1.0 = 100% 缩放。</summary>
    public static int TitleBarDecodePixelWidth(double dpiScale)
    {
        if (double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0) return 32;
        int w = (int)Math.Round(20 * dpiScale, MidpointRounding.AwayFromZero);
        return Math.Clamp(w, 16, 96);
    }
}
