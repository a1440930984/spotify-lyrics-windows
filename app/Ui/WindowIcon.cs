using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 窗口图标加载：优先用输出目录里的 app.ico（与 exe 图标同一份），
/// 失败则不设（回退系统默认），绝不抛异常。
/// </summary>
public static class WindowIcon
{
    private static BitmapImage? _cached;

    public static void Apply(Window window)
    {
        try
        {
            _cached ??= Load();
            if (_cached != null) window.Icon = _cached;
        }
        catch { /* 图标加载失败不影响功能 */ }
    }

    /// <summary>标题栏小 logo 用：同一份 app.ico 的 ImageSource（加载失败返回 null）。</summary>
    public static System.Windows.Media.ImageSource? LoadImageSource()
    {
        try
        {
            _cached ??= Load();
            return _cached;
        }
        catch { return null; }
    }

    private static BitmapImage? Load()
    {
        string ico = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (!File.Exists(ico)) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(ico);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
