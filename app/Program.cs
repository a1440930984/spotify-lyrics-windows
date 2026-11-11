using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SpotifyLyricsWin.Core;
using SpotifyLyricsWin.Lyrics;
using SpotifyLyricsWin.Player;
using SpotifyLyricsWin.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

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

    // v0.6 新增状态：单实例锁、纯音乐、网络失败延迟重试
    private static Mutex? _singleInstanceMutex;
    private static bool _isInstrumental;
    private static string _instrumentalKey = "";
    private static string _pendingRetryKey = "";
    private static long _pendingRetryAtTick;

    // v0.4 自动更新
    private static Application _app = null!;
    private static ReleaseInfo? _pendingRelease;
    private static string? _startupUpdateNote;

    [STAThread]
    public static void Main()
    {
        // v0.6 单实例：已有实例在跑时新进程直接退出（只记一行日志，不做 IPC 唤起——
        // 托盘图标一直都在，用户点托盘即可，不值得为唤起引入管道复杂度）。
        _singleInstanceMutex = new Mutex(initiallyOwned: true,
            @"Local\SpotifyLyricsWin-SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            AppLog.EnsureFile();
            AppLog.Write("[启动] 已有实例在运行，本进程退出（单实例）");
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _app = app;

        // v0.6 全局异常兜底（治本项）：用户反馈「手动搜词后整程序退出、日志里什么都没有」——
        // 此前没有任何全局捕获，未观察的任务异常/模板应用期异常会直接杀进程且不留痕。
        // 三路全注册，统一落 AppLog（完整类型+消息+堆栈）；UI 线程异常标记 Handled 尽量保命，
        // 实在保不住的也先把日志落盘再退。
        RegisterGlobalExceptionHandlers(app);

        // v0.3：启动先落一行日志，保证 log.txt 必然存在（v0.2 只在广告命中时写，
        // 从未命中就没文件，用户找不到）。路径：%AppData%/SpotifyLyricsWin/log.txt
        AppLog.EnsureFile();
        AppLog.Write("[启动] SpotifyLyricsWin v0.6 启动");

        // v0.4：先看上次自动更新的结果（Updater 写的 update-result.json），汇报一次后删掉
        var lastResult = UpdateService.ReadResult();
        if (lastResult != null)
        {
            _startupUpdateNote = lastResult.Ok
                ? $"已更新到 {lastResult.To}（{lastResult.Time} 完成）"
                : $"上次更新没成功：{lastResult.Error}（详情看更新日志 {UpdateService.UpdaterLogPath}）";
            AppLog.Write($"[自动更新] 上次结果：ok={lastResult.Ok} {lastResult.From} → {lastResult.To} {lastResult.Error}");
            UpdateService.DeleteResult();
        }

        _settings = Settings.Load();
        // v0.6 多主题：先把调色板切到用户选的那套，之后所有窗口构造时读到的都是它
        Theme.Apply(_settings.UiTheme);
        // v0.6 Fluent 外壳（WPF-UI）：合并库的主题/控件资源字典并套当前主题，
        // 必须赶在设置窗/选词窗创建之前（它们构造时就靠这些资源渲染）。
        InitFluentUi();
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
            if (t != null && !_isAd && !_isInstrumental) StartFetch(t, skipCache: true, manual: true);
        };
        _floating.NotAdRequested += OnNotAd;
        _floating.ToggleInstrumentalRequested += OnToggleInstrumental;
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
            if (t != null && !_isAd && !_isInstrumental) StartFetch(t, skipCache: true, manual: true);
        };
        _tray.NotAd += OnNotAd;
        _tray.ToggleInstrumental += OnToggleInstrumental;
        _tray.OpenLog += OpenLogFile;
        _tray.BalloonClicked += OpenSettings;
        _tray.ExitApp += () =>
        {
            _tray.Dispose();
            app.Shutdown();
        };
        if (_startupUpdateNote != null)
            _tray.SetStatus("SpotifyLyrics - " + _startupUpdateNote);

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
                _isInstrumental = false;
                _instrumentalKey = "";
                _pendingRetryKey = "";
                _statusHint = "♪ 等待 Spotify 播放";
                SyncAdInstrumentalUi();
                return;
            }

            // 广告识别（启发式）：命中则不取词、不推进，tick 里显示广告提示；结束自动恢复。
            // v0.6：判定带上用户「这不是广告」抑制名单，名单内的曲目永不判广告。
            var verdict = AdDetector.Check(track, _settings.NotAdKeys);
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
                SyncAdInstrumentalUi();
                return;
            }
            if (_isAd)
            {
                _isAd = false;
                _adKey = "";
                AppLog.Write("[广告识别] 广告结束，恢复歌词同步");
            }

            // v0.6 纯音乐：已标记（手动或标题特征自动标记）→ 不取词，tick 显示「纯音乐」提示。
            bool marked = InstrumentalMarks.IsMarked(_settings, track.Key);
            if (!marked && InstrumentalMarks.TitleLooksInstrumental(track.Title))
            {
                InstrumentalMarks.Mark(_settings, track.Key);
                marked = true;
                AppLog.Write($"[纯音乐] 标题特征自动标记：「{track.Title} - {track.Artist}」，跳过取词");
            }
            if (marked)
            {
                if (!_isInstrumental || _instrumentalKey != track.Key)
                {
                    _isInstrumental = true;
                    _instrumentalKey = track.Key;
                    _fetchCts?.Cancel();
                    _fetchingKey = "";
                    _engine.Lyrics = null;
                    _loadedKey = track.Key; // 占位防重复触发；取消标记时由处理函数清掉
                    _notFound = false;
                    _tray.SetStatus($"SpotifyLyrics - 纯音乐：{track.Title}");
                }
                SyncAdInstrumentalUi();
                return;
            }
            if (_isInstrumental) { _isInstrumental = false; _instrumentalKey = ""; }

            SyncAdInstrumentalUi();

            // v0.6：网络型失败耗尽后的延迟重试（60 秒后自动再试一次，不再「失败即永久放弃」）
            if (_pendingRetryKey == track.Key && Environment.TickCount64 >= _pendingRetryAtTick
                && track.Key != _fetchingKey)
            {
                _pendingRetryKey = "";
                AppLog.Write($"[取词] 「{track.Title}」 网络失败后的延迟重试（60s）");
                StartFetch(track, skipCache: false);
                return;
            }

            if (track.Key != _loadedKey && track.Key != _fetchingKey)
                StartFetch(track, skipCache: false);
        };
        pollTimer.Start();

        // 渲染 tick：约 40ms 一帧（照规格），只推进渐变/切行，不重排版
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        tick.Tick += (_, _) => OnTick();
        tick.Start();

        // 启动后静默查一次更新（v0.4 起；v0.6 仓库转公开后无令牌也查——匿名检查；
        // 有新版才用气泡提醒，不弹窗打断听歌；失败只记限频日志不打扰用户）
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            try
            {
                var result = await UpdateService.CheckLatestAsync(_settings.GithubToken);
                Application.Current?.Dispatcher.Invoke(() => HandleCheckResult(result, manual: false));
            }
            catch (Exception ex)
            {
                AppLog.WriteLimited("silent-update-check",
                    $"[自动更新] 静默检查异常：{ex.GetType().Name}: {ex.Message}", 60_000);
            }
        });

        app.Run();
    }

    /// <summary>
    /// v0.6：三路全局异常捕获，统一走 AppLog.LogException 落盘（该方法即无头测试的被测对象）。
    /// Dispatcher 路标记 Handled 保命；AppDomain 路是最后遗言，只能落盘；
    /// TaskScheduler 路 SetObserved 防止未观察异常在终结器线程上升级。
    /// </summary>
    private static void RegisterGlobalExceptionHandlers(Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            AppLog.LogException("DispatcherUnhandledException（已标记 Handled 保命）", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.LogException("AppDomain.UnhandledException（进程即将退出）",
                e.ExceptionObject as Exception
                ?? new Exception("非 Exception 对象：" + (e.ExceptionObject?.ToString() ?? "null")));
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.LogException("TaskScheduler.UnobservedTaskException（已 SetObserved）", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>v0.6：把广告/纯音乐状态同步到悬浮窗右击菜单与托盘菜单的可用性/文案。</summary>
    private static void SyncAdInstrumentalUi()
    {
        _floating.IsAdShowing = _isAd;
        var t = _tracker.Current;
        bool marked = t != null && InstrumentalMarks.IsMarked(_settings, t.Key);
        _floating.IsInstrumental = marked;
        _tray.SetAdState(_isAd);
        _tray.SetInstrumentalState(marked);
    }

    /// <summary>
    /// v0.6「这不是广告」（悬浮窗右击菜单 / 托盘共用）：把当前曲 Key 记入抑制名单
    /// （存设置、上限 200），同曲永不再判广告；正在判广告的立刻解除并恢复取词。
    /// </summary>
    private static void OnNotAd()
    {
        var t = _tracker.Current;
        if (t == null) return;
        Settings.AddCapped(_settings.NotAdKeys, t.Key, 200);
        _settings.RequestSave();
        AppLog.Write($"[广告识别] 用户标记「这不是广告」：「{t.Title} - {t.Artist}」" +
            $"（抑制名单共 {_settings.NotAdKeys.Count} 条，该曲不再判广告）");
        if (_isAd) { _isAd = false; _adKey = ""; }
        _loadedKey = "";
        _notFound = false;
        SyncAdInstrumentalUi();
        if (!_isInstrumental) StartFetch(t, skipCache: false);
    }

    /// <summary>
    /// v0.6 纯音乐标记切换（悬浮窗右击菜单 / 托盘 / 手动选词窗共用语义）：
    /// 已标记 → 取消并恢复取词；未标记 → 标记、停掉在途取词、清掉当前歌词。
    /// </summary>
    private static void OnToggleInstrumental()
    {
        var t = _tracker.Current;
        if (t == null) return;
        if (InstrumentalMarks.IsMarked(_settings, t.Key))
        {
            InstrumentalMarks.Unmark(_settings, t.Key);
            AppLog.Write($"[纯音乐] 用户取消标记：「{t.Title} - {t.Artist}」，恢复取词");
            _isInstrumental = false;
            _instrumentalKey = "";
            _loadedKey = "";
            SyncAdInstrumentalUi();
            if (!_isAd) StartFetch(t, skipCache: false);
        }
        else
        {
            InstrumentalMarks.Mark(_settings, t.Key);
            AppLog.Write($"[纯音乐] 用户手动标记：「{t.Title} - {t.Artist}」，停止取词");
            _fetchCts?.Cancel();
            _fetchingKey = "";
            _engine.Lyrics = null;
            _isInstrumental = true;
            _instrumentalKey = t.Key;
            _loadedKey = t.Key;
            _notFound = false;
            _tray.SetStatus($"SpotifyLyrics - 纯音乐：{t.Title}");
            SyncAdInstrumentalUi();
        }
    }

    /// <summary>
    /// v0.6：合并 WPF-UI 的主题与控件资源字典（零 XAML 的代码路径），并套当前预设。
    /// 四套预设与库主题系统的对齐方式：Spotify 深色 = Dark；纯黑 = Dark + 底色资源覆写；
    /// 深蓝 = Dark + 蓝系底色资源覆写；浅色 = Light。强调色都走库的强调色管理器
    /// （深色三套 Spotify 绿、浅色套深绿，与自家调色板 Theme.Palettes 同一份色值）。
    /// </summary>
    private static void InitFluentUi()
    {
        var res = Application.Current.Resources;
        res.MergedDictionaries.Add(new ThemesDictionary
        {
            Theme = Theme.Current.IsLight ? ApplicationTheme.Light : ApplicationTheme.Dark
        });
        res.MergedDictionaries.Add(new ControlsDictionary());
        ApplyFluentTheme();
    }

    private static readonly List<string> _fluentOverrideKeys = new();

    /// <summary>把当前 Theme 调色板套到 WPF-UI 主题系统上（切换预设时重调用）。</summary>
    private static void ApplyFluentTheme()
    {
        var palette = Theme.Current;
        var theme = palette.IsLight ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(theme);
        ApplicationAccentColorManager.Apply(palette.Accent, theme);

        var res = Application.Current.Resources;
        foreach (string k in _fluentOverrideKeys) res.Remove(k);
        _fluentOverrideKeys.Clear();
        if (palette.Id is "pure-black" or "deep-blue")
        {
            SetOverride("ApplicationBackgroundBrush", palette.Bg);
            SetOverride("CardBackgroundFillColorDefaultBrush", palette.Panel);
        }
        void SetOverride(string key, Color color)
        {
            res[key] = Theme.Brush(color);
            _fluentOverrideKeys.Add(key);
        }
    }

    /// <summary>
    /// v0.6 主题切换：套新调色板 + Fluent 主题后，把已打开的设置窗/选词窗关掉重开整体换装
    /// （窗口构造时才读取资源/调色板；位置记忆保证重开回到原位，代价是滚动位置重置）。
    /// </summary>
    private static void OnThemeChanged()
    {
        Theme.Apply(_settings.UiTheme);
        ApplyFluentTheme();
        AppLog.Write($"[设置] 界面主题切换为「{Theme.Current.DisplayName}」（{_settings.UiTheme}）");
        if (_manualWindow != null) { var m = _manualWindow; _manualWindow = null; m.Close(); }
        if (_settingsWindow != null)
        {
            var w = _settingsWindow;
            _settingsWindow = null;
            w.Close();
        }
        OpenSettings();
    }

    private static void OpenSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        try { _settingsWindow = new SettingsWindow(_settings); }
        catch (Exception ex)
        {
            // v0.6：窗口打开异常必须留痕（v0.5.0 设置窗崩溃时用户只能口述「打不开」）
            AppLog.LogException("打开设置窗", ex);
            _settingsWindow = null;
            _tray.SetStatus("SpotifyLyrics - 设置窗打开失败，详情见日志");
            return;
        }
        // v0.2：设置实时生效——每次变更立刻重套悬浮窗样式（落盘由设置窗内的防抖处理）
        _settingsWindow.LiveChanged += () => _floating.ApplyStyle();
        _settingsWindow.OpenLogRequested += OpenLogFile;
        // v0.4：更新检查/安装由主程序编排，结果推回设置窗
        _settingsWindow.CheckUpdateRequested += async () =>
        {
            _settingsWindow?.SetUpdateStatus("正在检查更新…");
            var r = await UpdateService.CheckLatestAsync(_settings.GithubToken);
            HandleCheckResult(r, manual: true);
        };
        // v0.6：主题切换（关掉重开换装）与纯音乐名单清空（记一行日志）
        _settingsWindow.ThemeChanged += OnThemeChanged;
        _settingsWindow.InstrumentalMarksChanged += () =>
            AppLog.Write("[设置] 纯音乐标记名单已清空");
        _settingsWindow.InstallUpdateRequested += RunInstallUpdate;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        if (_pendingRelease != null)
        {
            _settingsWindow.SetUpdateStatus(
                $"发现新版 {_pendingRelease.TagName}（当前 v{UpdateService.CurrentVersion}）。点「立即更新」下载并自动换版。");
            _settingsWindow.ShowUpdateAvailable(_pendingRelease);
        }
        else if (_startupUpdateNote != null)
        {
            _settingsWindow.SetUpdateStatus(_startupUpdateNote + $" 当前版本 v{UpdateService.CurrentVersion}。");
        }
        _settingsWindow.Show();
    }

    /// <summary>v0.4：统一处理更新检查结果（静默/手动共用）。</summary>
    private static void HandleCheckResult(UpdateCheckResult result, bool manual)
    {
        AppLog.Write($"[自动更新] 检查：{result.Kind} {result.Message}");
        switch (result.Kind)
        {
            case UpdateCheckKind.UpdateAvailable:
                _pendingRelease = result.Release;
                if (_settingsWindow != null)
                {
                    _settingsWindow.SetUpdateStatus(result.Message + "。点「立即更新」下载并自动换版。");
                    _settingsWindow.ShowUpdateAvailable(result.Release!);
                }
                else if (!manual)
                {
                    _tray.ShowBalloon("Spotify 歌词有新版",
                        $"{result.Release!.TagName} 已发布，点这里打开设置更新");
                }
                _tray.SetStatus($"SpotifyLyrics - 发现新版 {result.Release!.TagName}");
                break;
            case UpdateCheckKind.UpToDate:
                _pendingRelease = null;
                _settingsWindow?.HideUpdateAvailable();
                _settingsWindow?.SetUpdateStatus(result.Message);
                break;
            default: // NoToken / Failed
                _settingsWindow?.SetUpdateStatus(result.Message);
                break;
        }
    }

    /// <summary>
    /// v0.4「立即更新」：下载 zip（带进度）→ 解压 staging → Updater 复制到临时目录并经 WMI
    /// 脱钩拉起 → 主程序退出交接。拉起失败绝不退出，提示用户手动运行 Updater 兜底。
    /// </summary>
    private static async void RunInstallUpdate()
    {
        var release = _pendingRelease;
        var win = _settingsWindow;
        if (release?.ZipAsset == null)
        {
            win?.SetUpdateStatus("没有可安装的新版，请先点「检查更新」。");
            return;
        }
        win?.SetInstallEnabled(false);
        try
        {
            Directory.CreateDirectory(UpdateService.UpdateWorkDir);
            // 先删旧助手日志：失败时用户打开的就是这一次的全新日志，不会和旧的混淆
            try { if (File.Exists(UpdateService.UpdaterLogPath)) File.Delete(UpdateService.UpdaterLogPath); } catch { }

            var progress = new Progress<double>(p => win?.SetUpdateStatus($"正在下载更新… {p:F0}%"));
            await UpdateService.DownloadAssetAsync(_settings.GithubToken, release.ZipAsset,
                UpdateService.DownloadedZipPath, progress);
            win?.SetUpdateStatus("下载完成，正在准备更新…");

            UpdateService.ExtractToStaging(UpdateService.DownloadedZipPath);
            string stagedUpdater = UpdateService.StageUpdater(AppContext.BaseDirectory);
            string appDir = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string args = UpdateLauncher.BuildArguments(
                Environment.ProcessId, UpdateService.StagingDir, appDir, "SpotifyLyricsWin.exe",
                "v" + UpdateService.CurrentVersion, release.TagName,
                UpdateService.ResultPath, UpdateService.UpdaterLogPath);

            var (ok, how, error) = UpdateLauncher.LaunchDetached(stagedUpdater, args);
            if (!ok)
            {
                win?.SetUpdateStatus(
                    $"更新助手没能自动启动（{error}）。请手动运行一次：{stagedUpdater} {args}");
                win?.SetInstallEnabled(true);
                AppLog.Write($"[自动更新] 拉起 Updater 失败：{error}");
                return;
            }
            AppLog.Write($"[自动更新] 已通过 {how} 拉起 Updater（目标 {release.TagName}），主程序退出交接");
            _tray.Dispose();
            _app.Shutdown();
        }
        catch (Exception ex)
        {
            win?.SetUpdateStatus($"更新没能开始：{ex.Message} 可再点「立即更新」重试。");
            win?.SetInstallEnabled(true);
            AppLog.Write($"[自动更新] 下载/准备失败：{ex.Message}");
        }
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
            var v = AdDetector.Check(track, _settings.NotAdKeys);
            string controls = _tracker.LastControlsSummary;
            signature = $"{track.Key}|{track.Album}|{track.DurationMs}|{track.HasCover}|{track.IsPlaying}|{v.IsAd}|{controls}";
            line = $"[SMTC] 标题=「{track.Title}」 歌手=「{track.Artist}」 专辑=「{track.Album}」 " +
                   $"时长={(track.DurationMs > 0 ? (track.DurationMs / 1000) + "s" : "未知")} " +
                   $"封面={(track.HasCover ? "有" : "无")} 状态={(track.IsPlaying ? "播放" : "暂停")} " +
                   $"判定={(v.IsAd ? $"广告（{v.Rule}）" : "歌曲")} 动作[{controls}]";
        }
        if (signature == _lastLogSignature) return;
        _lastLogSignature = signature;
        AppLog.Write(line);
    }

    private static void OpenManualPicker()
    {
        if (_manualWindow != null) { _manualWindow.Activate(); return; }
        var t = _tracker.Current;
        try { _manualWindow = new ManualLyricsWindow(t?.Title ?? "", t?.Artist ?? "", _settings); }
        catch (Exception ex)
        {
            // v0.6：窗口打开异常留痕（手动选词正是用户报「打开就退出」的路径，必须能查）
            AppLog.LogException("打开手动选词窗", ex);
            _manualWindow = null;
            _tray.SetStatus("SpotifyLyrics - 手动选词窗打开失败，详情见日志");
            return;
        }
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
        // v0.6：窗内「标记为纯音乐」——只在当前未标记时才切换（按钮语义是单向标记）
        _manualWindow.MarkInstrumentalRequested += () =>
        {
            var track = _tracker.Current;
            if (track != null && !InstrumentalMarks.IsMarked(_settings, track.Key))
                OnToggleInstrumental();
        };
        _manualWindow.Closed += (_, _) => _manualWindow = null;
        _manualWindow.Show();
    }

    /// <summary>
    /// 发起取词。v0.6 重做失败处理（借鉴清单 #1，蓝本 V2.4 的重试纪律）：
    /// 失败先分类——网络型才退避重试（自动取词最多 3 次、间隔 6s×次数；手动重搜 1 次），
    /// 没收录/解析型立即收手；网络型重试用尽后不像 v0.5 那样把 Key 永久锁死，
    /// 而是登记 60 秒后的一次延迟重试（ poll 驱动），给网络恢复留机会。
    /// 取词成功且无译文时，后台再向网易云补一次 tlyric（不阻塞显示）。
    /// </summary>
    private static void StartFetch(TrackInfo track, bool skipCache, bool manual = false)
    {
        _fetchCts?.Cancel();
        _fetchCts = new CancellationTokenSource();
        var ct = _fetchCts.Token;
        _fetchingKey = track.Key;
        _notFound = false;
        _statusHint = "♪ 正在找歌词…";
        _tray.SetStatus($"SpotifyLyrics - 正在找歌词：{track.Title}");
        string key = track.Key;
        int maxAttempts = manual ? 1 : 3;
        _ = Task.Run(async () =>
        {
            LyricsService.FetchReport? report = null;
            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    report = await _service.FetchReportAsync(track, skipCache, ct);
                    if (report.Outcome != null) break;
                    if (report.Failure != LyricsService.FetchFailureKind.Network) break;
                    if (attempt >= maxAttempts) break;
                    int delayMs = 6000 * attempt; // V2.4 §2.7：网络型退避 6s × 第几次
                    AppLog.Write($"[取词] 「{track.Title}」 网络型失败（{report.Detail}），{delayMs / 1000}s 后重试（{attempt + 1}/{maxAttempts}）");
                    await Task.Delay(delayMs, ct);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { AppLog.LogException("取词任务", ex); }

            var finalReport = report;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (ct.IsCancellationRequested) return;
                if (_tracker.Current?.Key != key) return; // 已经切歌了
                _fetchingKey = "";
                var outcome = finalReport?.Outcome;
                if (outcome != null)
                {
                    _engine.Lyrics = outcome.Lyrics;
                    _loadedKey = key;
                    _pendingRetryKey = "";
                    _tray.SetStatus($"SpotifyLyrics - 歌词：{outcome.SourceName}｜{track.Title}");
                    AppLog.Write($"[取词] 「{track.Title}」 命中 {outcome.SourceName}" +
                        (outcome.Score >= 0 ? $" 打分={outcome.Score} 候选=「{outcome.CandidateTitle}」" : "（顺序回退）") +
                        $" 逐字={outcome.Lyrics.IsWordLevel} 行数={outcome.Lyrics.Lines.Count}");
                    MaybeBackfillTranslation(track, outcome);
                }
                else
                {
                    _engine.Lyrics = null;
                    _notFound = true;
                    _tray.SetStatus($"SpotifyLyrics - 未找到歌词：{track.Title}");
                    if (finalReport?.Failure == LyricsService.FetchFailureKind.Network)
                    {
                        // 网络型：不锁死 Key，60 秒后自动再试一次（切歌会在 poll 里清掉登记）
                        _loadedKey = "";
                        _pendingRetryKey = key;
                        _pendingRetryAtTick = Environment.TickCount64 + 60_000;
                        AppLog.Write($"[取词] 「{track.Title}」 网络型失败耗尽重试（{finalReport.Detail}），已登记 60s 后自动重试");
                    }
                    else
                    {
                        _loadedKey = key; // 没收录/解析型：记住已试过，避免每 500ms 重试拖死接口；手动「重新搜词」可再来
                        AppLog.Write($"[取词] 「{track.Title}」 未命中：{finalReport?.Detail}（可手动选词）");
                    }
                }
            });
        }, ct);
    }

    /// <summary>
    /// v0.6 后台补译：主源歌词没有可用译文且设置允许时，向网易云补 tlyric，
    /// 补到后若仍是同一首歌、且引擎里还是同一份歌词对象，就无缝换上（并已写缓存）。
    /// 全程后台进行，不阻塞首屏显示；失败静默（下次取词还会再试缓存/主源）。
    /// </summary>
    private static void MaybeBackfillTranslation(TrackInfo track, FetchOutcome outcome)
    {
        if (!_settings.ShowTranslation) return;
        if (LyricsService.HasUsableTranslation(outcome.Lyrics)) return;
        string key = track.Key;
        var original = outcome.Lyrics;
        _ = Task.Run(async () =>
        {
            try
            {
                var merged = await _service.FetchTranslationBackupAsync(track, original, CancellationToken.None);
                if (merged == null) return;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_tracker.Current?.Key != key) return;
                    if (!ReferenceEquals(_engine.Lyrics, original)) return; // 已被手动选词等换掉
                    _engine.Lyrics = merged;
                });
            }
            catch (Exception ex) { AppLog.LogException("后台补译", ex); }
        });
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

        // v0.6 纯音乐：显示「纯音乐」小提示，而不是误导性的「未找到歌词」
        if (_isInstrumental && track.Key == _instrumentalKey)
        {
            _floating.SetDimmed(!track.IsPlaying);
            if (_settings.FloatingEnabled) _floating.SetHint("♪ 纯音乐 · 无歌词");
            return;
        }

        _floating.SetDimmed(!track.IsPlaying);
        // v0.5：进度改读 ProgressClock 的平滑显示进度（observe 校正、无死区硬跳）；
        // 时钟暂无样本时回退到曲目自带外推兜底。
        long smooth = _tracker.CurrentProgressMs();
        long progress = (smooth >= 0 ? smooth : track.CurrentProgress()) + _settings.LyricsOffsetMs;
        if (progress < 0) progress = 0;

        var lyrics = _engine.Lyrics;
        if (lyrics == null || _loadedKey != track.Key)
        {
            // v0.6：网络失败待重试（_pendingRetryKey）也算「已确认没找到」，别一直显示找词中
            bool confirmedNotFound = _notFound &&
                (_loadedKey == track.Key || _pendingRetryKey == track.Key);
            _floating.SetHint(confirmedNotFound ? "未找到歌词" : "♪ 正在找歌词…");
            return;
        }

        var render = _engine.RenderAt(progress, track.DurationMs);
        string translation = render.CurrentIndex >= 0 ? lyrics.TranslationAt(render.CurrentIndex) : "";
        _floating.SetLine(render.Current, render.Next, translation);
        _floating.SetProgress(render.ProgressFraction,
            _settings.WordByWordEnabled && render.Current.Length > 0);
    }
}
