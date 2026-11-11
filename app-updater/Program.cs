using System.Diagnostics;
using System.Text.Json;

namespace SpotifyLyricsUpdater;

/// <summary>
/// SpotifyLyricsWin 独立更新小助手（v0.4）。
/// 由主程序经 WMI 脱钩拉起（父进程是 WMI 服务，主程序退出不会连带杀掉本进程）。
///
/// 参数：
///   --pid    主进程 pid（等它退出再换文件，最多等 20 秒）
///   --src    staging 目录（新版文件已解压在此）
///   --dst    主程序安装目录（覆盖目标）
///   --exe    主程序 exe 名（换完后重新启动它）
///   --from / --to  版本号（写进结果文件给主程序下次启动汇报）
///   --result 结果 json 路径（%AppData%/SpotifyLyricsWin/update-result.json）
///   --log    日志路径（%TEMP%/SpotifyLyricsWin-update/updater.log）
///
/// 结果 json 格式与主程序 Core/UpdateService.UpdateResult 对齐：
///   { "from": "...", "to": "...", "ok": true/false, "error": "...", "time": "..." }
/// </summary>
public static class Program
{
    private static string _logPath = "";

    public static int Main(string[] args)
    {
        var opt = ParseArgs(args);
        _logPath = opt.GetValueOrDefault("--log", "");
        string resultPath = opt.GetValueOrDefault("--result", "");
        string from = opt.GetValueOrDefault("--from", "");
        string to = opt.GetValueOrDefault("--to", "");

        // 第一件事写心跳：主程序/用户能据此确认助手真的被拉起来了
        //（NOVA 的教训：助手没跑起来时最难查，先留证据）
        Log("==== Updater 启动 ====");
        Log($"参数：pid={opt.GetValueOrDefault("--pid", "?")} src={opt.GetValueOrDefault("--src", "")} dst={opt.GetValueOrDefault("--dst", "")}");

        bool ok = false;
        string error = "";
        try
        {
            ok = Run(opt, out error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log($"未预期异常：{ex}");
        }

        WriteResult(resultPath, from, to, ok, ok ? "" : error);
        Log(ok ? "==== 更新完成 ====" : $"==== 更新失败：{error} ====");
        return ok ? 0 : 1;
    }

    private static bool Run(Dictionary<string, string> opt, out string error)
    {
        error = "";
        string src = opt.GetValueOrDefault("--src", "");
        string dst = opt.GetValueOrDefault("--dst", "");
        string exe = opt.GetValueOrDefault("--exe", "SpotifyLyricsWin.exe");
        if (!int.TryParse(opt.GetValueOrDefault("--pid", ""), out int pid)) pid = -1;
        if (string.IsNullOrWhiteSpace(src) || !Directory.Exists(src))
        { error = $"staging 目录不存在：{src}"; Log(error); return false; }
        if (string.IsNullOrWhiteSpace(dst))
        { error = "未给安装目录 --dst"; Log(error); return false; }
        Directory.CreateDirectory(dst);

        WaitForMainExit(pid);

        // 逐个覆盖；单个文件被锁（杀毒扫描等）时重试 5 次、间隔 500ms。
        // 说明：本助手的既有逻辑本来就是对**全部文件**无条件 File.Copy(overwrite:true)，
        // 不存在按时间戳/大小跳过的判断。
        // v0.8.3 起再对 app.ico / app.png 显式加一道：先删目标再复制 + 复制后校验
        // 大小一致并记日志——图标文件曾被怀疑「换版没真正覆盖」，从此有据可查。
        var files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).ToList();
        Log($"开始覆盖 {files.Count} 个文件 → {dst}");
        var failures = new List<string>();
        foreach (string file in files)
        {
            string rel = Path.GetRelativePath(src, file);
            string target = Path.Combine(dst, rel);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (IsIconFile(rel)) CopyIconForced(file, target, rel);
                else CopyWithRetry(file, target);
            }
            catch (Exception ex)
            {
                failures.Add($"{rel}：{ex.Message}");
                Log($"覆盖失败 {rel}：{ex.Message}");
            }
        }
        if (failures.Count > 0)
        {
            error = $"{failures.Count} 个文件没能覆盖：{string.Join("；", failures)}";
            TryRelaunch(dst, exe); // 旧版大体还在，先把程序拉回来别让用户没得用
            return false;
        }
        Log("全部文件覆盖完成");

