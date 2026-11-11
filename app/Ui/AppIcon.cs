using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 全 App 统一的图标加载：托盘（System.Drawing.Icon）与各窗口（WPF ImageSource）
/// 共用这一份逻辑，不再各加载各的。启动时经 ReportToLog 自报来源/装机文件
/// 指纹/帧列表（v0.8.3 起保留）——装机 app.ico 若不是预期的那份（更新器没
/// 覆盖成功等），日志一眼可见。
///
/// 选帧策略（v0.8.4 改回多帧）：v0.8.3 曾把 app.ico 抽成「只含 256 最大帧」
/// 的单帧字节，想把选帧定死；真机反馈大小对了但**失真**——托盘/任务栏只有
/// 十几到几十像素，大图硬缩必然发糊。本版改回 v0.7 时代的原始机制：托盘与
/// 窗口图标一律吃 app.ico 的**原始多帧字节**（new Icon(stream) 保留全部帧，
/// 由系统按每个显示面需要的尺寸自己选帧）；唯一例外是 WPF 标题栏的
/// ImageSource——那是位图缩放链，给 BitmapImage 设 DecodePixelWidth 为
/// 标题栏显示档（约 20 DIP × DPI 缩放，见 Core.IconSizing），让解码器
/// 按档挑帧解码，不再拿 256 帧硬缩。读不到文件/解析失败返回 null，
/// 调用方走原有回退（托盘回退运行时绘制，窗口回退不设图标）。
/// </summary>
public static class AppIcon
{
    private static bool _loaded;
    private static byte[]? _icoBytes;          // app.ico 原始多帧字节；不可用时为 null
    private static string _source = "运行时绘制";
    private static string _md5Prefix = "-";
    private static string _frameList = "-";
    private static BitmapImage? _cachedImage;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            string ico = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (!File.Exists(ico)) return;
            byte[] bytes = File.ReadAllBytes(ico);
            var frames = IcoFrames.Parse(bytes);
            if (frames.Count == 0) return;
            _icoBytes = bytes;
            _source = "file";
            _md5Prefix = IcoFrames.Md5Prefix8(bytes);
            _frameList = IcoFrames.FrameListText(frames);
        }
        catch { /* 加载失败不影响功能，各调用方走回退 */ }
    }

    /// <summary>启动自报：把图标来源/文件指纹/帧列表写进日志（只写一行）。</summary>
    public static void ReportToLog()
    {
        EnsureLoaded();
        AppLog.Write($"[图标] 来源={_source} 文件MD5={_md5Prefix} 帧={_frameList}");
    }

    /// <summary>
    /// 托盘/窗口用：从原始多帧字节构造 Icon（保留全部帧，系统按显示面选帧）；
    /// 不可用返回 null（调用方回退运行时绘制）。
    /// </summary>
    public static Icon? LoadDrawingIcon()
    {
        EnsureLoaded();
        if (_icoBytes == null) return null;
        try
        {
            using var ms = new MemoryStream(_icoBytes);
            return new Icon(ms);
        }
        catch { return null; }
    }

    /// <summary>
    /// 标题栏小 logo 用：原始多帧字节解出的 ImageSource，按标题栏显示档设
    /// DecodePixelWidth（见类注释）；缓存+冻结，全窗共用。
    /// </summary>
    public static BitmapImage? LoadImageSource()
    {
        EnsureLoaded();
        if (_icoBytes == null) return null;
        if (_cachedImage != null) return _cachedImage;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(_icoBytes);
            bmp.DecodePixelWidth = IconSizing.TitleBarDecodePixelWidth(CurrentDpiScale());
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _cachedImage = bmp;
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>系统 DPI 缩放（1.0 = 100%）；取不到返回 0，由 IconSizing 回退到 32。</summary>
    private static double CurrentDpiScale()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch { return 0; }
    }
}
