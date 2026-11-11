namespace SpotifyLyricsWin.Core;

/// <summary>一次下载进度快照（UpdateService.DownloadAssetAsync 的字节级回调载荷）。</summary>
public readonly record struct DownloadProgressInfo(long DownloadedBytes, long TotalBytes);

/// <summary>
/// 下载进度文案（v0.8.2，纯函数供无头测试）：
/// 总字节已知 → 「正在下载更新… 3.2 MB / 9.4 MB · 34%」；
/// 未知（TotalBytes ≤ 0）→ 退化为「正在下载更新… 已下载 3.2 MB」。
/// </summary>
public static class DownloadProgressText
{
    /// <summary>字节数 → 人话（B / KB / MB，一位小数；负数表示未知，给「?」占位不直接外露）。</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "?";
        if (bytes >= 1024 * 1024) return $"{bytes / 1048576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    /// <summary>百分比（0~100）；总字节未知时返回 -1（UI 侧据此切不定量进度条）。</summary>
    public static double Percent(long downloaded, long total)
        => total > 0 ? Math.Min(100.0, Math.Max(0.0, downloaded * 100.0 / total)) : -1;

    /// <summary>设置窗更新区的进度行文案（与 SetUpdateStatus 的静态文案口径一致）。</summary>
    public static string Format(long downloaded, long total)
    {
        double pct = Percent(downloaded, total);
        return pct >= 0
            ? $"正在下载更新… {FormatBytes(downloaded)} / {FormatBytes(total)} · {pct:F0}%"
            : $"正在下载更新… 已下载 {FormatBytes(downloaded)}";
    }
}
