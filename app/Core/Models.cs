using System.Diagnostics;

namespace SpotifyLyricsWin.Core;

/// <summary>
/// 播放状态（移植自安卓 Models.kt TrackInfo）。
/// 进度用「记录时的 position + 之后流逝的时间」推算，避免频繁问系统；
/// 暂停时停表。时间基准用 Stopwatch（单调时钟，等价于 SystemClock.elapsedRealtime）。
/// </summary>
public sealed class TrackInfo
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public long DurationMs { get; init; }
    public long ProgressMs { get; init; }
    public bool IsPlaying { get; init; }
    /// <summary>
    /// SMTC 是否给了封面图（v0.3 广告识别信号）。默认 true=按「有封面」处理：
    /// 未知时宁可不判广告（保守）；SmtcTracker 会按实际元数据覆盖此值。
    /// </summary>
    public bool HasCover { get; init; } = true;
    /// <summary>记录 ProgressMs 时的 Stopwatch 毫秒时间戳</summary>
    public long UpdatedAtMs { get; init; } = NowMs();

    public static long NowMs() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    /// <summary>歌词缓存/切歌判定的唯一键（繁体转简体，避免同一首歌存两份）</summary>
    public string Key =>
        $"{Simplifier.ToSimplified(Title).Trim()}|{Simplifier.ToSimplified(Artist).Trim()}".ToLowerInvariant();

    /// <summary>实时进度（毫秒）</summary>
    public long CurrentProgress(long nowMs)
    {
        if (!IsPlaying) return ProgressMs;
        long p = ProgressMs + Math.Max(0, nowMs - UpdatedAtMs);
        if (DurationMs > 0) p = Math.Min(p, DurationMs);
        return p;
    }

    public long CurrentProgress() => CurrentProgress(NowMs());
}

/// <summary>一行带时间轴的歌词。CharTimesMs != null 即「真逐字」。</summary>
public sealed class LyricLine
{
    public long TimeMs { get; }
    public string Text { get; }
    /// <summary>每个字符的绝对开始时间（毫秒），来自增强 LRC 的词级标签；null 表示运行时插值。</summary>
    public long[]? CharTimesMs { get; }

    public LyricLine(long timeMs, string text, long[]? charTimesMs = null)
    {
        TimeMs = timeMs;
        Text = text;
        CharTimesMs = charTimesMs;
    }
}

/// <summary>一首歌的完整歌词。Translations 与 Lines 同索引，没有译文的项是空串。</summary>
public sealed class Lyrics
{
    public List<LyricLine> Lines { get; }
    public string Title { get; }
    public string Artist { get; }
    public List<string> Translations { get; }

    public Lyrics(List<LyricLine> lines, string title = "", string artist = "",
        List<string>? translations = null)
    {
        Lines = lines;
        Title = title;
        Artist = artist;
        Translations = translations ?? new List<string>();
    }

    public string TranslationAt(int index) =>
        index >= 0 && index < Translations.Count ? Translations[index] : "";

    public bool IsWordLevel => Lines.Any(l => l.CharTimesMs != null);

    public static readonly Lyrics Empty = new(new List<LyricLine>());
}

/// <summary>
/// 繁→简转换（对应安卓版 ChineseConverter.toSimplified）。
/// v0.3 起用完整单字映射表（SimplifierData，3510 对，源自 MediaWiki 繁简对照），
/// 不再是 v0.2 的百余字高频表——歌名/歌手的繁体字基本都能转。
/// 应用点：曲目 Key、搜索关键词、候选匹配归一化、手动选词预填显示。
/// </summary>
public static class Simplifier
{
    private static readonly Dictionary<char, char> Map = Build();

    /// <summary>映射表条数（供测试/诊断）。</summary>
    public static int MapCount => Map.Count;

    private static Dictionary<char, char> Build()
    {
        string pairs = SimplifierData.Pairs;
        var d = new Dictionary<char, char>(pairs.Length / 2);
        for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
        return d;
    }

    public static string ToSimplified(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        char[] chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (Map.TryGetValue(chars[i], out char v)) chars[i] = v;
        return new string(chars);
    }
}

/// <summary>
/// 显示层繁→简转换（v0.7，「繁体转简体」开关开时用）：把一份歌词复制成显示副本，
/// **原文行与自带译文行**都按字 1:1 映射成简体。映射是 char→char，行长与逐字时间数组
/// （CharTimesMs，同一数组引用复用）都不受影响，扫光节奏不变。
/// 只转显示副本——缓存文件、搜索匹配用的原始歌词永不被改（关掉开关即可原样显示繁体）。
/// </summary>
public static class LyricsDisplay
{
    public static Lyrics ConvertForDisplay(Lyrics raw)
    {
        var lines = new List<LyricLine>(raw.Lines.Count);
        foreach (var l in raw.Lines)
            lines.Add(new LyricLine(l.TimeMs, Simplifier.ToSimplified(l.Text), l.CharTimesMs));
        var trans = new List<string>(raw.Translations.Count);
        foreach (string t in raw.Translations)
            trans.Add(Simplifier.ToSimplified(t));
        return new Lyrics(lines, raw.Title, raw.Artist, trans);
    }
}
