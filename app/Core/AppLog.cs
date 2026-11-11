using System.IO;
using System.Text;

namespace SpotifyLyricsWin.Core;

/// <summary>
/// 诊断日志：追加写 %AppData%/SpotifyLyricsWin/log.txt（单行一条，带本地时间）。
/// v0.3 起记全量诊断：程序启动一行 + 每次 SMTC 切歌/会话变化一行
/// （标题/歌手/专辑/时长/封面/状态/判定），广告判定理由也在这里——
/// 所以文件必然存在，不会再出现「日志在哪、好像没有」。
/// 滚动上限约 200KB：超了保留后半段（从完整行开始），避免无限增长。
/// </summary>
public static class AppLog
{
    public static string LogPath => Path.Combine(Settings.AppDataDir, "log.txt");
    private static readonly object Lock = new();
    private const long MaxBytes = 200 * 1024;

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Settings.AppDataDir);
                TrimIfNeeded();
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch { /* 日志写失败不影响主流程 */ }
    }

    // ---------- v0.6：异常落盘 + 限频写 ----------

    private static readonly Dictionary<string, long> LastWriteByKey = new();

    /// <summary>
    /// 把异常完整落盘（类型 + 消息 + 堆栈）。全局未处理异常捕获（Program 三路）
    /// 与各关键路径的 catch 都走这里——v0.5 时手动选词崩溃退出、日志里一个字没有，
    /// 就是因为没有这条兜底。抽成独立方法便于无头测试断言落盘内容。
    /// </summary>
    public static void LogException(string where, Exception? ex)
    {
        if (ex == null) { Write($"[异常] {where}：（无异常对象）"); return; }
        Write($"[异常] {where}：{ex.GetType().FullName}: {ex.Message}");
        string? stack = ex.StackTrace;
        if (!string.IsNullOrWhiteSpace(stack))
        {
            foreach (string line in stack.Replace("\r\n", "\n").Split('\n'))
                if (line.Trim().Length > 0) Write($"    {line.Trim()}");
        }
        if (ex.InnerException != null)
            Write($"    内部异常：{ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
    }

    /// <summary>
    /// 限频版 Write：同一 key 在 minIntervalMs 内只写一次（同类重复行防刷屏，
    /// 如轮询里的重复状态、重试里的同因失败）。
    /// </summary>
    public static void WriteLimited(string key, string message, int minIntervalMs = 10_000)
    {
        lock (Lock)
        {
            long now = Environment.TickCount64;
            if (LastWriteByKey.TryGetValue(key, out long last) && now - last < minIntervalMs) return;
            LastWriteByKey[key] = now;
        }
        Write(message);
    }

    /// <summary>确保日志文件存在（程序启动时调用，哪怕还没写过任何一行）。</summary>
    public static void EnsureFile()
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Settings.AppDataDir);
                if (!File.Exists(LogPath)) File.WriteAllText(LogPath, "", Encoding.UTF8);
            }
        }
        catch { }
    }

    private static void TrimIfNeeded()
    {
        var fi = new FileInfo(LogPath);
        if (!fi.Exists || fi.Length <= MaxBytes) return;
        try
        {
            byte[] all = File.ReadAllBytes(LogPath);
            int keep = (int)(MaxBytes / 2);
            int start = all.Length - keep;
            // 对齐到行首：跳过不完整的一行
            while (start < all.Length && all[start] != (byte)'\n') start++;
            if (start < all.Length) start++;
            using var fs = new FileStream(LogPath, FileMode.Create, FileAccess.Write);
            fs.Write(all, start, all.Length - start);
        }
        catch { /* 截断失败就保持原样 */ }
    }
}
