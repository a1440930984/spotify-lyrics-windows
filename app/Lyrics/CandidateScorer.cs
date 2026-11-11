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
/// </summary>
public static class CandidateScorer
{
    private static readonly string[] EditionKeywords = { "live", "翻唱", "伴奏", "铃声", "cover" };

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
