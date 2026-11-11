using System.IO;

namespace SpotifyLyricsWin.Core;

/// <summary>
/// 歌词缓存统计与清理（v0.8）：缓存目录里每首歌一份 &lt;hash&gt;.lrc（正文）+
/// 可选 &lt;hash&gt;.trans.lrc（译文）。统计口径：「首数」只数正文 .lrc（不含 .trans.lrc），
/// 字节数数全部缓存文件。清理只删这两种后缀，其他文件（万一有）不动；
/// 设置与纯音乐标记不在缓存目录里，天然不受影响。纯逻辑对目录操作，供无头测试。
/// </summary>
public static class CacheStats
{
    public const string MainSuffix = ".lrc";
    public const string TransSuffix = ".trans.lrc";

    public static bool IsCacheFile(string path) =>
        path.EndsWith(TransSuffix, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(MainSuffix, StringComparison.OrdinalIgnoreCase);

    public static bool IsMainFile(string path) =>
        path.EndsWith(MainSuffix, StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(TransSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>统计缓存目录（不存在视为空）。返回（首数， 总字节）。</summary>
    public static (int Songs, long Bytes) Measure(string dir)
    {
        if (!Directory.Exists(dir)) return (0, 0);
        int songs = 0;
        long bytes = 0;
        foreach (string f in Directory.EnumerateFiles(dir))
        {
            if (!IsCacheFile(f)) continue;
            try { bytes += new FileInfo(f).Length; } catch { /* 读不到的文件跳过字节 */ }
            if (IsMainFile(f)) songs++;
        }
        return (songs, bytes);
    }

    /// <summary>显示文案：「共 N 首 · 约 X MB」（KB 级显示 KB，0 首显示暂无缓存）。</summary>
    public static string Format(int songs, long bytes)
    {
        if (songs <= 0 && bytes <= 0) return "暂无缓存";
        string size = bytes >= 1024 * 1024
            ? $"约 {bytes / 1048576.0:0.0} MB"
            : $"约 {Math.Max(1, bytes / 1024.0):0} KB";
        return $"共 {songs} 首 · {size}";
    }

    /// <summary>清空歌词缓存：删全部 .lrc/.trans.lrc，返回删除的文件数。单个删失败不中断。</summary>
    public static int Clear(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        int removed = 0;
        foreach (string f in Directory.EnumerateFiles(dir))
        {
            if (!IsCacheFile(f)) continue;
            try { File.Delete(f); removed++; } catch { /* 占用中的跳过 */ }
        }
        return removed;
    }
}
