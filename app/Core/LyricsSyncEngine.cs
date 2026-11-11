namespace SpotifyLyricsWin.Core;

/// <summary>一行歌词的渲染状态：文本 + 已点亮到第几个字（移植自安卓 LineRender）</summary>
public sealed class LineRender
{
    public string Current { get; init; } = "";
    public string Next { get; init; } = "";
    public int CurrentIndex { get; init; } = -1;
    public int HighlightedChars { get; init; }
    /// <summary>true = 用的是歌词源自带的真实词级时间戳；false = 按字数插值估算</summary>
    public bool ExactWordTiming { get; init; }
    /// <summary>亚字符级推进比例 0~1，悬浮窗据此画平滑渐变擦除</summary>
    public float ProgressFraction { get; init; }

    public static readonly LineRender Empty = new();
}

/// <summary>
/// 歌词同步引擎（移植自安卓 LyricsSyncEngine.kt）：
/// 给一个播放进度，算出"当前该显示哪一行、亮到第几个字"。二分查找。
/// </summary>
public sealed class LyricsSyncEngine
{
    public Lyrics? Lyrics { get; set; }

    /// <summary>返回最后一条时间 &lt;= progressMs 的行号；还没到第一行时返回 -1</summary>
    public int IndexAt(long progressMs)
    {
        var lines = Lyrics?.Lines;
        if (lines == null || lines.Count == 0) return -1;
        int low = 0, high = lines.Count - 1, answer = -1;
        while (low <= high)
        {
            int mid = (low + high) >> 1;
            if (lines[mid].TimeMs <= progressMs) { answer = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return answer;
    }

    /// <summary>当前行 + 下一行（纯文本）</summary>
    public (string, string) CurrentAndNext(long progressMs)
    {
        var lines = Lyrics?.Lines;
        if (lines == null || lines.Count == 0) return ("", "");
        int index = IndexAt(progressMs);
        if (index < 0) return ("", lines[0].Text);
        return (lines[index].Text, index + 1 < lines.Count ? lines[index + 1].Text : "");
    }

    /// <summary>带字级进度的渲染状态，悬浮窗逐字高亮用</summary>
    public LineRender RenderAt(long progressMs, long durationMs = 0)
    {
        var lines = Lyrics?.Lines;
        if (lines == null || lines.Count == 0) return LineRender.Empty;
        int index = IndexAt(progressMs);
        if (index < 0)
            return new LineRender { Current = "", Next = lines[0].Text, CurrentIndex = -1 };
        var line = lines[index];
        LyricLine? nextLine = index + 1 < lines.Count ? lines[index + 1] : null;
        long[] times = WordTimeline.ForLine(line, nextLine?.TimeMs ?? 0, durationMs);
        long endMs = nextLine != null && nextLine.TimeMs > line.TimeMs
            ? nextLine.TimeMs
            : durationMs > line.TimeMs ? durationMs : line.TimeMs + 4000;
        return new LineRender
        {
            Current = line.Text,
            Next = nextLine?.Text ?? "",
            CurrentIndex = index,
            HighlightedChars = WordTimeline.HighlightedCount(times, progressMs),
            ExactWordTiming = line.CharTimesMs != null,
            ProgressFraction = WordTimeline.Fraction(times, progressMs, endMs)
        };
    }
}
