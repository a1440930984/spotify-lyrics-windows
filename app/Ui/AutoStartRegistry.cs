using System.IO;
using Microsoft.Win32;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 开机自动启动的注册表读写（v0.8）：HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// 当前用户键、无需管理员权限。所有操作吞异常返回成败——读不到按未开启处理、
/// 写失败由设置窗回滚开关显示并记日志。纯逻辑（值名/期望值/对齐判定）在 Core.AutoStart。
/// </summary>
public static class AutoStartRegistry
{
    /// <summary>当前 exe 的完整路径（自启要指向的本体）。</summary>
    public static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SpotifyLyricsWin.exe");

    /// <summary>注册表里现在的值（没有/读不到为 null）。</summary>
    public static string? ReadValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStart.RunKeyPath, writable: false);
            return key?.GetValue(AutoStart.ValueName) as string;
        }
        catch { return null; }
    }

    /// <summary>实际是否已开启（值存在且与当前 exe 路径对齐）。</summary>
    public static bool IsEnabled() => AutoStart.IsAligned(ReadValue(), ExePath);

    /// <summary>开启（写入期望值）/关闭（删除值）。返回是否成功。</summary>
    public static bool SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStart.RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(AutoStart.RunKeyPath);
            if (key == null) return false;
            if (enable) key.SetValue(AutoStart.ValueName, AutoStart.BuildRunValue(ExePath));
            else key.DeleteValue(AutoStart.ValueName, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }
}
