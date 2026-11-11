using System.Windows;
using System.Windows.Threading;
using SpotifyLyricsWin.Core;
using SpotifyLyricsWin.Lyrics;
using SpotifyLyricsWin.Player;
using SpotifyLyricsWin.Ui;

namespace SpotifyLyricsWin;

/// <summary>
/// 入口与编排（等价安卓 LyricsForegroundService 的调度角色）：
/// SMTC 轮询（500ms）发现曲目 → 切歌自动取词 → 40ms tick 按外推进度驱动扫光。
/// </summary>
public static class Program
{
    private static Settings _settings = null!;
    private static SmtcTracker _tracker = null!;
    private static LyricsService _service = null!;
    private static LyricsSyncEngine _engine = null!;
    private static FloatingWindow _floating = null!;
    private static Tray _tray = null!;
    private static SettingsWindow? _settingsWindow;

    private static string _fetchingKey = "";
    private static string _loadedKey = "";
    private static CancellationTokenSource? _fetchCts;
    private static string _statusHint = "♪ 等待 Spotify 播放";
    private static bool _notFound;
    private static ManualLyricsWindow? _manualWindow;
    private static bool _isAd;
    private static string _adKey = "";
    private static string _lastLogSignature = "\0";

    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // v0.3：启动先落一行日志，保证 log.txt 必然存在（v0.2 只在广告命中时写，
        // 从未命中就没文件，用户找不到）。路径：%AppData%/SpotifyLyricsWin/log.txt
        AppLog.EnsureFile();
        AppLog.Write("[启动] SpotifyLyricsWin v0.3 启动");

        _settings = Settings.Load();
        _tracker = new SmtcTracker();
        _service = new LyricsService(_settings);
        _engine = new LyricsSyncEngine();
        _floating = new FloatingWindow(_settings);
        _floating.CloseRequested += () =>
        {
            _settings.FloatingEnabled = false;
            _settings.Save();
            _floating.Hide();
        };
        _floating.SettingsRequested += OpenSettings;
        _floating.ManualPickRequested += OpenManualPicker;
        _floating.RefetchRequested += () =>
        {
            var t = _tracker.Current;
            if (t != null && !_isAd) StartFetch(t, skipCache: true);
        };
        if (_settings.FloatingEnabled) _floating.Show();
        _floating.SetHint(_statusHint);
        _floating.SetDimmed(true);

        _tray = new Tray();
        _tray.ToggleFloating += () =>
        {
            _settings.FloatingEnabled = !_settings.FloatingEnabled;
            _settings.Save();
            if (_settings.FloatingEnabled) _floating.Show(); else _floating.Hide();
        };
        _tray.OpenSettings += OpenSettings;
        _tray.ManualPick += OpenManualPicker;
        _tray.RefetchLyrics += () =>
        {
            var t = _tracker.Current;
            if (t != null && !_isAd) StartFetch(t, skipCache: true);
        };
        _tray.OpenLog += OpenLogFile;
        _tray.ExitApp += () =>
        {
            _tray.Dispose();
            app.Shutdown();
        };

        // SMTC 轮询：500ms
        var pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        pollTimer.Tick += async (_, _) =>
        {
            await _tracker.PollAsync();
            var track = _tracker.Current;
            LogSmtcState(track);
            if (track == null)
            {
                _loadedKey = "";
                _notFound = false;
                _isAd = false;
                _adKey = "";
                _statusHint = "♪ 等待 Spotify 播放";
                return;
            }

            // 广告识别（启发式）：命中则不取词、不推进，tick 里显示广告提示；结束自动恢复
            var verdict = AdDetector.Check(track);
            if (verdict.IsAd)
            {
                if (!_isAd)
                {
                    _isAd = true;
                    _adKey = track.Key;
                    AppLog.Write($"[广告识别] 标题=「{track.Title}」 歌手=「{track.Artist}」 " +
                        $"专辑=「{track.Album}」 时长={track.DurationMs}ms 命中规则：{verdict.Rule}");
                    _tray.SetStatus("SpotifyLyrics - 广告播放中");
                }
                return;
            }
            if (_isAd)
            {
                _isAd = false;
                _adKey = "";
                AppLog.Write("[广告识别] 广告结束，恢复歌词同步");
            }

            if (track.Key != _loadedKey && track.Key != _fetchingKey)
                StartFetch(track, skipCache: false);
        };
        pollTimer.Start();

        // 渲染 tick：约 40ms 一帧（照规格），只推进渐变/切行，不重排版
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        tick.Tick += (_, _) => OnTick();
        tick.Start();

