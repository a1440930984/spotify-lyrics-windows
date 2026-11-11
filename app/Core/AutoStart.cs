namespace SpotifyLyricsWin.Core;

/// <summary>
/// 开机自动启动的纯逻辑（v0.8）：实现走当前用户注册表 Run 键
/// （HKCU\Software\Microsoft\Windows\CurrentVersion\Run），值名固定、值 = 带引号的
/// exe 完整路径。注册表实际读写在 Ui.AutoStartRegistry（平台 API 不进无头测试工程），
/// 这里只管值名/值构造/状态判定（注册表值 == 期望值 才算「已开启」，路径变了自动视为未对齐）。
/// </summary>
public static class AutoStart
{
    public const string ValueName = "SpotifyLyricsWin";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Run 键里应写入的值：带双引号的 exe 路径（路径含空格也安全）。</summary>
    public static string BuildRunValue(string exePath) => $"\"{exePath.Trim()}\"";

    /// <summary>注册表当前值是否与期望值一致（一致 = 自启已开启且指向当前这份 exe）。</summary>
    public static bool IsAligned(string? registryValue, string exePath) =>
        registryValue != null
        && string.Equals(registryValue.Trim(), BuildRunValue(exePath),
            StringComparison.OrdinalIgnoreCase);
}
