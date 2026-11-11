using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 全 App 统一的图标加载（v0.8.3）：托盘（System.Drawing.Icon）与各窗口
/// （WPF ImageSource）共用这一份逻辑，不再各加载各的。
///
/// 为什么要统一 + 定死选帧：app.ico 文件本身已逐字节恢复 v0.7（MD5 验过），
/// 但真机任务栏/托盘显示仍与 v0.7 时代不完全一样、偏小。剩余嫌疑之一是多帧 ICO
/// 的选帧由框架决定（new Icon(path) / BitmapImage 各挑各的，可能拿到小帧，
/// 任务栏便只居中不放大）。本类在首次加载时把 app.ico 读进来，经
/// Core.IcoFrames 抽出**最大帧**拼成单帧 ICO 字节，之后所有加载都吃这份字节，
/// 选帧从此是确定行为。读不到文件/解析失败时返回 null，由调用方走原有回退
/// （托盘回退运行时绘制，窗口回退不设图标）。
///
/// 顺带把装机文件的指纹（MD5 前 8 位）与帧列表缓存在这里，Program 启动时
/// 经 ReportToLog 写一行 [图标] 日志——Sun 下次回传日志即可实锤他机器上
/// app.ico 到底是不是 v0.7 那份（更新器没覆盖成功时指纹会对不上）。
/// </summary>
public static class AppIcon
{
    private static bool _loaded;
    private static byte[]? _largestFrameIco;   // 只含最大帧的 ICO 字节；不可用时为 null
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
            _largestFrameIco = IcoFrames.ExtractLargestFrameIco(bytes);
            if (_largestFrameIco == null) return;
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

    /// <summary>托盘用：从最大帧字节构造 Icon；不可用返回 null（调用方回退运行时绘制）。</summary>
    public static Icon? LoadDrawingIcon()
    {
        EnsureLoaded();
        if (_largestFrameIco == null) return null;
        try
        {
            using var ms = new MemoryStream(_largestFrameIco);
            return new Icon(ms);
        }
        catch { return null; }
    }

    /// <summary>窗口用：同一份最大帧字节解出的 ImageSource（缓存+冻结，全窗共用）。</summary>
    public static BitmapImage? LoadImageSource()
    {
        EnsureLoaded();
        if (_largestFrameIco == null) return null;
        if (_cachedImage != null) return _cachedImage;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(_largestFrameIco);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _cachedImage = bmp;
            return bmp;
        }
        catch { return null; }
    }
}
