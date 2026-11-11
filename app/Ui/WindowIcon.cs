using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 窗口图标加载：优先用输出目录里的 app.ico（与 exe 图标同一份），
/// 失败则不设（回退系统默认），绝不抛异常。
/// v0.8.3：实际加载改走 AppIcon（与托盘共用同一逻辑），本类只保留
/// 「套到窗口上」的薄封装。v0.8.4：AppIcon 已改回原始多帧策略
/// （窗口图标系统选帧、标题栏 ImageSource 按显示档 DecodePixelWidth）。
/// </summary>
public static class WindowIcon
{
    public static void Apply(Window window)
    {
        try
        {
            var img = AppIcon.LoadImageSource();
            if (img != null) window.Icon = img;
        }
        catch { /* 图标加载失败不影响功能 */ }
    }

    /// <summary>标题栏小 logo 用：同一份 app.ico 的 ImageSource（加载失败返回 null）。</summary>
    public static System.Windows.Media.ImageSource? LoadImageSource()
    {
        try { return AppIcon.LoadImageSource(); }
        catch { return null; }
    }
}
