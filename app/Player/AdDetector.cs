using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Player;

/// <summary>
/// 广告识别（启发式）。Spotify 免费版广告经 SMTC 上报时，曲目信息有明显特征。
/// 判定原则：**保守，宁漏勿误杀**——有专辑名的永不判；时长 >90 秒永不判
/// （规则 1 除外：标题明写 Advertisement 是显式信号，不受时长保护线限制）。
/// v0.3 增加组合规则（专辑空 + 歌手空/时长短 + 无封面），扩大对「信息残缺型」广告的覆盖。
/// 判定全过程有诊断日志兜底（见 Program 的 SMTC 日志），规则可按真实日志回调。
/// </summary>
public static class AdDetector
{
    public readonly record struct Verdict(bool IsAd, string Rule)
    {
        public static readonly Verdict NotAd = new(false, "");
    }

    public static Verdict Check(TrackInfo track)
    {
        if (track == null) return Verdict.NotAd;

        // 规则 1（强信号）：标题就是 Advertisement（去空格、忽略大小写，等值或包含）
        string squashed = track.Title.Replace(" ", "").Replace("\u3000", "");
        if (squashed.Length > 0 &&
            (string.Equals(squashed, "Advertisement", StringComparison.OrdinalIgnoreCase) ||
             squashed.Contains("Advertisement", StringComparison.OrdinalIgnoreCase)))
            return new Verdict(true, "标题含 Advertisement");

        // 保护线：时长 >90 秒永不判广告（纯音乐/播客片段常信息不全，不能误杀）
        if (track.DurationMs > 90_000) return Verdict.NotAd;

        bool noArtist = string.IsNullOrWhiteSpace(track.Artist);
        bool noAlbum = string.IsNullOrWhiteSpace(track.Album);

        // 保护线：有专辑名的永不判
        if (!noAlbum) return Verdict.NotAd;

        // 规则 2（中信号）：歌手与专辑全空，且时长明确且 ≤90 秒（广告常见形态）。
        if (noArtist && track.DurationMs > 0)
            return new Verdict(true, $"歌手/专辑为空且时长 {track.DurationMs / 1000}s ≤90s");

        // 规则 3（v0.3 组合信号）：专辑为空 且（歌手为空 或 时长≤35 秒）且 无封面图。
        // 覆盖时长未知但歌手也缺失的残缺上报；有封面图的通常是真曲目，不判。
        if (!track.HasCover && (noArtist || track.DurationMs is > 0 and <= 35_000))
            return new Verdict(true,
                $"组合信号：专辑为空、无封面、{(noArtist ? "歌手为空" : $"时长 {track.DurationMs / 1000}s ≤35s")}");

        return Verdict.NotAd;
    }
}
