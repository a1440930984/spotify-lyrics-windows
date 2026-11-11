using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 全 App 统一的图标加载：托盘（System.Drawing.Icon）与各窗口（WPF ImageSource）
/// 共用这一份逻辑，不再各加载各的。启动时经 ReportToLog 自报来源/装机文件
/// 指纹/帧列表（v0.8.3 起保留）——装机 app.ico 若不是预期的那份（更新器没
/// 覆盖成功等），日志一眼可见。
///
/// 选帧策略沿革：v0.8.3 曾把 app.ico 抽成「只含 256 最大帧」的单帧字节，
/// 真机反馈大小对了但**失真**（大图硬缩发糊）；v0.8.4 改回原始多帧字节，
/// 但 WPF 侧仍走 BitmapImage——它只解一帧（实测拿到偏小帧），任务栏图标
/// 约以 16px 帧呈现、明显小于 v0.7 时代。v0.8.5 定案：两侧都显式走
/// 「多帧 + 按尺寸选帧」——
/// · WPF 侧（窗口图标/标题栏 logo）：用 BitmapFrame.Create 吃整份 ICO 流，
///   解出的帧集合留在解码器里，由 WPF 成像管线按每个显示面的渲染尺寸
///   自己选最接近的一帧（BitmapImage 路径只作回退，回退时仍按
///   Core.IconSizing 设 DecodePixelWidth）。
/// · 托盘侧：构造 Icon 时传**系统小图标尺寸**（SM_CXSMICON，已含 DPI
///   缩放，经 Core.TrayIconSizing 归一化），让 GDI 选最接近帧——既不拿
///   小帧被迫放大、也不拿 256 大帧硬缩。
/// 读不到文件/解析失败返回 null，调用方走原有回退（托盘回退运行时绘制，
/// 窗口回退不设图标）。
/// </summary>
public static class AppIcon
{
    private static bool _loaded;
    private static byte[]? _icoBytes;          // app.ico 原始多帧字节；不可用时为 null
    private static string _source = "运行时绘制";
    private static string _md5Prefix = "-";
    private static string _frameList = "-";
    private static ImageSource? _cachedImage;

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
    /// 托盘用：从原始多帧字节构造 Icon，构造尺寸传系统小图标尺寸
    /// （SM_CXSMICON，已含 DPI 缩放），让 GDI 选最接近的一帧；
    /// 不可用返回 null（调用方回退运行时绘制）。
    /// </summary>
    public static Icon? LoadDrawingIcon()
    {
        EnsureLoaded();
        if (_icoBytes == null) return null;
        try
        {
            int size = TrayIconSizing.Normalize(SmallIconPixelSize());
            using var ms = new MemoryStream(_icoBytes);
            return new Icon(ms, size, size);
        }
        catch { return null; }
    }

    /// <summary>系统小图标边长（设备像素，已含 DPI 缩放）；取不到返回 0，由 TrayIconSizing 回退。</summary>
    private static int SmallIconPixelSize()
    {
        try { return SystemInformation.SmallIconSize.Width; }
        catch { return 0; }
    }

    /// <summary>
    /// 窗口图标/标题栏小 logo 用：整份 ICO 流经 BitmapFrame.Create 多帧解码，
    /// 成像管线按渲染尺寸选帧；缓存+冻结，全窗共用。解码失败回退
    /// BitmapImage 路径（单帧，但按标题栏显示档设 DecodePixelWidth）。
    /// </summary>
    public static ImageSource? LoadImageSource()
    {
        EnsureLoaded();
        if (_icoBytes == null) return null;
        if (_cachedImage != null) return _cachedImage;
        try
        {
            using var ms = new MemoryStream(_icoBytes);
            var frame = BitmapFrame.Create(
                ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame.Freeze();
            _cachedImage = frame;
            return frame;
        }
        catch { /* 回退 BitmapImage 路径 */ }
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
