using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Player;

/// <summary>
/// 广告识别（启发式，v0.2 新增）。Spotify 免费版广告经 SMTC 上报时，
/// 曲目信息有明显特征。判定原则：**保守，宁漏勿误杀**——尤其纯音乐/播客
/// 片段不能因为信息少就误判。个别把自己伪装成正常歌曲的广告识别不了（README 已注明）。
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

        // 规则 2（中信号）：歌手与专辑全空，且时长明确且 ≤90 秒（广告常见形态）。
        // 时长 >90 秒或未知（0）一律不判——保护纯音乐/播客片段不被误杀。
        bool noArtist = string.IsNullOrWhiteSpace(track.Artist);
        bool noAlbum = string.IsNullOrWhiteSpace(track.Album);
        if (noArtist && noAlbum && track.DurationMs > 0 && track.DurationMs <= 90_000)
            return new Verdict(true, $"歌手/专辑为空且时长 {track.DurationMs / 1000}s ≤90s");

        return Verdict.NotAd;
    }
}
