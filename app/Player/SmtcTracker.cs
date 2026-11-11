using SpotifyLyricsWin.Core;
using Windows.Media.Control;

namespace SpotifyLyricsWin.Player;

/// <summary>
/// 通过 Windows SMTC（GlobalSystemMediaTransportControls）跟踪播放。
/// 优先选 SourceAppUserModelId 含 "Spotify" 的会话；没有时用系统当前会话兜底。
/// SMTC 的 Timeline 更新稀疏，这里每次轮询拿到 position 后存下基准，
/// 两次轮询之间由 TrackInfo 按流逝时间外推（暂停停表），40ms tick 驱动扫光。
/// </summary>
public sealed class SmtcTracker
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private TrackInfo? _current;
    private bool _polling;

    public TrackInfo? Current => _current;
    public bool HasSession => _session != null;

    /// <summary>轮询一次（约每 500ms 调一次）。会话/曲目/进度有变时重设外推基准。</summary>
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
            if (picked == null) { _current = null; return; }

            var props = await picked.TryGetMediaPropertiesAsync();
            if (props == null || string.IsNullOrWhiteSpace(props.Title)) { _current = null; return; }

            var timeline = picked.GetTimelineProperties();
            var playback = picked.GetPlaybackInfo();
            bool isPlaying = playback?.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            long startMs = (long)timeline.StartTime.TotalMilliseconds;
            long endMs = (long)timeline.EndTime.TotalMilliseconds;
            long posMs = (long)timeline.Position.TotalMilliseconds - startMs;
            long durationMs = Math.Max(0, endMs - startMs);
            if (posMs < 0) posMs = 0;

            var now = TrackInfo.NowMs();
            var prev = _current;
            bool sameTrack = prev != null && prev.Title == props.Title && prev.Artist == props.Artist;
            // 同一首歌且外推进度与 SMTC 报告相差 <1.5s 时保留旧基准，避免每轮询一次就抖一下
            if (sameTrack && prev!.IsPlaying == isPlaying &&
                Math.Abs(prev.CurrentProgress(now) - posMs) < 1500)
            {
                return;
            }

            _current = new TrackInfo
            {
                Title = props.Title,
                Artist = props.Artist ?? "",
                Album = props.AlbumTitle ?? "",
                DurationMs = durationMs,
                ProgressMs = posMs,
                IsPlaying = isPlaying,
                // v0.3：封面图有无是广告识别的组合信号（广告常无封面）；读不到引用即按无封面
                HasCover = props.Thumbnail != null,
                UpdatedAtMs = now
            };
        }
        catch
        {
            // SMTC 偶发异常（如会话刚关闭）时保留上次状态，下一轮再试
        }
        finally { _polling = false; }
    }

    /// <summary>当前外推进度（毫秒），无曲目返回 -1。</summary>
    public long CurrentProgressMs()
    {
        var t = _current;
        if (t == null) return -1;
        return t.CurrentProgress();
    }
}
