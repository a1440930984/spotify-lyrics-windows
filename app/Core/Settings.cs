using System.IO;
using System.Text.Json;

namespace SpotifyLyricsWin.Core;

/// <summary>
/// 本地配置（对应安卓 Prefs.kt）。持久化到 %AppData%/SpotifyLyricsWin/settings.json。
/// 默认值照复刻规格：字号 18、高亮 #FF1ED760、文字 #FFFFFFFF、背景 0（纯文字无框）、逐字开。
/// </summary>
public sealed class Settings
{
    public bool WordByWordEnabled { get; set; } = true;
    public bool FloatingEnabled { get; set; } = true;
    public bool ShowTranslation { get; set; } = true;
    public int FontSize { get; set; } = 18;                 // 12~36
    public string TextColorHex { get; set; } = "#FFFFFFFF"; // #AARRGGBB
    public string HighlightColorHex { get; set; } = "#FF1ED760";
    public int BgAlphaPercent { get; set; } = 0;            // 0~100
    public long LyricsOffsetMs { get; set; } = 0;           // ±30000 钳制
    public double? FloatingX { get; set; }
    public double? FloatingY { get; set; }
    public string FontFamily { get; set; } = "Microsoft YaHei UI"; // 新增项：字体选择
    public string MusixmatchApiKey { get; set; } = "";
    /// <summary>v0.2 新增：锁定悬浮窗位置（锁定后不可拖动）</summary>
    public bool PositionLocked { get; set; } = false;
    /// <summary>v0.2 新增：单行模式（仅当前行；双行 = 当前行 + 下一行）</summary>
    public bool SingleLineMode { get; set; } = false;

    public void Normalize()
    {
        FontSize = Math.Clamp(FontSize, 12, 36);
        BgAlphaPercent = Math.Clamp(BgAlphaPercent, 0, 100);
        LyricsOffsetMs = Math.Clamp(LyricsOffsetMs, -30000, 30000);
        if (!IsValidHex(TextColorHex)) TextColorHex = "#FFFFFFFF";
        if (!IsValidHex(HighlightColorHex)) HighlightColorHex = "#FF1ED760";
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = "Microsoft YaHei UI";
    }

    public static bool IsValidHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        string h = s.Trim().TrimStart('#');
        if (h.Length != 6 && h.Length != 8) return false;
        foreach (char c in h)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    /// <summary>恢复默认外观（安卓 btnResetStyle：纯文字 + 绿色平滑逐字）</summary>
    public void ResetStyle()
    {
        FontSize = 18;
        TextColorHex = "#FFFFFFFF";
        HighlightColorHex = "#FF1ED760";
        BgAlphaPercent = 0;
        WordByWordEnabled = true;
        ShowTranslation = true;
    }

    // ---------- 持久化 ----------

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpotifyLyricsWin");

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");
    public static string CacheDir => Path.Combine(AppDataDir, "cache");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (s != null) { s.Normalize(); return s; }
            }
        }
        catch { /* 损坏就用默认 */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            Normalize();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* 保存失败不崩溃 */ }
    }

    // ---------- 防抖保存（v0.2：设置实时生效，改动合并写入） ----------

    private static System.Threading.Timer? _debounceTimer;
    private static readonly object _debounceLock = new();

    /// <summary>请求一次防抖保存：300ms 内的多次改动只写一次盘。</summary>
    public void RequestSave()
    {
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(
                _ => { try { Save(); } catch { } },
                null, 300, System.Threading.Timeout.Infinite);
        }
    }
}
