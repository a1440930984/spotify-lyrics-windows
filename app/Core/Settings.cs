using System.IO;
using System.Text.Json;

namespace SpotifyLyricsWin.Core;

/// <summary>
/// 全部设置的默认值（v0.8.1 审计统一，单一事实来源）：首装的属性初始值、
/// Normalize 的兜底、ResetStyle 的恢复值都只准引用这里，不许各写一份——
/// 此前字号首装 18 与复刻规格/恢复默认各说各话，就是没有单一来源的教训。
/// 定案口径参考主流桌面歌词/播放器：主题浅色、逐字/译文/繁简/机翻全开、
/// 双行、字号 30（悬浮大字阅读距离比普通 UI 远，30 是桌面歌词常见中位）、
/// 高亮 Spotify 绿、文字纯白、背景全透明、偏移 0、自启关、凭据类全空。
/// </summary>
public static class Defaults
{
    public const bool WordByWordEnabled = true;
    public const bool FloatingEnabled = true;
    public const bool ShowTranslation = true;
    public const int FontSize = 30;                          // 12~60
    public const string TextColorHex = "#FFFFFFFF";
    public const string HighlightColorHex = "#FF1ED760";
    public const int BgAlphaPercent = 0;
    public const long LyricsOffsetMs = 0;
    public const string FontFamily = "Microsoft YaHei UI";
    public const bool PositionLocked = false;
    public const bool SingleLineMode = false;                // false = 双行
    public const string UiTheme = "light";
    public const bool AutoTranslate = true;
    public const bool ConvertTradToSimp = true;
    public const string TranslateService = "auto";
    public const bool AutoStartEnabled = false;
    public const bool OpenSettingsOnStartup = true;          // v0.8.2：启动自动打开设置页（默认开，可关）
    public const string Empty = "";
}

/// <summary>
/// 本地配置（对应安卓 Prefs.kt）。持久化到 %AppData%/SpotifyLyricsWin/settings.json。
/// 默认值见 Defaults（v0.8.1 起单一事实来源）。
/// </summary>
public sealed class Settings
{
    public bool WordByWordEnabled { get; set; } = Defaults.WordByWordEnabled;
    public bool FloatingEnabled { get; set; } = Defaults.FloatingEnabled;
    public bool ShowTranslation { get; set; } = Defaults.ShowTranslation;
    public int FontSize { get; set; } = Defaults.FontSize;   // 12~60（v0.7 上限 36→60，给大屏留余量）
    public string TextColorHex { get; set; } = Defaults.TextColorHex; // #AARRGGBB
    public string HighlightColorHex { get; set; } = Defaults.HighlightColorHex;
    public int BgAlphaPercent { get; set; } = Defaults.BgAlphaPercent; // 0~100
    public long LyricsOffsetMs { get; set; } = Defaults.LyricsOffsetMs; // ±30000 钳制
    public double? FloatingX { get; set; }
    public double? FloatingY { get; set; }
    public string FontFamily { get; set; } = Defaults.FontFamily; // 新增项：字体选择
    public string MusixmatchApiKey { get; set; } = Defaults.Empty;
    /// <summary>
    /// v0.4 新增：GitHub 只读令牌（fine-grained，仅 spotify-lyrics-windows 仓、Contents 只读）。
    /// 仅用于检查/下载本仓库的 Release 做自动更新，只存本机 settings.json，绝不外传。
    /// 留空 = 不检查更新。
    /// </summary>
    public string GithubToken { get; set; } = Defaults.Empty;
    /// <summary>v0.2 新增：锁定悬浮窗位置（锁定后不可拖动）</summary>
    public bool PositionLocked { get; set; } = Defaults.PositionLocked;
    /// <summary>v0.2 新增：单行模式（仅当前行；双行 = 当前行 + 下一行）</summary>
    public bool SingleLineMode { get; set; } = Defaults.SingleLineMode;
    /// <summary>v0.5 新增：设置窗位置记忆（Left/Top，DIP；null = 没记过，首开居中）</summary>
    public double? SettingsWindowX { get; set; }
    public double? SettingsWindowY { get; set; }
    /// <summary>v0.5 新增：手动选词窗位置记忆（颜色盘明确不记忆，每次居中于所有者）</summary>
    public double? ManualWindowX { get; set; }
    public double? ManualWindowY { get; set; }
    /// <summary>
    /// 界面主题 id（spotify-dark / pure-black / deep-blue / light），见 Ui.Theme.Palettes。
    /// v0.7：新装默认改浅色（Sun 拍板）；已存设置的用户读到的仍是自己存的值，不受影响。
    /// </summary>
    public string UiTheme { get; set; } = Defaults.UiTheme;
    /// <summary>v0.7 新增：无自带译文时自动机器翻译成中文兜底（默认开）</summary>
    public bool AutoTranslate { get; set; } = Defaults.AutoTranslate;
    /// <summary>
    /// v0.7 新增：繁体转简体显示（默认开）。开时显示层把原文行 + 自带译文行按字转简体，
    /// 只转显示副本、不动缓存与匹配；关则原样显示（有些歌就是要看繁体原词）。
    /// </summary>
    public bool ConvertTradToSimp { get; set; } = Defaults.ConvertTradToSimp;
    /// <summary>
    /// v0.8 新增：翻译服务选择（auto/mymemory/google/baidu/youdao/microsoft，见
    /// Lyrics.TranslateServiceIds）。auto = 现行自动链（Google gtx → MyMemory）；
    /// 选单个服务时优先用它，失败自动回落自动链，不会因选路而断路。
    /// </summary>
    public string TranslateService { get; set; } = Defaults.TranslateService;
    /// <summary>v0.8 新增：MyMemory 的 de 邮箱（可选，只存本机；带上后日额度 5000→50000 字符）。</summary>
    public string MyMemoryEmail { get; set; } = Defaults.Empty;
    /// <summary>v0.8 新增：百度翻译开放平台凭据（APP ID + 密钥，只存本机，用户自备免费额度）。</summary>
    public string BaiduAppId { get; set; } = Defaults.Empty;
    public string BaiduKey { get; set; } = Defaults.Empty;
    /// <summary>v0.8 新增：有道智云凭据（应用 ID + 应用密钥，只存本机，用户自备）。</summary>
    public string YoudaoAppId { get; set; } = Defaults.Empty;
    public string YoudaoKey { get; set; } = Defaults.Empty;
    /// <summary>v0.8 新增：微软 Azure 翻译凭据（密钥 + 区域，如 eastasia；只存本机，用户自备）。</summary>
    public string MsTranslatorKey { get; set; } = Defaults.Empty;
    public string MsTranslatorRegion { get; set; } = Defaults.Empty;
    /// <summary>
    /// v0.8 新增：开机自动启动的「期望状态」记录（真实来源是 HKCU Run 注册表值，
    /// 设置窗打开时以注册表实际值对齐回写本字段）。
    /// </summary>
    public bool AutoStartEnabled { get; set; } = Defaults.AutoStartEnabled;
    /// <summary>
    /// v0.8.2 新增：启动时自动打开设置页（默认开）。更新完成后程序重启也算一次启动，
    /// 故更新后展示结果与日常启动共用同一条自动打开路径（居中）；关掉则启动不弹
    /// （更新结果仍在托盘状态/下次手动打开设置时可见）。
    /// </summary>
    public bool OpenSettingsOnStartup { get; set; } = Defaults.OpenSettingsOnStartup;
    /// <summary>
    /// v0.6 新增：纯音乐标记（曲目 Key 集合，借鉴 LyricFlow instrumentalKeys，上限 500，超限淘汰最旧）。
    /// 命中标记的曲目不再联网取词，悬浮窗显示「纯音乐」提示。
    /// </summary>
    public List<string> InstrumentalKeys { get; set; } = new();
    /// <summary>
    /// v0.6 新增：「这不是广告」抑制名单（曲目 Key 集合，上限 200，超限淘汰最旧）。
    /// 名单内的曲目永远不判广告（用户人工纠偏，粒度按 Key 而非全局时长）。
    /// </summary>
    public List<string> NotAdKeys { get; set; } = new();

