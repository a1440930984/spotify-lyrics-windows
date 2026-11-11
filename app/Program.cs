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
    // v0.8：当前已应用歌词的来源与候选标题（供手动选词窗标「当前使用」并置顶）
    private static string _currentSourceName = "";
    private static string _currentCandidateTitle = "";
    private static CancellationTokenSource? _fetchCts;
    private static string _statusHint = "♪ 等待 Spotify 播放";
    private static bool _notFound;
    private static ManualLyricsWindow? _manualWindow;
    private static bool _isAd;
    private static string _adKey = "";
    private static string _lastLogSignature = "\0";
    // v0.7：广告连续段状态机（第一段实锤后，同 pod 内的弱信号段也判广告）
    private static readonly AdPodTracker _adPod = new();
    // v0.7：引擎里放的是「显示副本」（繁→简转换后）；原始歌词另存 _rawLyrics，
    // 开关切换时从原始重新生成显示副本。补译/机翻的换入校验也以原始对象为准。
    private static Core.Lyrics? _rawLyrics;

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
        AppLog.Write($"[启动] SpotifyLyricsWin v{UpdateService.CurrentVersion} 启动");

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
        // v0.8：托盘「检查更新」——走手动检查路径，有无新版都给反馈（见 HandleCheckResult）
        _tray.CheckUpdate += async () =>
        {
            AppLog.Write("[自动更新] 用户从托盘菜单手动检查更新");
            var r = await UpdateService.CheckLatestAsync(_settings.GithubToken);
            HandleCheckResult(r, manual: true);
        };
        // v0.8：托盘「重置歌词窗位置」——清位置记忆并挪回默认位（首开同款：居中、距顶 120），
        // 治悬浮窗被拖出屏幕/换显示器后找不回来。
        _tray.ResetFloatingPosition += () =>
        {
            _settings.FloatingX = null;
            _settings.FloatingY = null;
            _settings.RequestSave();
            var wa = SystemParameters.WorkArea;
            var (left, top) = WindowPlacement.DefaultFloatingPosition(
                wa.Left, wa.Top, wa.Width, _floating.Width);
            _floating.Left = left;
            _floating.Top = top;
            if (!_settings.FloatingEnabled)
            {
                _settings.FloatingEnabled = true;
                _settings.Save();
                _floating.Show();
            }
            AppLog.Write($"[悬浮窗] 位置已重置到默认（{left:0},{top:0}）");
        };
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
                _adPod.Reset();
                _statusHint = "♪ 等待 Spotify 播放";
                SyncAdInstrumentalUi();
                return;
            }

            // 广告识别（启发式）：命中则不取词、不推进，tick 里显示广告提示；结束自动恢复。
            // v0.6：判定带上用户「这不是广告」抑制名单，名单内的曲目永不判广告。
            // v0.7：判定结果再过一遍 AdPodTracker（连续段状态机）——第一段实锤后，
            // 同 pod 内信号变弱的后续段（专辑空但 40s、时长未知等）也能判出；
            // 且**每段都更新 _adKey 并逐段记日志**（旧逻辑只在 _isAd 由假转真时写 _adKey，
            // 连播第二段起 Key 对不上，tick 的广告提示会失效——实锤显示缺陷）。
            var verdict = _adPod.Observe(track, AdDetector.Check(track, _settings.NotAdKeys),
                TrackInfo.NowMs(), _settings.NotAdKeys);
            if (verdict.IsAd)
            {
                if (!_isAd || _adKey != track.Key)
                {
                    _isAd = true;
                    _adKey = track.Key;
                    AppLog.Write($"[广告识别] 段签名={track.Key} 标题=「{track.Title}」 歌手=「{track.Artist}」 " +
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
                    _engine.Lyrics = null; _rawLyrics = null;
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
    /// v0.7：把原始歌词生成显示副本。开关开 → 原文行 + 自带译文行繁→简（只转显示，
    /// 不动缓存/匹配；逐字时间数组复用同一引用、字数 1:1 不变）；关 → 原样。
    /// </summary>
    private static Core.Lyrics ToDisplayLyrics(Core.Lyrics raw) =>
        _settings.ConvertTradToSimp ? LyricsDisplay.ConvertForDisplay(raw) : raw;

    /// <summary>从 _rawLyrics 重生成引擎的显示副本（开关变化时即时生效）。</summary>
    private static void RefreshDisplayLyrics()
    {
        if (_rawLyrics != null) _engine.Lyrics = ToDisplayLyrics(_rawLyrics);
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
            _engine.Lyrics = null; _rawLyrics = null;
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
        if (palette.Id is "pure-black" or "deep-blue" or "spotify-dark")
        {
            // v0.7 覆写补全：v0.6 只写了两个 Brush 键，且 FluentWindow 开着 Mica 时
            // 窗口背景根本不走这些键（系统材质罩住），几套主题看着一模一样。
            // 双管齐下：深色套的窗口改走 WindowBackdropType.None（见 Theme.Backdrop，
            // v0.8 起深色三套全关 Mica），覆写键也从「背景 + 卡片背景」补到描边与
            // 对应的 Color 键（库里部分模板引用的是 Color 资源，只写 Brush 会漏）。
            SetOverride("ApplicationBackgroundBrush", palette.Bg);
            SetColorOverride("ApplicationBackgroundColor", palette.Bg);
            SetOverride("CardBackgroundFillColorDefaultBrush", palette.Panel);
            SetColorOverride("CardBackgroundFillColorDefault", palette.Panel);
            SetOverride("CardStrokeColorDefaultBrush", palette.Border);
            SetColorOverride("CardStrokeColorDefault", palette.Border);
        }
        void SetOverride(string key, Color color)
        {
            res[key] = Theme.Brush(color);
            _fluentOverrideKeys.Add(key);
        }
        void SetColorOverride(string key, Color color)
        {
            res[key] = color;
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
        // v0.7：主题切换后让悬浮窗重申一遍自己的背景铁律（恒透明 + 背景透明度设置），
        // 保证主题系统无论如何都碰不到悬浮窗背景（与主题彻底解耦的兜底动作）。
        _floating.ApplyStyle();
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
        // v0.7：顺带从原始歌词重生成显示副本（「繁体转简体」开关一切换，当前行立刻变）
        _settingsWindow.LiveChanged += () =>
        {
            _floating.ApplyStyle();
            RefreshDisplayLyrics();
        };
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
                // v0.8：托盘手动检查且设置窗没开着时，气泡给个回音，别点了没反应
                if (manual && _settingsWindow == null)
                    _tray.ShowBalloon("检查更新", result.Message);
                break;
            default: // NoToken / Failed
                _settingsWindow?.SetUpdateStatus(result.Message);
                if (manual && _settingsWindow == null)
                    _tray.ShowBalloon("检查更新", result.Message);
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
                   $"判定={(v.IsAd ? $"广告（{v.Rule}）" : "歌曲")} " +
                   $"广告段pod={(_adPod.IsPodActive(TrackInfo.NowMs()) ? "开" : "关")} 动作[{controls}]";
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
        // v0.8：把「当前在用的歌词」注入选词窗（标当前使用并置顶）；
        // 打分路径有候选标题，顺序回退路径没有——退用曲目标题匹配。
        _manualWindow.CurrentSourceName = _currentSourceName;
        _manualWindow.CurrentTitle = _currentCandidateTitle.Length > 0
            ? _currentCandidateTitle : (t?.Title ?? "");
        _manualWindow.ApplyAsync = async candidate =>
        {
            var track = _tracker.Current;
            if (track == null || _isAd) return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var outcome = await _service.FetchCandidateAsync(candidate, track, cts.Token);
            if (outcome == null) return false;
            _rawLyrics = outcome.Lyrics;
            _engine.Lyrics = ToDisplayLyrics(outcome.Lyrics);
            _loadedKey = track.Key;
            _currentSourceName = candidate.ProviderName;
            _currentCandidateTitle = candidate.Title;
            if (_manualWindow != null)
            {
                _manualWindow.CurrentSourceName = candidate.ProviderName;
                _manualWindow.CurrentTitle = candidate.Title;
            }
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
                    _rawLyrics = outcome.Lyrics;
                    _engine.Lyrics = ToDisplayLyrics(outcome.Lyrics);
                    _loadedKey = key;
                    _currentSourceName = outcome.SourceName;
                    _currentCandidateTitle = outcome.CandidateTitle;
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
                    _rawLyrics = null;
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
    /// 后台补译（v0.6 起）：主源歌词没有可用译文且设置允许时，向网易云补 tlyric，
    /// 补到后若仍是同一首歌、且原始歌词对象没被换掉，就无缝换上（并已写缓存）。
    /// v0.7 第二级兜底：网易云也没有时，若「无译文时自动翻译」开关开着，再走机器翻译
    /// （逐行机翻成简体中文、写 .trans.lrc 缓存并在日志标 machine 来源）。
    /// 全程后台进行，不阻塞首屏显示；失败静默（下次取词还会再试缓存/主源）。
    /// </summary>
    private static void MaybeBackfillTranslation(TrackInfo track, FetchOutcome outcome)
    {
        if (!_settings.ShowTranslation) return;
        if (LyricsService.HasUsableTranslation(outcome.Lyrics)) return;
        string key = track.Key;
        var original = outcome.Lyrics; // 原始歌词（未转简体的那份，换入校验以它为准）
        _ = Task.Run(async () =>
        {
            try
            {
                Core.Lyrics? merged = await _service.FetchTranslationBackupAsync(track, original, CancellationToken.None);
                if (merged == null && _settings.AutoTranslate)
                    merged = await _service.MachineTranslateAsync(track, original, CancellationToken.None);
                if (merged == null) return;
                var rawMerged = merged;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_tracker.Current?.Key != key) return;
                    if (!ReferenceEquals(_rawLyrics, original)) return; // 已被手动选词等换掉
                    _rawLyrics = rawMerged;
                    _engine.Lyrics = ToDisplayLyrics(rawMerged);
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
        // v0.8：文案精简为「♪ 广告播放中」，且走 SetAdHint——固定默认白色，不随用户颜色设置变。
        if (_isAd && track.Key == _adKey)
        {
            _floating.SetDimmed(true);
            if (_settings.FloatingEnabled) _floating.SetAdHint("♪ 广告播放中");
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
