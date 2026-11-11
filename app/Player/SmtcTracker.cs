using SpotifyLyricsWin.Core;
using Windows.Media.Control;

namespace SpotifyLyricsWin.Player;

/// <summary>
/// 通过 Windows SMTC（GlobalSystemMediaTransportControls）跟踪播放。
/// 优先选 SourceAppUserModelId 含 "Spotify" 的会话；没有时用系统当前会话兜底。
///
/// 进度（v0.5 重做）：SMTC 时间线更新稀疏，样本一律喂给 ProgressClock.Observe 消化——
/// 样本位置对应时间线 **LastUpdatedTime** 时刻（换算到单调时钟域），不是轮询收到时刻；
/// 死区丢弃样本 + 到阈值硬跳的旧逻辑已废除（逐字「跑快了回退」的元凶）。
/// 两次轮询之间 tick（40ms）读 ProgressClock.PositionMs 平滑外推。
/// TrackInfo 只承载曲目元数据（标题/歌手/专辑/时长/封面），其 ProgressMs 为最近样本原值。
/// </summary>
public sealed class SmtcTracker
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private TrackInfo? _current;
    private bool _polling;
    private string _clockKey = "";
    private long _lastClockLogMs;

    private readonly ProgressClock _clock = new(() => TrackInfo.NowMs());

    public TrackInfo? Current => _current;
    public bool HasSession => _session != null;

    /// <summary>
    /// v0.6 诊断：最近一次样本的 PlaybackInfo.Controls 可用动作摘要
    /// （能否切歌/快进快退/拖动）。这是 LyricFlow「广告无切歌钮」信号在 SMTC 的对应物——
    /// 本版只随 SMTC 日志行记录、**不参与任何判定**，为下一轮广告规则调校攒真实数据。
    /// </summary>
    public string LastControlsSummary { get; private set; } = "";

    /// <summary>轮询一次（约每 500ms 调一次）。每个样本必过 ProgressClock.Observe。</summary>
    public async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = _manager.GetSessions();
            GlobalSystemMediaTransportControlsSession? picked = null;
            foreach (var s in sessions)
            {
                if (s.SourceAppUserModelId?.IndexOf("Spotify", StringComparison.OrdinalIgnoreCase) >= 0)
                { picked = s; break; }
            }
            picked ??= _manager.GetCurrentSession();
            _session = picked;
            if (picked == null) { SetNoTrack(); return; }

            var props = await picked.TryGetMediaPropertiesAsync();
            if (props == null || string.IsNullOrWhiteSpace(props.Title)) { SetNoTrack(); return; }

            var timeline = picked.GetTimelineProperties();
            var playback = picked.GetPlaybackInfo();
            bool isPlaying = playback?.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // v0.6：记录 Controls 可用动作（只记录不判定，见 LastControlsSummary 说明）
            try
            {
                var c = playback?.Controls;
                LastControlsSummary = c == null ? "Controls=无"
                    : $"可切歌={c.IsNextEnabled} 可上一首={c.IsPreviousEnabled} " +
                      $"可快进={c.IsFastForwardEnabled} 可快退={c.IsRewindEnabled} 可拖动={c.IsPlaybackPositionEnabled}";
            }
            catch { LastControlsSummary = "Controls=读取失败"; }

            long startMs = (long)timeline.StartTime.TotalMilliseconds;
            long endMs = (long)timeline.EndTime.TotalMilliseconds;
            long posMs = (long)timeline.Position.TotalMilliseconds - startMs;
            long durationMs = Math.Max(0, endMs - startMs);
            if (posMs < 0) posMs = 0;

            // 样本时刻 = 时间线 LastUpdatedTime（位置在那一刻的值）。
            // 换算到 Stopwatch 单调时钟域：now单调 − (现实时间 − LastUpdatedTime)。
            // 读不到/异常（默认最小值、晚于现在）时退回轮询时刻。
            long nowMs = TrackInfo.NowMs();
            long sampleAtMs = nowMs;
            try
            {
                var lut = timeline.LastUpdatedTime;
                if (lut > DateTimeOffset.MinValue)
                {
                    double ageMs = (DateTimeOffset.Now - lut).TotalMilliseconds;
                    if (ageMs >= -1000) sampleAtMs = nowMs - (long)Math.Max(0, ageMs);
                }
            }
            catch { /* 个别会话 LastUpdatedTime 异常：用轮询时刻兜底 */ }

            _current = new TrackInfo
            {
                Title = props.Title,
                Artist = props.Artist ?? "",
                Album = props.AlbumTitle ?? "",
                DurationMs = durationMs,
                ProgressMs = posMs,
                IsPlaying = isPlaying,
                // 封面图有无仍记录进日志备查（v0.5 广告规则已不依赖它）
                HasCover = props.Thumbnail != null,
                UpdatedAtMs = nowMs
            };

            // 切歌（key 变化）先重置时钟，再喂样本
            if (_current.Key != _clockKey)
            {
                _clockKey = _current.Key;
                _clock.Reset();
            }
            var action = _clock.Observe(posMs, sampleAtMs, isPlaying, durationMs);

            // 诊断日志（限频 10 秒一行）：补偿/硬跳事件留痕，便于用户反馈时对账
            if ((action == ClockAction.Compensating || action == ClockAction.SeekSnap) &&
                nowMs - _lastClockLogMs >= 10_000)
            {
                _lastClockLogMs = nowMs;
                AppLog.Write(action == ClockAction.SeekSnap
                    ? $"[进度时钟] 偏差 {_clock.LastDeviationMs}ms 超阈值，按 seek 硬跳校正"
                    : $"[进度时钟] 偏差 {_clock.LastDeviationMs}ms，速率补偿收敛中（×{_clock.CurrentRate:F2}）");
            }
        }
        catch
        {
            // SMTC 偶发异常（如会话刚关闭）时保留上次状态，下一轮再试
        }
        finally { _polling = false; }
    }

    private void SetNoTrack()
    {
        _current = null;
        if (_clockKey.Length > 0)
        {
            _clockKey = "";
            _clock.Reset();
        }
    }

    /// <summary>当前平滑显示进度（毫秒），无曲目/无样本返回 -1。tick 驱动扫光用。</summary>
    public long CurrentProgressMs()
    {
        if (_current == null) return -1;
        return _clock.PositionMs();
    }
}
