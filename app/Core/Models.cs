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
/// 常用繁→简映射（安卓版用 ChineseConverter，这里收录高频字，够歌名/歌手去重用）。
/// </summary>
public static class Simplifier
{
    private static readonly Dictionary<char, char> Map = Build();

    private static Dictionary<char, char> Build()
    {
        const string pairs =
            "愛爱樂乐國国華华龍龙鳳凤雲云風风雨點点們们這这裡里麼么麼么時时還还過过來来去們们說说聽听見见開开關关門门問问間间陽阳陰阴難难離离夢梦聲声歌詞词曲線线長长髮发現现實实個个為为麼么將将從从當当對对會会學学書书畫画東东車车馬马鳥鸟魚鱼語语讀读寫写發发頭头買买賣卖開开關关們们這这個个來来時时說说國国會会過过還还進进遠远運运邊边這这連连達达麼么選选這这遞递適适裡里釋释鑑鉴鍵键鏈链銷销鎖锁鐵铁鑄铸鐘钟錦锦錫锡錯错鍋锅鋼钢錢钱鍵键鑽钻鑲镶鏡镜閃闪閉闭問问闖闯階阶陰阴陣阵陳陈陸陆陽阳隸隶雜杂難难霧雾靈灵頁页頂顶項项順顺預预領领頻频題题額额風风飛飞飯饭飲饮館馆馬马驗验驚惊體体魚鱼鳥鸟鴿鸽鳳凤黃黄點点黨党齊齐齒齿龍龙龜龟龔龚";
        var d = new Dictionary<char, char>();
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
