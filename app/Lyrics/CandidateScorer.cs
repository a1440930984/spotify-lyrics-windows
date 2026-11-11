namespace SpotifyLyricsWin.Lyrics;

/// <summary>
/// 自动匹配打分（v0.3 新增）：给一个候选按「像不像当前曲目」打分，
/// 自动模式并行搜完国内四源后选最高分（≥50 才采用），治 v0.2「顺序首命中」
/// 经常配错版/配不上的问题。分值表是与用户对齐过的，不要随意改：
///   标题归一化完全相等 +50 / 互相包含 +30
///   歌手命中 +30
///   时长差 ≤2s +20 / ≤5s +10 / >10s −20
///   逐字（KRC 等真逐字源）+15
///   标题含 Live/翻唱/伴奏/铃声/cover 且原曲标题不含 → −25（只罚一次）
/// v0.6 增补（借鉴清单 #7，出处 LyricFlow Matcher 的 Levenshtein 选型 + V2.4 的标题淘汰线；
/// 主权重一概未动，只加两条兜底）：
///   标题既不相等也不包含时 → Levenshtein 相似度 ×40 取整（连续分，上限 +40，
///     只在 sim ≥0.6 时给，低于此视为不相关给 0）；
///   标题淘汰线 TitleEliminated：不相等、不包含、相似度 &lt;0.45 → 打分阶段直接跳过该候选。
/// </summary>
public static class CandidateScorer
{
    private static readonly string[] EditionKeywords = { "live", "翻唱", "伴奏", "铃声", "cover" };

    /// <summary>归一化标题的 Levenshtein 相似度（0~1）：1 − 编辑距离 / 较长串长度。</summary>
    public static double TitleSimilarity(string a, string b)
    {
        string x = HttpHelp.Norm(a), y = HttpHelp.Norm(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        if (x == y) return 1.0;
        int dist = Levenshtein(x, y);
        return 1.0 - (double)dist / Math.Max(x.Length, y.Length);
    }

    private static int Levenshtein(string x, string y)
    {
        var prev = new int[y.Length + 1];
        var cur = new int[y.Length + 1];
        for (int j = 0; j <= y.Length; j++) prev[j] = j;
        for (int i = 1; i <= x.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= y.Length; j++)
            {
                int cost = x[i - 1] == y[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[y.Length];
    }

    /// <summary>
    /// 标题单项淘汰线：候选标题与目标既不相等、不互相包含、相似度又 &lt;0.45，
    /// 基本不可能是同一首歌，打分阶段直接跳过（防相似度兜底把无关候选抬过门槛）。
    /// 任一为空时不淘汰（信息不足不误杀）。
    /// </summary>
    public static bool TitleEliminated(LyricCandidate candidate, string title)
    {
        string cand = HttpHelp.Norm(candidate.Title);
        string target = HttpHelp.Norm(title);
        if (cand.Length == 0 || target.Length == 0) return false;
        if (cand == target) return false;
        if (Math.Min(cand.Length, target.Length) >= 2 && (cand.Contains(target) || target.Contains(cand)))
            return false;
        return TitleSimilarity(candidate.Title, title) < 0.45;
    }

    public static int Score(LyricCandidate candidate, string title, string artist, long durationMs)
    {
        int score = 0;
        string candTitle = HttpHelp.Norm(candidate.Title);
        string targetTitle = HttpHelp.Norm(title);

        if (candTitle.Length > 0 && targetTitle.Length > 0)
        {
            if (candTitle == targetTitle) score += 50;
            else if (Math.Min(candTitle.Length, targetTitle.Length) >= 2 &&
                     (candTitle.Contains(targetTitle) || targetTitle.Contains(candTitle)))
                score += 30;
            else
            {
                // v0.6 相似度兜底：如「一千年以后」vs「一千年以后(Live)」已走包含分支；
                // 这里治的是差一两个字/异体写法的候选（如带不可见字符清洗后的近似名）。
                double sim = TitleSimilarity(candidate.Title, title);
                if (sim >= 0.6) score += (int)Math.Round(sim * 40);
            }
        }

        if (HttpHelp.ArtistMatches(candidate.Artist, artist)) score += 30;

        if (candidate.DurationMs > 0 && durationMs > 0)
        {
            long diff = Math.Abs(candidate.DurationMs - durationMs);
            if (diff <= 2_000) score += 20;
            else if (diff <= 5_000) score += 10;
            else if (diff > 10_000) score -= 20;
        }

        if (candidate.WordLevel) score += 15;

        foreach (string kw in EditionKeywords)
        {
            if (candTitle.Contains(kw) && !targetTitle.Contains(kw))
            {
                score -= 25;
                break;
            }
        }
        return score;
    }
}