        app.Run();
    }

    private static void OpenSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings);
        // v0.2：设置实时生效——每次变更立刻重套悬浮窗样式（落盘由设置窗内的防抖处理）
        _settingsWindow.LiveChanged += () => _floating.ApplyStyle();
        _settingsWindow.OpenLogRequested += OpenLogFile;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    /// <summary>用记事本打开诊断日志（文件必然存在：启动时已 EnsureFile）。</summary>
    private static void OpenLogFile()
    {
        try
        {
            AppLog.EnsureFile();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{AppLog.LogPath}\"",
                UseShellExecute = true
            });
        }
        catch { /* 打开失败不崩溃 */ }
    }

    /// <summary>
    /// v0.3 全量诊断日志：SMTC 状态签名（曲目/专辑/时长/封面/播放态/判定）一变就记一行，
    /// 不再只记广告命中。广告没识别出来时，用户把这几行发回来就能看清 SMTC 到底给了什么。
    /// </summary>
    private static void LogSmtcState(TrackInfo? track)
    {
        string signature, line;
        if (track == null)
        {
            signature = "<none>";
            line = "[SMTC] 无曲目（会话消失或未在播放） 判定=未定";
        }
        else
        {
            var v = AdDetector.Check(track);
            signature = $"{track.Key}|{track.Album}|{track.DurationMs}|{track.HasCover}|{track.IsPlaying}|{v.IsAd}";
            line = $"[SMTC] 标题=「{track.Title}」 歌手=「{track.Artist}」 专辑=「{track.Album}」 " +
                   $"时长={(track.DurationMs > 0 ? (track.DurationMs / 1000) + "s" : "未知")} " +
                   $"封面={(track.HasCover ? "有" : "无")} 状态={(track.IsPlaying ? "播放" : "暂停")} " +
                   $"判定={(v.IsAd ? $"广告（{v.Rule}）" : "歌曲")}";
        }
        if (signature == _lastLogSignature) return;
        _lastLogSignature = signature;
        AppLog.Write(line);
    }

    private static void OpenManualPicker()
    {
        if (_manualWindow != null) { _manualWindow.Activate(); return; }
        var t = _tracker.Current;
        _manualWindow = new ManualLyricsWindow(t?.Title ?? "", t?.Artist ?? "");
        _manualWindow.SearchAsync = (title, artist, ct) =>
            _service.SearchCandidatesAsync(title, artist, ct);
        _manualWindow.ApplyAsync = async candidate =>
        {
            var track = _tracker.Current;
            if (track == null || _isAd) return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var outcome = await _service.FetchCandidateAsync(candidate, track, cts.Token);
            if (outcome == null) return false;
            _engine.Lyrics = outcome.Lyrics;
            _loadedKey = track.Key;
            _notFound = false;
            _tray.SetStatus($"SpotifyLyrics - 歌词：{outcome.SourceName}（手动）｜{track.Title}");
            return true;
        };
        _manualWindow.Closed += (_, _) => _manualWindow = null;
        _manualWindow.Show();
    }

    private static void StartFetch(TrackInfo track, bool skipCache)
    {
        _fetchCts?.Cancel();
        _fetchCts = new CancellationTokenSource();
        var ct = _fetchCts.Token;
        _fetchingKey = track.Key;
        _notFound = false;
        _statusHint = "♪ 正在找歌词…";
        _tray.SetStatus($"SpotifyLyrics - 正在找歌词：{track.Title}");
        string key = track.Key;
        _ = Task.Run(async () =>
        {
            FetchOutcome? outcome = null;
            try { outcome = await _service.FetchAsync(track, skipCache, ct); }
            catch (OperationCanceledException) { return; }
            catch { /* 服务内已兜底，这里再防一层 */ }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (ct.IsCancellationRequested) return;
                if (_tracker.Current?.Key != key) return; // 已经切歌了
                _fetchingKey = "";
                if (outcome != null)
                {
                    _engine.Lyrics = outcome.Lyrics;
                    _loadedKey = key;
                    _tray.SetStatus($"SpotifyLyrics - 歌词：{outcome.SourceName}｜{track.Title}");
                    AppLog.Write($"[取词] 「{track.Title}」 命中 {outcome.SourceName}" +
                        (outcome.Score >= 0 ? $" 打分={outcome.Score} 候选=「{outcome.CandidateTitle}」" : "（顺序回退）") +
                        $" 逐字={outcome.Lyrics.IsWordLevel} 行数={outcome.Lyrics.Lines.Count}");
                }
                else
                {
                    _engine.Lyrics = null;
                    _loadedKey = key; // 记住已试过，避免每 500ms 重试拖死接口；手动「重新搜词」可再来
                    _notFound = true;
                    _tray.SetStatus($"SpotifyLyrics - 未找到歌词：{track.Title}");
                    AppLog.Write($"[取词] 「{track.Title}」 所有源均未命中（可手动选词）");
                }
            });
        }, ct);
    }

    private static void OnTick()
    {
        var track = _tracker.Current;
        if (track == null)
        {
            _floating.SetDimmed(true);
            if (_settings.FloatingEnabled) _floating.SetHint(_statusHint);
            return;
        }

        // 广告中：停止歌词同步推进，整块变淡并显示提示（照规格的广告变淡行为）
        if (_isAd && track.Key == _adKey)
        {
            _floating.SetDimmed(true);
            if (_settings.FloatingEnabled) _floating.SetHint("广告播放中 · 歌词马上回来");
            return;
        }

        _floating.SetDimmed(!track.IsPlaying);
        long progress = track.CurrentProgress() + _settings.LyricsOffsetMs;
        if (progress < 0) progress = 0;

        var lyrics = _engine.Lyrics;
        if (lyrics == null || _loadedKey != track.Key)
        {
            _floating.SetHint(_notFound && _loadedKey == track.Key ? "未找到歌词" : "♪ 正在找歌词…");
            return;
        }

        var render = _engine.RenderAt(progress, track.DurationMs);
        string translation = render.CurrentIndex >= 0 ? lyrics.TranslationAt(render.CurrentIndex) : "";
        _floating.SetLine(render.Current, render.Next, translation);
        _floating.SetProgress(render.ProgressFraction,
            _settings.WordByWordEnabled && render.Current.Length > 0);
    }
}
