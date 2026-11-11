namespace SpotifyLyricsWin.Core;

/// <summary>
/// 文本分类小工具（v0.6，借鉴 LyricFlow Util.kt 的 chineseRatio/isChineseSong/isInstrumentalText）：
/// - 中文歌判定：汉字占比 &gt;45% 且无日文假名（阈值取 LyricFlow 实证的 45%，
///   不是 V2.4 文档的 30%——两项目参数不一致，整理文档 §7.5 已注明取实证值）；
/// - 纯音乐标题特征：含「纯音乐/伴奏/instrumental/暂无歌词」等词。
/// 纯函数、无状态，供取词链（译文跳过、纯音乐短路）与测试共用。
/// </summary>
public static class TextClassify
{
    /// <summary>汉字（CJK 统一表意文字基本区）占全文字符的比例。</summary>
    public static double ChineseRatio(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int c = 0;
        foreach (char ch in s)
            if (ch >= '\u4e00' && ch <= '\u9fff') c++;
        return (double)c / s.Length;
    }

    /// <summary>是否含日文假名（平假名/片假名区段）。</summary>
    public static bool HasKana(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char ch in s)
            if (ch >= '\u3040' && ch <= '\u30ff') return true;
        return false;
    }

    /// <summary>中文歌：汉字占比 >45% 且无假名（有假名按日文歌处理，不跳过译文）。</summary>
    public static bool IsChineseSong(string? text) =>
        ChineseRatio(text) > 0.45 && !HasKana(text);

    private static readonly string[] InstrumentalWords =
    {
        "纯音乐", "伴奏", "instrumental", "无歌词", "此歌曲为没有填词", "暂无歌词",
        "off vocal", "(inst)", "（inst）", "[inst]", "无人声"
    };

    /// <summary>文本是否带纯音乐特征（蓝本 LyricFlow isInstrumentalText 词表 + off vocal/(inst) 扩展）。</summary>
    public static bool IsInstrumentalText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        foreach (string w in InstrumentalWords)
            if (s.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

/// <summary>
/// 纯音乐标记存取（v0.6，借鉴 LyricFlow 标记体系）：标记存 Settings.InstrumentalKeys
/// （曲目 Key、上限 500）。标题特征命中会自动写标记；用户也可手动标记/取消。
/// </summary>
public static class InstrumentalMarks
{
    public const int Cap = 500;

    public static bool IsMarked(Settings settings, string trackKey) =>
        settings.InstrumentalKeys.Contains(trackKey);

    /// <summary>标题特征自动判定（只看标题，蓝本 LyricFlow 的标题特征来源）。</summary>
    public static bool TitleLooksInstrumental(string title) =>
        TextClassify.IsInstrumentalText(title);

    public static void Mark(Settings settings, string trackKey)
    {
        Settings.AddCapped(settings.InstrumentalKeys, trackKey, Cap);
        settings.RequestSave();
    }

    public static void Unmark(Settings settings, string trackKey)
    {
        if (settings.InstrumentalKeys.Remove(trackKey)) settings.RequestSave();
    }
}
