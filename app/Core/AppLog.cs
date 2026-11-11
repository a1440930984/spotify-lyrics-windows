using System.IO;

namespace SpotifyLyricsWin.Core;

/// <summary>
/// 极简日志：追加写 %AppData%/SpotifyLyricsWin/log.txt（单行一条，带本地时间）。
/// 目前主要记录广告识别命中（时间/标题/歌手/命中规则），方便用户反馈误判。
/// 写失败静默；文件过大（>512KB）时截断重写，避免无限增长。
/// </summary>
public static class AppLog
{
    public static string LogPath => Path.Combine(Settings.AppDataDir, "log.txt");
    private static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Settings.AppDataDir);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 512 * 1024)
                    File.WriteAllText(LogPath, "");
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
        }
        catch { /* 日志写失败不影响主流程 */ }
    }
}
