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
            // v0.6：元数据行过滤（借鉴 V2.4 cleanMetaLines）：作词/作曲等行带时间标签、
            // 会混进正文显示、还会把译文按行序兜底整体带偏，解析时直接跳过。
            if (IsMetaLine(body)) continue;
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

    private static readonly Regex MetaLine = new(
        @"^(作词|作曲|编曲|制作人|出品人|出品|监制|和声|合声|录音|混音|母带|吉他|贝斯|鼓|键盘|弦乐|琵琶|二胡|笛子|演奏|演唱|原唱|词|曲|OP|SP|发行)\s*[:：]",
        RegexOptions.Compiled);

    /// <summary>是否是「作词：xx」类元数据行（供 Parse 过滤与测试）。</summary>
    public static bool IsMetaLine(string text) =>
        !string.IsNullOrWhiteSpace(text) && MetaLine.IsMatch(text.Trim());

    /// <summary>
    /// 把译文 LRC 对齐到主歌词行（v0.6 重写，蓝本 LyricFlow Translation.kt 的 merge()，
    /// 替换 v0.5 以前的「精确时间戳字典匹配」——差 1ms 就整行丢弃，跨源贴译文几乎全空）：
    /// ① 最近行匹配：每行原文找时间差最小的未用译文行，容差 4000ms（含等于），
    ///    一对一占用（used[] 防同一译文行被贴两次）；
    /// ② 命中率（有译文的非空原文行 / 非空原文行）&lt;40% 时退回按行序对齐：
    ///    双方各自跳过空行、逐行顺贴。
    /// </summary>
    public static List<string> AlignTranslations(Lyrics main, Lyrics trans)
    {
        int n = main.Lines.Count;
        var mapped = new string?[n];
        var used = new bool[trans.Lines.Count];
        int hits = 0;

        for (int i = 0; i < n; i++)
        {
            long t = main.Lines[i].TimeMs;
            int best = -1;
            long bestDiff = 4000;
            for (int j = 0; j < trans.Lines.Count; j++)
            {
                if (used[j]) continue;
                long d = Math.Abs(trans.Lines[j].TimeMs - t);
                if (d <= bestDiff) { bestDiff = d; best = j; }
            }
            if (best >= 0)
            {
                used[best] = true;
                string s = trans.Lines[best].Text.Trim();
                if (s.Length > 0) { mapped[i] = s; hits++; }
            }
        }

        int valid = main.Lines.Count(l => !string.IsNullOrWhiteSpace(l.Text));
        double hitRate = valid > 0 ? (double)hits / valid : 0;
        if (valid > 0 && hitRate < 0.40)
        {
            // 行序兜底：清掉时间轴结果，双方跳空行顺贴
            Array.Clear(mapped);
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                if (string.IsNullOrWhiteSpace(main.Lines[i].Text)) continue;
                while (k < trans.Lines.Count && string.IsNullOrWhiteSpace(trans.Lines[k].Text)) k++;
                if (k < trans.Lines.Count) mapped[i] = trans.Lines[k++].Text.Trim();
            }
        }

        var list = new List<string>(n);
        for (int i = 0; i < n; i++) list.Add(mapped[i] ?? "");
        return list;
    }
}
