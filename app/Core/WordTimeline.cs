namespace SpotifyLyricsWin.Core;

/// <summary>
/// 字级时间轴（逐行移植自安卓 WordTimeline.kt V1.6/V1.8）。
/// ① 源里有真实词级时间戳 → 直接用；
/// ② 只有行级 LRC → 在「本行起点 → 下一行起点」之间按字符权重插值。
/// fraction() 给出亚字符级 0~1 连续进度，是渐变扫光不跳字的关键。
/// </summary>
public static class WordTimeline
{
    /// <summary>单句最长按 8 秒推进，避免间奏很长时字慢慢爬</summary>
    private const long MaxLineMs = 8000;
    /// <summary>最后一句没有"下一行"时，默认给 4 秒</summary>
    private const long DefaultLastLineMs = 4000;
    /// <summary>句尾通常有停顿/拖音，只把前 90% 的时长用于推进点亮</summary>
    private const long UsablePercent = 90;

    /// <summary>每个字符的绝对开始时间（毫秒），长度 == line.Text.Length</summary>
    public static long[] ForLine(LyricLine line, long lineEndMs, long durationMs)
    {
        string text = line.Text;
        if (text.Length == 0) return Array.Empty<long>();

        // ① 源里有真实词级时间戳，且长度对得上
        if (line.CharTimesMs != null && line.CharTimesMs.Length == text.Length)
            return line.CharTimesMs;

        // ② 按字符权重插值
        long start = line.TimeMs;
        long end;
        if (lineEndMs > start) end = Math.Min(lineEndMs, start + MaxLineMs);
        else if (durationMs > start) end = Math.Min(durationMs, start + MaxLineMs);
        else end = start + DefaultLastLineMs;
        if (end <= start) end = start + DefaultLastLineMs;
        long usable = Math.Max(1, (end - start) * UsablePercent / 100);

        var weights = new double[text.Length];
        double total = 0;
        for (int i = 0; i < text.Length; i++) { weights[i] = WeightOf(text[i]); total += weights[i]; }
        if (total <= 0) total = 1.0;

        var times = new long[text.Length];
        double acc = 0.0;
        for (int i = 0; i < text.Length; i++)
        {
            times[i] = start + (long)(usable * acc / total);
            acc += weights[i];
        }
        return times;
    }

    /// <summary>进度对应的已点亮字符数</summary>
    public static int HighlightedCount(long[] times, long progressMs)
    {
        int n = 0;
        while (n < times.Length && times[n] <= progressMs) n++;
        return n;
    }

    /// <summary>
    /// 亚字符级进度 0~1：在当前字到下一个字之间按时间线性插值，
    /// 交给渲染层画成带柔边的渐变（卡拉OK 扫光）。
    /// </summary>
    public static float Fraction(long[] times, long progressMs, long lineEndMs)
    {
        if (times.Length == 0) return 0f;
        if (progressMs <= times[0]) return 0f;
        long tail = Math.Max(times[^1] + 320, lineEndMs);
        if (progressMs >= tail) return 1f;

        int i = 0;
        while (i + 1 < times.Length && times[i + 1] <= progressMs) i++;
        long t0 = times[i];
        long t1 = i + 1 < times.Length ? times[i + 1] : tail;
        float within = t1 > t0
            ? Math.Clamp((progressMs - t0) / (float)(t1 - t0), 0f, 1f)
            : 1f;
        return Math.Clamp((i + within) / times.Length, 0f, 1f);
    }

    /// <summary>
    /// 字符权重：中文/日文/韩文一字一拍；拉丁字母按半个字算（连读快）；
    /// 空格、标点几乎不占时间，避免它们在视觉上"卡住"高亮。
    /// </summary>
    private static double WeightOf(char c)
    {
        if (char.IsWhiteSpace(c)) return 0.15;
        if ("，。,.!?;:()（）-—…、~♪".IndexOf(c) >= 0) return 0.2;
        if ((c >= '一' && c <= '鿿') || (c >= '぀' && c <= 'ヿ') || (c >= '가' && c <= '힣')) return 1.0;
        if (char.IsLetterOrDigit(c)) return 0.6;
        return 0.5;
    }
}
