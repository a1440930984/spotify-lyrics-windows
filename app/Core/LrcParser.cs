using System.Text.RegularExpressions;

namespace SpotifyLyricsWin.Core;

/// <summary>
/// LRC 歌词解析器（移植自安卓 LrcParser.kt）。
/// 支持 [mm:ss.xx]、[mm:ss:xx]、一行多个时间标签、[offset:]，
/// 以及增强 LRC 的行内词级标签 &lt;mm:ss.xx&gt;，产出 LyricLine.CharTimesMs。
/// 酷狗 KRC 解密后会转成本格式（每字一个绝对时间标签）。
/// </summary>
public static class LrcParser
{
    private static readonly Regex TimeTag = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]");
    private static readonly Regex WordTag = new(@"<(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?>");
    private static readonly Regex OffsetTag = new(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase);

    public static Lyrics Parse(string lrc, string title = "", string artist = "")
    {
        // 全局偏移（毫秒），LRC 规范里正数表示歌词整体提前
        long offsetMs = 0;
        var om = OffsetTag.Match(lrc);
        if (om.Success && long.TryParse(om.Groups[1].Value, out long ov)) offsetMs = -ov;

        var result = new List<LyricLine>();
        foreach (string raw in lrc.Replace("\r\n", "\n").Split('\n'))
        {
            var tags = TimeTag.Matches(raw);
            if (tags.Count == 0) continue;
            string body = TimeTag.Replace(raw, "").Trim();
            if (body.Length == 0) continue;
            var (text, charTimes) = SplitWordTags(body, offsetMs);
            if (text.Length == 0) continue;
            foreach (Match m in tags)
            {
                if (!long.TryParse(m.Groups[1].Value, out long min)) continue;
                if (!long.TryParse(m.Groups[2].Value, out long sec)) continue;
                long time = Math.Max(0, min * 60000 + sec * 1000 + FracToMs(m.Groups[3].Value) + offsetMs);
                result.Add(new LyricLine(time, text, charTimes));
            }
        }
        var sorted = result.OrderBy(l => l.TimeMs)
            .DistinctBy(l => (l.TimeMs, l.Text))
            .ToList();
        return new Lyrics(sorted, title, artist);
    }

    /// <summary>把 "xx"/"x"/"xxx" 形式的毫秒小数段转成毫秒</summary>
    private static long FracToMs(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return 0;
        if (raw.Length == 1) return long.Parse(raw) * 100;
        if (raw.Length == 2) return long.Parse(raw) * 10;
        return long.Parse(raw.Substring(0, 3));
    }

    private static long TagToMs(Match m)
    {
        if (!long.TryParse(m.Groups[1].Value, out long min)) return 0;
        if (!long.TryParse(m.Groups[2].Value, out long sec)) return 0;
        return min * 60000 + sec * 1000 + FracToMs(m.Groups[3].Value);
    }

    /// <summary>
    /// 解析增强 LRC：标签的时间戳是它后面那段词的起点。
    /// 若标签不在行首、或时间轴长度和文本对不上，就放弃词级信息回退到运行时插值。
    /// </summary>
    private static (string, long[]?) SplitWordTags(string body, long offsetMs)
    {
        var tags = WordTag.Matches(body);
        if (tags.Count == 0) return (body, null);
        if (tags[0].Index > 0) return (StripTags(body), null);

        var sb = new System.Text.StringBuilder();
        var times = new List<long>(body.Length);
        for (int i = 0; i < tags.Count; i++)
        {
            int from = tags[i].Index + tags[i].Length;
            int to = i + 1 < tags.Count ? tags[i + 1].Index : body.Length;
            if (from >= to) continue; // 末尾的"行结束"标签，没有跟随文本
            string seg = body.Substring(from, to - from);
            long segStart = TagToMs(tags[i]) + offsetMs;
            long segEnd = i + 1 < tags.Count ? TagToMs(tags[i + 1]) + offsetMs
                                             : segStart + seg.Length * 120;
            long span = Math.Max(1, segEnd - segStart);
            for (int j = 0; j < seg.Length; j++)
            {
                sb.Append(seg[j]);
                times.Add(segStart + span * j / seg.Length);
            }
        }
        string text = sb.ToString();
        long[] arr = times.ToArray();
        return (text, arr.Length == text.Length && text.Length > 0 ? arr : null);
    }

    private static string StripTags(string s) => WordTag.Replace(s, "").Trim();

    /// <summary>
    /// 降级：歌词源只给纯文本（没有时间轴）时，按歌曲时长把每行均匀铺开。
    /// </summary>
    public static Lyrics FromPlain(string plain, long durationMs, string title, string artist)
    {
        var texts = plain.Replace("\r\n", "\n").Split('\n')
            .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (texts.Count == 0) return new Lyrics(new List<LyricLine>(), title, artist);
        long step = durationMs > 0 ? durationMs / texts.Count : 4000;
        var lines = texts.Select((t, i) => new LyricLine(i * step, t)).ToList();
        return new Lyrics(lines, title, artist);
    }

    /// <summary>毫秒 → [mm:ss.xx] 时间标签（把纯文本/时间列表转回 LRC 文本时用）</summary>
    public static string StampTag(long ms)
    {
        long v = Math.Max(0, ms);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[{v / 60000L:00}:{(v % 60000L) / 1000L:00}.{v % 1000L / 10:00}]");
    }

    /// <summary>把译文 LRC 按时间戳对齐到主歌词行（精确时间戳匹配，安安卓 tlyric 对齐方式）</summary>
    public static List<string> AlignTranslations(Lyrics main, Lyrics trans)
    {
        var byTime = new Dictionary<long, string>();
        foreach (var l in trans.Lines) byTime[l.TimeMs] = l.Text;
        var list = new List<string>(main.Lines.Count);
        foreach (var l in main.Lines)
            list.Add(byTime.TryGetValue(l.TimeMs, out string? t) ? t : "");
        return list;
    }
}