    public void Normalize()
    {
        FontSize = Math.Clamp(FontSize, 12, 60);
        BgAlphaPercent = Math.Clamp(BgAlphaPercent, 0, 100);
        LyricsOffsetMs = Math.Clamp(LyricsOffsetMs, -30000, 30000);
        if (!IsValidHex(TextColorHex)) TextColorHex = Defaults.TextColorHex;
        if (!IsValidHex(HighlightColorHex)) HighlightColorHex = Defaults.HighlightColorHex;
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = Defaults.FontFamily;
        if (string.IsNullOrWhiteSpace(UiTheme)) UiTheme = Defaults.UiTheme;
        if (!SpotifyLyricsWin.Lyrics.TranslateServiceIds.IsValid(TranslateService)) TranslateService = Defaults.TranslateService;
        MyMemoryEmail ??= ""; BaiduAppId ??= ""; BaiduKey ??= "";
        YoudaoAppId ??= ""; YoudaoKey ??= "";
        MsTranslatorKey ??= ""; MsTranslatorRegion ??= "";
        InstrumentalKeys ??= new List<string>();
        NotAdKeys ??= new List<string>();
        TrimToCap(InstrumentalKeys, 500);
        TrimToCap(NotAdKeys, 200);
    }

    /// <summary>集合限长：只保留最近 cap 条（列表尾部最新），淘汰最旧。</summary>
    public static void TrimToCap(List<string> list, int cap)
    {
        if (list.Count > cap) list.RemoveRange(0, list.Count - cap);
    }

    /// <summary>往限长集合里加一个 key（已有则先删再加到尾部，保持「最近」语义）。</summary>
    public static void AddCapped(List<string> list, string key, int cap)
    {
        list.Remove(key);
        list.Add(key);
        TrimToCap(list, cap);
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
        FontSize = Defaults.FontSize;
        TextColorHex = Defaults.TextColorHex;
        HighlightColorHex = Defaults.HighlightColorHex;
        BgAlphaPercent = Defaults.BgAlphaPercent;
        WordByWordEnabled = Defaults.WordByWordEnabled;
        ShowTranslation = Defaults.ShowTranslation;
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
