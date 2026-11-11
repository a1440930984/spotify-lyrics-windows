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
/// 现行规则（判定原则仍是宁漏勿误杀，保护线见各条）：
///   规则 1：标题含 Advertisement（显式信号，不受任何保护线限制，同旧版）；
///   规则 B：歌手/标题含域名样式（*.com/*.net 等或 www.）→ 广告
///           （不受「专辑非空」保护——品牌域名出现在真实歌手名里几乎不可能；
///            受时长保护：>90 秒不判）；
///   规则 A：专辑为空 且 时长在 1~35 秒 → 广告（不看封面）；
///   规则 A2（沿用旧规则 2）：专辑、歌手全空 且 时长 ≤90 秒 → 广告。
/// 保护线：时长 >90 秒，除规则 1 外永不判；专辑非空，除规则 B 外永不判。
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

    public static Verdict Check(TrackInfo track)
    {
        if (track == null) return Verdict.NotAd;

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

        bool noArtist = string.IsNullOrWhiteSpace(track.Artist);
        bool noAlbum = string.IsNullOrWhiteSpace(track.Album);

        // 保护线：有专辑名的永不判（规则 B 除外，已在上面处理）
        if (!noAlbum) return Verdict.NotAd;

        // 规则 A（v0.5 主力）：专辑为空 且 时长 1~35 秒 → 广告。
        // 用户真实日志里 5 条漏检广告全部命中此条（10~30s、专辑空、有封面——不看封面）。
        if (track.DurationMs is > 0 and <= 35_000)
            return new Verdict(true, $"专辑为空且时长 {track.DurationMs / 1000}s ≤35s");

        // 规则 A2（旧规则 2 保留）：歌手与专辑全空，且时长明确且 ≤90 秒（保护线已保证）。
        if (noArtist && track.DurationMs > 0)
            return new Verdict(true, $"歌手/专辑为空且时长 {track.DurationMs / 1000}s ≤90s");

        return Verdict.NotAd;
    }

    /// <summary>字符串里是否含域名样式片段（供规则 B 与无头测试）。</summary>
    public static bool LooksLikeDomain(string? s) =>
        !string.IsNullOrWhiteSpace(s) && DomainPattern.IsMatch(s);
}
