using System.Text.RegularExpressions;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Player;

/// <summary>
/// 广告识别（启发式）。Spotify 免费版广告经 SMTC 上报时，曲目信息有明显特征。
///
/// v0.5 按用户真机日志（v0.3 全部漏检的真实广告）重写。真实样本的共同点：
///   广告：专辑**全部为空**、时长 10~30 秒、封面其实**有**（T-Mobile 10s /
///         Instacart 30s / HepatitisBTesting.com 15~30s / McDonald's 15s / Lowe's 30s）；
///   歌曲：专辑全部非空、时长 227~272 秒。
/// v0.3 规则要求「无封面」正是误杀主因（广告有封面），已去除封面条件。
///
/// v0.6 再并入两家安卓实现的关键词体系（出处分开标注，参数不可混抄）：
///   - 关键词表 = V2.4 原理文档 §1.4 的 KEYWORDS（advertisement/advert/sponsored/commercial/
///     广告/music will resume/music resumes/spotify premium/listen ad-free）∪
///     LyricFlow AdDetector.kt 的 WORDS（赞助/推广/广告时间/upgrade to premium 等）；
///   - 判定结构取 LyricFlow 的**加权计分**：关键词命中 +2、短时长（1~45s）+1，
///     合计 ≥3 才判（规则 K）。单独一个关键词（2 分）不判——留给规则 1 的
///     「标题含 advertisement」这个最强信号单独成立（沿用旧版，且与表内同词一致）。
/// 现行规则全表：
///   规则 0：曲目 Key 在用户「这不是广告」抑制名单内 → 永不判（人工纠偏优先于一切规则）；
///   规则 1：标题含 Advertisement（强信号，不受任何保护线限制）；
///   规则 B：歌手/标题含域名样式（*.com/*.net 等或 www.）→ 广告
///           （不受「专辑非空」保护；受时长保护：>90 秒不判）；
///   规则 K：关键词加权（命中 +2、时长 1~45s +1，≥3）→ 广告（受 >90s 保护线）；
///   规则 A：专辑为空 且 时长在 1~35 秒 → 广告（不看封面）；
///   规则 A2（沿用旧规则 2）：专辑、歌手全空 且 时长 ≤90 秒 → 广告。
/// 保护线：时长 >90 秒，除规则 1 外永不判；专辑非空，除规则 B/K 外永不判
/// （K 要判有专辑的曲目，必须同时有关键词+短时长两个实证信号）。
/// 判定全过程有诊断日志兜底（见 Program 的 SMTC 日志），规则可按真实日志回调。
/// </summary>
public static class AdDetector
{
    public readonly record struct Verdict(bool IsAd, string Rule)
    {
        public static readonly Verdict NotAd = new(false, "");
    }

