namespace SpotifyLyricsWin.Core;

/// <summary>
/// 字体本地化名的选择逻辑（v0.6）：从一组 (语言标签, 名称) 里按
/// zh-cn → zh-hans → en-us → 任意第一个非空 的顺序挑显示名。
/// 纯函数、不依赖 WPF（FontFamily.FamilyNames 的转换在 SettingsWindow 里做），便于无头测试。
/// </summary>
public static class FontNamePick
{
    public static string Pick(IReadOnlyList<(string Lang, string Name)> names, string fallback)
    {
        string? Find(string lang)
        {
            foreach (var (l, n) in names)
                if (string.Equals(l, lang, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(n))
                    return n;
            return null;
        }
        return Find("zh-cn") ?? Find("zh-Hans") ?? Find("en-us")
            ?? names.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Name)).Name
            ?? fallback;
    }
}
