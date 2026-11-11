namespace SpotifyLyricsWin.Core;

/// <summary>
/// 符号/图标字体过滤（v0.7）：系统字体列表里混着 Wingdings、Symbol、Segoe MDL2 Assets
/// 这类「字体」——它们的字形是图标/符号，用来渲染歌词就是用户截图里的乱码/方块。
/// 设置窗字体下拉构建时按黑名单剔除（匹配字体 Source 名，大小写不敏感的子串匹配）。
/// 黑名单只收符号字体专有标记词，正常文本字体（中英日韩、Yu Gothic、Segoe UI 等）不含这些词、不会误杀。
/// </summary>
public static class FontFilter
{
    private static readonly string[] SymbolTokens =
    {
        "symbol",           // Symbol、Noto Sans Symbols、Bookshelf Symbol 7 等符号集
        "wingdings",        // Wingdings / Wingdings 2 / Wingdings 3
        "webdings",         // Webdings
        "marlett",          // Marlett（窗口控件字形）
        "ms outlook",       // MS Outlook（信封等图标）
        "ms reference",     // MS Reference Specialty（符号）
        "mt extra",         // MT Extra（公式符号）
        "mdl2",             // Segoe MDL2 Assets、HoloLens MDL2 Assets（UWP 图标字体）
        "fluent icons",     // Segoe Fluent Icons（图标字体）
        "dingbats",         // ZapfDingbats / ITC Zapf Dingbats 类
        "monotype sorts",   // Monotype Sorts（装饰符号）
        "bookshelf",        // Bookshelf Symbol 系列
    };

    /// <summary>字体 Source 名（或显示名）是否为符号/图标字体（应从歌词字体列表里剔除）。</summary>
    public static bool IsSymbolFont(string? fontSourceOrName)
    {
        if (string.IsNullOrWhiteSpace(fontSourceOrName)) return false;
        string s = fontSourceOrName.ToLowerInvariant();
        foreach (string t in SymbolTokens)
            if (s.Contains(t)) return true;
        return false;
    }
}