        TryRelaunch(dst, exe);

        // 清理 staging 与下载的 zip（尽力而为，失败不影响结果）
        try { Directory.Delete(src, recursive: true); } catch { }
        try
        {
            string zip = Path.Combine(Path.GetDirectoryName(src)!, "update.zip");
            if (File.Exists(zip)) File.Delete(zip);
        }
        catch { }
        return true;
    }

    private static void WaitForMainExit(int pid)
    {
        if (pid <= 0) { Log("未给主进程 pid，跳过等待"); return; }
        Process? p = null;
        try { p = Process.GetProcessById(pid); } catch { /* 已经不在了 */ }
        if (p == null || p.HasExited) { Log($"主进程 {pid} 已退出"); return; }
        Log($"等主进程 {pid} 退出（最多 20 秒）…");
        try
        {
            if (p.WaitForExit(20000)) { Log("主进程已退出"); return; }
        }
        catch { }
        // 超时也继续：先尝试温和结束（发关闭消息），不行再强制结束
        Log("主进程 20 秒未退出，尝试温和结束");
        try { p.CloseMainWindow(); } catch { }
        try { if (p.WaitForExit(3000)) { Log("主进程已退出（温和结束）"); return; } } catch { }
        try { p.Kill(); Log("已强制结束主进程"); } catch (Exception ex) { Log($"强制结束失败：{ex.Message}，继续尝试覆盖"); }
    }

    private static void CopyWithRetry(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { File.Copy(from, to, overwrite: true); return; }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 5)
            {
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>图标文件判定（只看文件名，与所在目录无关）。</summary>
    private static bool IsIconFile(string rel)
    {
        string name = Path.GetFileName(rel);
        return name.Equals("app.ico", StringComparison.OrdinalIgnoreCase)
            || name.Equals("app.png", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// v0.8.3 图标文件强制覆盖：先删目标（杜绝任何「目标已存在就不动」的余地）→
    /// 复制 → 校验目标大小与源一致；不一致或 IO 异常重试 5 次，最终失败抛
    /// IOException 交上层计入 failures。每次成功都记一行日志（源/目标字节数）。
    /// </summary>
    private static void CopyIconForced(string from, string to, string rel)
    {
        long srcSize = new FileInfo(from).Length;
        Exception? lastError = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                if (File.Exists(to)) File.Delete(to);
                File.Copy(from, to);
                long dstSize = new FileInfo(to).Length;
                if (dstSize == srcSize)
                {
                    Log($"图标文件强制覆盖完成：{rel}（{srcSize} 字节，目标校验一致）");
                    return;
                }
                lastError = new IOException($"大小不一致：源 {srcSize} 字节，目标 {dstSize} 字节");
                Log($"图标文件覆盖后校验不一致（第 {attempt} 次）：{rel} {lastError.Message}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                lastError = ex;
                Log($"图标文件覆盖失败（第 {attempt} 次）：{rel}：{ex.Message}");
            }
            Thread.Sleep(500);
        }
        throw new IOException($"图标文件 {rel} 强制覆盖 5 次仍失败：{lastError?.Message}");
    }

    private static void TryRelaunch(string dst, string exe)
    {
        try
        {
            string path = Path.Combine(dst, exe);
            if (!File.Exists(path)) { Log($"重启失败：找不到 {path}"); return; }
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = dst,
                UseShellExecute = true
            });
            Log($"已重新启动主程序 {exe}");
        }
        catch (Exception ex) { Log($"重启主程序失败：{ex.Message}（可手动双击 exe）"); }
    }

    private static void WriteResult(string resultPath, string from, string to, bool ok, string error)
    {
        if (string.IsNullOrWhiteSpace(resultPath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            var payload = new Dictionary<string, object>
            {
                ["from"] = from,
                ["to"] = to,
                ["ok"] = ok,
                ["error"] = error,
                ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            File.WriteAllText(resultPath, JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { WriteIndented = true }));
            Log($"结果已写入 {resultPath}");
        }
        catch (Exception ex) { Log($"写结果文件失败：{ex.Message}"); }
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i + 1 < args.Length; i += 2)
            if (args[i].StartsWith("--")) d[args[i]] = args[i + 1];
        return d;
    }

    private static void Log(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        try
        {
            if (!string.IsNullOrWhiteSpace(_logPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch { /* 日志写不了也不能崩 */ }
    }
}