    // 域名样式：token 里带已知 TLD 后缀（HepatitisBTesting.com 这类），或 www. 开头。
    // TLD 列表刻意收紧，避免误伤 "will.i.am"、"Mr. Probz" 这类带点的真实艺人名。
    private static readonly Regex DomainPattern = new(
        @"(^|[\s/(@])(www\.|[a-z0-9][a-z0-9\-]*\.(com|net|org|io|co|tv|info|biz|xyz|online|site|app|dev)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 广告关键词表（v0.6）：V2.4 原理文档 §1.4 的 KEYWORDS 与 LyricFlow AdDetector.kt
    /// 的 WORDS 并集（去重）。匹配面 = 标题+歌手+专辑合并文本（SMTC 没有 mediaId/通知文案）。
    /// 已知误伤面：真实作品名里含「广告/推广」等词时会被规则 K 计 2 分，
    /// 但还需短时长凑满 3 分才判，且用户可用「这不是广告」一键纠偏并永久抑制该曲。
    /// </summary>
    public static readonly string[] Keywords =
    {
        "advertisement", "advert", "sponsored", "commercial", "广告",
        "music will resume", "music resumes", "spotify premium", "listen ad-free",
        "赞助", "推广", "upgrade to premium", "广告时间"
    };

    /// <summary>在合并文本里命中的关键词（供规则 K 计分与日志/测试）。</summary>
    public static List<string> KeywordHits(string? combinedLower)
    {
        var hits = new List<string>();
        if (string.IsNullOrEmpty(combinedLower)) return hits;
        foreach (string kw in Keywords)
            if (combinedLower.Contains(kw)) hits.Add(kw);
        return hits;
    }

    public static Verdict Check(TrackInfo track) => Check(track, null);

    /// <summary>带「这不是广告」抑制名单的判定（名单内的曲目 Key 永不判广告）。</summary>
    public static Verdict Check(TrackInfo track, IReadOnlyCollection<string>? notAdKeys)
    {
        if (track == null) return Verdict.NotAd;

        // 规则 0（v0.6）：用户已人工纠偏过这首——尊重用户，优先于一切自动规则。
        if (notAdKeys != null && notAdKeys.Contains(track.Key)) return Verdict.NotAd;

        // 规则 1（强信号）：标题就是 Advertisement（去空格、忽略大小写，等值或包含）
        string squashed = track.Title.Replace(" ", "").Replace("\u3000", "");
        if (squashed.Length > 0 &&
            squashed.Contains("Advertisement", StringComparison.OrdinalIgnoreCase))
            return new Verdict(true, "标题含 Advertisement");

        // 保护线：时长 >90 秒永不判广告（规则 1 除外，已在上面处理）。
        // 纯音乐/播客片段常信息不全，不能误杀。
        if (track.DurationMs > 90_000) return Verdict.NotAd;

        // 规则 B（v0.5，强信号）：歌手或标题是域名样式——广告主把网址当歌手名上报。
        // 不受专辑保护：真实歌手名里出现 .com 之类后缀几乎不可能。
        if (LooksLikeDomain(track.Artist) || LooksLikeDomain(track.Title))
            return new Verdict(true, "歌手/标题含域名样式（疑似广告主网址）");

        // 规则 K（v0.6，加权计分，蓝本 LyricFlow AdDetector 的「关键词 +2、短时长 +1、≥3 判」）：
        // 关键词单独出现只有 2 分不判（防真实作品名带词误杀），必须再有短时长信号凑满。
        string combined = $"{track.Title} {track.Artist} {track.Album}".ToLowerInvariant();
        var hits = KeywordHits(combined);
        if (hits.Count > 0)
        {
            int score = 2;
            bool shortDur = track.DurationMs is > 0 and <= 45_000;
            if (shortDur) score += 1;
            if (score >= 3)
                return new Verdict(true,
                    $"关键词命中「{hits[0]}」(+2) + 短时长 {track.DurationMs / 1000}s (+1) ≥3");
        }

        bool noAlbum = string.IsNullOrWhiteSpace(track.Album);

        // 保护线：有专辑名的永不判（规则 B、K 除外，已在上面处理）
        if (!noAlbum) return Verdict.NotAd;

        // 规则 A（v0.5 主力）：专辑为空 且 时长 1~35 秒 → 广告。
        // 用户真实日志里 5 条漏检广告全部命中此条（10~30s、专辑空、有封面——不看封面）。
        if (track.DurationMs is > 0 and <= 35_000)
            return new Verdict(true, $"专辑为空且时长 {track.DurationMs / 1000}s ≤35s");

        // 规则 A2（旧规则 2 保留）：歌手与专辑全空，且时长明确且 ≤90 秒（保护线已保证）。
        bool noArtist = string.IsNullOrWhiteSpace(track.Artist);
        if (noArtist && track.DurationMs > 0)
            return new Verdict(true, $"歌手/专辑为空且时长 {track.DurationMs / 1000}s ≤90s");

        return Verdict.NotAd;
    }

    /// <summary>字符串里是否含域名样式片段（供规则 B 与无头测试）。</summary>
    public static bool LooksLikeDomain(string? s) =>
        !string.IsNullOrWhiteSpace(s) && DomainPattern.IsMatch(s);
}
