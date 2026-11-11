using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Player;

/// <summary>
/// 广告「连续段」状态机（v0.7，治 Sun 实案：Spotify 广告连播 3 段、只有第一段能识别）。
///
/// 排查结论（实锤/推断分开）：
/// - 实锤：Program 的轮询对每段曲目都无条件跑 AdDetector.Check，没有「判过就跳过」的
///   状态短路；检测层不存在只判第一段的代码路径。另有一处实锤显示缺陷——旧逻辑只在
///   _isAd 由假变真时写 _adKey，第二段起（Key 不同）tick 的广告提示判定对不上号，
///   该修已在 Program 侧完成（每段都更新 _adKey 并逐段记日志）。
/// - 推断：后两段识别不出，是**单段信号变弱**——AdDetector 的强规则需要「标题
///   Advertisement / 域名 / 专辑空+≤35s」等硬信号；连播的后续段 SMTC 元数据常不全
///   （专辑空但时长 40s 出头、或时长上报为 0、或只有关键词擦边），逐条都差一口气。
///
/// 修法（广告段/pod 语义）：Spotify 免费版广告是成组连播的——第一段被强规则实锤后，
/// 接下来短时间内出现的「弱信号短曲目」按同一广告段处理：
/// - 强判定命中 → 打开/刷新 pod（记最后命中时刻）；
/// - pod 窗口（120s）内，弱规则任一成立即判广告（仅 pod 内有效，窗口外绝不单独成立）：
///   W1 专辑为空 且 时长 1~45s（比规则 A 的 35s 放宽一档）；
///   W2 时长未知（=0）且 歌手、专辑都为空（后续段元数据缺失的典型形态）；
///   W3 广告关键词单独命中（规则 K 里值 2 分、单独不够线的，这里补上时长/情境分）；
/// - 破窗：出现时长 >90s 的曲目（真歌/长内容）立刻关 pod 并按原判定；
///   「这不是广告」抑制名单内的曲目永远不判（人工纠偏优先，pod 也不例外）。
/// 本类纯状态+纯逻辑（时间由调用方传入），供无头测试模拟三连段。
/// </summary>
public sealed class AdPodTracker
{
    /// <summary>pod 窗口：第一段实锤后多久内的弱信号段仍按广告处理。</summary>
    public const long PodWindowMs = 120_000;

    private long _lastAdAtMs = long.MinValue / 2;

    /// <summary>无曲目/会话消失时清状态。</summary>
    public void Reset() => _lastAdAtMs = long.MinValue / 2;

    /// <summary>pod 当前是否开着（供日志标注）。</summary>
    public bool IsPodActive(long nowMs) =>
        _lastAdAtMs != long.MinValue / 2 && nowMs - _lastAdAtMs <= PodWindowMs;

    /// <summary>
    /// 输入 AdDetector 的单段判定，输出结合 pod 情境的最终判定。
    /// 强判定命中会刷新 pod 计时；弱规则判出的广告也刷新（连播可以超过单个窗口时长）。
    /// </summary>
    public AdDetector.Verdict Observe(TrackInfo track, AdDetector.Verdict detectorVerdict,
        long nowMs, IReadOnlyCollection<string>? notAdKeys)
    {
        if (track == null) return AdDetector.Verdict.NotAd;

        // 人工纠偏优先于一切（包括 pod）：名单内的曲目永不判。
        if (notAdKeys != null && notAdKeys.Contains(track.Key))
            return AdDetector.Verdict.NotAd;

        // 破窗：长内容出现，广告段结束（>90s 与 AdDetector 保护线同口径）。
        if (track.DurationMs > 90_000)
        {
            _lastAdAtMs = long.MinValue / 2;
            return detectorVerdict; // 此分支 detectorVerdict 必为 NotAd（保护线），原样返回
        }

        if (detectorVerdict.IsAd)
        {
            _lastAdAtMs = nowMs;
            return detectorVerdict;
        }

        if (!IsPodActive(nowMs)) return detectorVerdict;

        bool noAlbum = string.IsNullOrWhiteSpace(track.Album);
        bool noArtist = string.IsNullOrWhiteSpace(track.Artist);

        // W1：专辑空 + 时长 1~45s（规则 A 的 35s 在连播段里放宽到 45s）
        if (noAlbum && track.DurationMs is > 0 and <= 45_000)
        {
            _lastAdAtMs = nowMs;
            return new AdDetector.Verdict(true,
                $"广告连续段 W1：专辑为空且时长 {track.DurationMs / 1000}s ≤45s（pod 内）");
        }

        // W2：时长未知 + 歌手/专辑全空（连播后续段 SMTC 时间线不刷新的典型形态）
        if (track.DurationMs <= 0 && noAlbum && noArtist)
        {
            _lastAdAtMs = nowMs;
            return new AdDetector.Verdict(true,
                "广告连续段 W2：时长未知且歌手/专辑为空（pod 内）");
        }

        // W3：关键词单独命中（在规则 K 里只有 2 分不够线，pod 情境补足）
        string combined = $"{track.Title} {track.Artist} {track.Album}".ToLowerInvariant();
        var hits = AdDetector.KeywordHits(combined);
        if (hits.Count > 0)
        {
            _lastAdAtMs = nowMs;
            return new AdDetector.Verdict(true,
                $"广告连续段 W3：关键词「{hits[0]}」命中（pod 内补足情境分）");
        }

        return detectorVerdict;
    }
}
