using System.Diagnostics;
using System.Management;

namespace SpotifyLyricsWin.Core;

/// <summary>
/// 把 Updater 以「与主程序脱钩」的方式拉起（v0.4 更新流程的最后一环）。
///
/// 教训来源（NOVA 在用户机器上实测）：App 直接 Process.Start 派生的子进程会随 App 退出
/// 被一并杀掉，小助手从未真正执行，更新静默失败。因此这里走 WMI Win32_Process.Create：
/// 新进程的父进程是 WMI 服务（WmiPrvSE），主程序退出后 Updater 照常存活换版。
/// 顺序：① System.Management 直调 WMI；② 失败则退到 powershell 隐藏窗口调
/// Invoke-CimMethod（同为 WMI 拉起，只是借 PowerShell 当壳）；③ 两者都失败返回 false，
/// 调用方应提示用户手动运行 Updater 兜底，而不是默默放弃更新。
/// </summary>
public static class UpdateLauncher
{
    /// <summary>Updater 命令行参数（路径都带引号，兼容空格）。</summary>
    public static string BuildArguments(int pid, string srcDir, string dstDir, string exeName,
        string fromVersion, string toVersion, string resultPath, string logPath) =>
        $"--pid {pid} --src \"{srcDir}\" --dst \"{dstDir}\" --exe \"{exeName}\" " +
        $"--from \"{fromVersion}\" --to \"{toVersion}\" --result \"{resultPath}\" --log \"{logPath}\"";

    /// <summary>脱钩启动 Updater。成功返回 (true, 用哪种方式, "")。</summary>
    public static (bool Ok, string How, string Error) LaunchDetached(string updaterExe, string arguments)
    {
        string commandLine = $"\"{updaterExe}\" {arguments}";
        if (TryViaWmi(commandLine, out string err1))
            return (true, "WMI", "");
        if (TryViaPowerShellCim(commandLine, out string err2))
            return (true, "PowerShell-CIM", "");
        return (false, "", $"WMI：{err1}；PowerShell：{err2}");
    }

    private static bool TryViaWmi(string commandLine, out string error)
    {
        error = "";
        try
        {
            using var procClass = new ManagementClass("Win32_Process");
            using var inParams = procClass.GetMethodParameters("Create");
            inParams["CommandLine"] = commandLine;
            using var outParams = procClass.InvokeMethod("Create", inParams, null);
            uint rc = Convert.ToUInt32(outParams["ReturnValue"]);
            if (rc == 0) return true;
            error = $"Win32_Process.Create 返回 {rc}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryViaPowerShellCim(string commandLine, out string error)
    {
        error = "";
        try
        {
            // 命令行放进 PowerShell 单引号字符串（单引号转义为两个单引号），避免多层引号互咬
            string psCmd = "$c = '" + commandLine.Replace("'", "''") + "'; " +
                           "$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create " +
                           "-Arguments @{ CommandLine = $c }; " +
                           "if ($r.ReturnValue -ne 0) { exit 1 }";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{psCmd}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) { error = "powershell 启动失败"; return false; }
            if (!p.WaitForExit(15000)) { error = "powershell 超时未返回"; return false; }
            if (p.ExitCode != 0) { error = $"powershell 退出码 {p.ExitCode}"; return false; }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
