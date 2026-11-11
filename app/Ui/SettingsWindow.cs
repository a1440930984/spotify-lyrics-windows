using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 设置窗（v0.5 现代化改版）：继承 ThemedWindow 自绘深色主题，标题「Spotify 歌词 · 设置 v0.5」。
/// - **所有改动实时生效**：控件一变就写 Settings、通知主程序重套悬浮窗样式、防抖落盘；
/// - v0.5 布局改卡片式：各分区独立卡片（圆角 12、描边、留白），开关全换自绘胶囊开关
///   （ToggleSwitch），滚动条换自绘细滚动条；
/// - v0.5 位置记忆：首次打开主屏居中，之后记住上次位置（见 ThemedWindow.UsePositionMemory）；
/// - v0.3 起没有「完成」按钮：实时生效+自动存盘下它没有功能意义，关窗走标题栏 ✕；
/// - 「打开日志」：诊断日志路径直接写在窗内（广告识别调规则靠它）。
/// 条目照复刻规格：逐字开关、字号 12~36、背景 0~100、高亮/文字色（hex + 色块 + 颜色盘 + 12 预设）、
/// 偏移 ±100ms（±30s 钳制）；另有字体、显示行数（单/双行）、锁定位置、Musixmatch Key、自动更新。
/// </summary>
public sealed class SettingsWindow : ThemedWindow
{
    private readonly Settings _settings;
    private readonly ToggleSwitch _wordByWord;
    private readonly ToggleSwitch _showTrans;
    private readonly ToggleSwitch _lockSwitch;
    private readonly RadioButton _doubleLine;
    private readonly RadioButton _singleLine;
    private readonly MiniSlider _fontSlider;
    private readonly TextBlock _fontValue;
    private readonly MiniSlider _bgSlider;
    private readonly TextBlock _bgValue;
    private readonly ColorRow _hiRow;
    private readonly ColorRow _textRow;
    private readonly TextBlock _offsetValue;
    private readonly ComboBox _fontCombo;
    private readonly TextBox _mxmKey;
    private bool _loading = true;

    /// <summary>任意设置变化（主程序据此重套悬浮窗样式；持久化走 Settings.RequestSave 防抖）。</summary>
    public event Action? LiveChanged;
    /// <summary>点「打开日志」（主程序负责用记事本打开 log.txt）。</summary>
    public event Action? OpenLogRequested;
    /// <summary>v0.4：点「检查更新」（主程序跑检查并把结果推回本窗）。</summary>
    public event Action? CheckUpdateRequested;
    /// <summary>v0.4：点「立即更新」（主程序下载、拉起 Updater 后退出本程序）。</summary>
    public event Action? InstallUpdateRequested;

    // v0.4 更新分区控件
    private readonly PasswordBox _tokenPwd;
    private readonly TextBox _tokenText;
    private readonly Button _tokenEye;
    private bool _tokenShown;
    private bool _syncingToken;
    private readonly TextBlock _updateStatus;
    private readonly TextBlock _releaseNotes;
    private readonly Button _installBtn;

    private static readonly string[] Presets = ColorPickerWindow.Presets;

    private sealed class ColorRow
    {
        public required TextBox HexBox;
        public required Border Swatch;
        public string Hex
        {
            get => HexBox.Text.Trim();
            set { HexBox.Text = value; UpdateSwatch(); }
        }
        public void UpdateSwatch()
        {
            var c = FloatingWindow.ParseHex(HexBox.Text, Colors.Transparent);
            Swatch.Background = new SolidColorBrush(c);
        }
    }

    public SettingsWindow(Settings settings) : base("Spotify 歌词 · 设置 v0.5", showMinimize: true)
    {
        _settings = settings;
        Width = 480;
        Height = 720;
        ResizeMode = ResizeMode.CanMinimize;

        var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };

        // ===== 卡片 1：外观与逐字 =====
        var (cardLook, look) = Theme.MakeCard("外观与逐字（改动实时生效，自动保存）");
        panel.Children.Add(cardLook);

        _wordByWord = AddSwitchRow(look, "平滑逐字高亮（渐变扫光）",
            v => _settings.WordByWordEnabled = v);
        _showTrans = AddSwitchRow(look, "显示译文（歌词源带译文时）",
            v => _settings.ShowTranslation = v);
        _lockSwitch = AddSwitchRow(look, "锁定悬浮窗位置（锁定后拖不动）",
            v => _settings.PositionLocked = v);

        // 显示行数（单行 = 当前行水平+垂直双居中；双行 = 当前行 + 下一行）
        look.Children.Add(new TextBlock { Text = "显示行数", Margin = new Thickness(0, 10, 0, 0), FontSize = 13.5 });
        var lineRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
        _doubleLine = new RadioButton { Content = "双行（当前行 + 下一行）", Margin = new Thickness(0, 0, 16, 0), GroupName = "lines" };
        _singleLine = new RadioButton { Content = "单行（仅当前行）", GroupName = "lines" };
        _doubleLine.Checked += (_, _) => OnChanged(() => _settings.SingleLineMode = false);
        _singleLine.Checked += (_, _) => OnChanged(() => _settings.SingleLineMode = true);
        lineRow.Children.Add(_doubleLine);
        lineRow.Children.Add(_singleLine);
        look.Children.Add(lineRow);

        // 字号 12~36
        _fontValue = new TextBlock { Foreground = Theme.SubTextBrush, FontSize = 12 };
        look.Children.Add(new TextBlock { Text = "歌词字号（12 ~ 36，长句会自动缩小）", Margin = new Thickness(0, 10, 0, 2), FontSize = 13.5 });
        _fontSlider = new MiniSlider { Minimum = 12, Maximum = 36 };
        _fontSlider.ValueChanged += v =>
        {
            _fontValue.Text = $"当前：{(int)v}";
            OnChanged(() => _settings.FontSize = (int)v);
        };
        look.Children.Add(_fontSlider);
        look.Children.Add(_fontValue);

        // ===== 卡片 2：颜色 =====
        var (cardColor, colorBody) = Theme.MakeCard("颜色");
        panel.Children.Add(cardColor);
        _hiRow = BuildColorRow(colorBody, "当前行高亮颜色（已唱过的部分）", first: true,
            () => _settings.HighlightColorHex, v => _settings.HighlightColorHex = v);
        _textRow = BuildColorRow(colorBody, "其余文字颜色（未唱部分 / 下一行）", first: false,
            () => _settings.TextColorHex, v => _settings.TextColorHex = v);

        // ===== 卡片 3：背景与时间 =====
        var (cardTime, timeBody) = Theme.MakeCard("背景与时间");
        panel.Children.Add(cardTime);
        _bgValue = new TextBlock { Foreground = Theme.SubTextBrush, FontSize = 12 };
        timeBody.Children.Add(new TextBlock
        {
            Text = "背景框透明度（0 ~ 100，0 = 只显示文字无背景框，推荐）",
            FontSize = 13.5, TextWrapping = TextWrapping.Wrap
        });
        _bgSlider = new MiniSlider { Minimum = 0, Maximum = 100 };
        _bgSlider.ValueChanged += v =>
        {
            _bgValue.Text = $"当前：{(int)v}";
            OnChanged(() => _settings.BgAlphaPercent = (int)v);
        };
        timeBody.Children.Add(_bgSlider);
        timeBody.Children.Add(_bgValue);

        timeBody.Children.Add(new TextBlock
        {
            Text = "歌词时间偏移（正数 = 歌词提前）",
            FontSize = 13.5,
            Margin = new Thickness(0, 12, 0, 0)
        });
        var offsetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var minus = new Button { Content = "− 0.1s", Width = 74, Margin = new Thickness(0, 0, 6, 0) };
        var plus = new Button { Content = "+ 0.1s", Width = 74, Margin = new Thickness(0, 0, 6, 0) };
        _offsetValue = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.SubTextBrush };
        minus.Click += (_, _) => OnChanged(() =>
            _settings.LyricsOffsetMs = Math.Clamp(_settings.LyricsOffsetMs - 100, -30000, 30000), RefreshOffset);
        plus.Click += (_, _) => OnChanged(() =>
            _settings.LyricsOffsetMs = Math.Clamp(_settings.LyricsOffsetMs + 100, -30000, 30000), RefreshOffset);
        offsetRow.Children.Add(minus);
        offsetRow.Children.Add(plus);
        offsetRow.Children.Add(_offsetValue);
        timeBody.Children.Add(offsetRow);

        // ===== 卡片 4：字体与歌词源 =====
        var (cardFont, fontBody) = Theme.MakeCard("字体与歌词源");
        panel.Children.Add(cardFont);
        fontBody.Children.Add(new TextBlock { Text = "字体（默认 Microsoft YaHei UI）", FontSize = 13.5 });
        _fontCombo = new ComboBox { Margin = new Thickness(0, 6, 0, 0) };
        var families = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(s => s).ToList();
        foreach (string f in families) _fontCombo.Items.Add(f);
        _fontCombo.SelectionChanged += (_, _) =>
        {
            if (_fontCombo.SelectedItem is string f)
                OnChanged(() => _settings.FontFamily = f);
        };
        fontBody.Children.Add(_fontCombo);

        fontBody.Children.Add(new TextBlock
        {
            Text = "Musixmatch API Key（可选，留空走桌面免 key 通道）",
            FontSize = 13.5,
            Margin = new Thickness(0, 12, 0, 0)
        });
        _mxmKey = new TextBox { Margin = new Thickness(0, 6, 0, 0) };
        _mxmKey.TextChanged += (_, _) => OnChanged(() => _settings.MusixmatchApiKey = _mxmKey.Text.Trim());
        fontBody.Children.Add(_mxmKey);

        // ===== 卡片 5：自动更新（v0.4）：私有仓 + 只读令牌，令牌只存本机 =====
        var (cardUpdate, upd) = Theme.MakeCard("自动更新（GitHub 私有仓库）");
        panel.Children.Add(cardUpdate);
        upd.Children.Add(new TextBlock
        {
            Text = "仓库是私有的，检查更新需要一枚只读令牌：GitHub → Settings → Developer settings → " +
                   "Fine-grained tokens，只选 spotify-lyrics-windows 仓库、Contents 只读即可。" +
                   "令牌只填在本机这里、只存本机，不用发到聊天里。",
            Foreground = Theme.SubTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        });
        var tokenRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        tokenRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tokenRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _tokenPwd = new PasswordBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Theme.PanelBrush,
            Foreground = Theme.TextBrush,
            BorderBrush = Theme.BorderBrush
        };
        _tokenText = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _tokenEye = new Button { Content = "显示", Width = 56, Height = 32, Margin = new Thickness(6, 0, 0, 0) };
        _tokenPwd.PasswordChanged += (_, _) =>
        {
            if (_syncingToken || _tokenShown) return;
            OnChanged(() => _settings.GithubToken = _tokenPwd.Password.Trim());
        };
        _tokenText.TextChanged += (_, _) =>
        {
            if (_syncingToken || !_tokenShown) return;
            OnChanged(() => _settings.GithubToken = _tokenText.Text.Trim());
        };
        _tokenEye.Click += (_, _) => ToggleTokenVisibility();
        Grid.SetColumn(_tokenPwd, 0);
        Grid.SetColumn(_tokenText, 0);
        Grid.SetColumn(_tokenEye, 1);
        tokenRow.Children.Add(_tokenPwd);
        tokenRow.Children.Add(_tokenText);
        tokenRow.Children.Add(_tokenEye);
        upd.Children.Add(tokenRow);

        _updateStatus = new TextBlock
        {
            Foreground = Theme.SubTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        upd.Children.Add(_updateStatus);
        _releaseNotes = new TextBlock
        {
            Foreground = Theme.SubTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };
        upd.Children.Add(_releaseNotes);
        var updateBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var checkBtn = new Button { Content = "检查更新", Height = 32, Width = 110, Margin = new Thickness(0, 0, 8, 0) };
        checkBtn.Click += (_, _) => CheckUpdateRequested?.Invoke();
        _installBtn = new Button
        {
            Content = "立即更新",
            Height = 32,
            Width = 110,
            Visibility = Visibility.Collapsed,
            Style = Theme.AccentButtonStyle()
        };
        _installBtn.Click += (_, _) => InstallUpdateRequested?.Invoke();
        updateBtnRow.Children.Add(checkBtn);
        updateBtnRow.Children.Add(_installBtn);
        upd.Children.Add(updateBtnRow);

        // ===== 卡片 6：诊断日志 =====
        var (cardLog, log) = Theme.MakeCard("诊断日志（广告识别/播放状态都记在这里）");
        panel.Children.Add(cardLog);
        log.Children.Add(new TextBlock
        {
            Text = AppLog.LogPath,
            Foreground = Theme.SubTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        });
        var logBtn = new Button { Content = "打开日志", Height = 32, Margin = new Thickness(0, 8, 0, 0) };
        logBtn.Click += (_, _) => OpenLogRequested?.Invoke();
        log.Children.Add(logBtn);

        // 恢复默认（实时生效）。v0.3 起没有「完成」按钮——关窗请用标题栏 ✕。
        var reset = new Button { Content = "恢复默认外观（纯文字 + 绿色平滑逐字）", Height = 34 };
        reset.Click += (_, _) =>
        {
            _loading = true;
            _settings.ResetStyle();
            LoadIntoControls();
            _loading = false;
            LiveChanged?.Invoke();
            _settings.RequestSave();
        };
        panel.Children.Add(reset);

        SetBody(new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        // v0.5 位置记忆：首开居中，之后回到上次位置
        UsePositionMemory(settings.SettingsWindowX, settings.SettingsWindowY, (x, y) =>
        {
            _settings.SettingsWindowX = x;
            _settings.SettingsWindowY = y;
            _settings.RequestSave();
        });

        LoadIntoControls();
        _loading = false;
    }

    /// <summary>统一的实时变更入口：写设置 → 通知主程序 → 防抖落盘。加载控件时不触发。</summary>
    private void OnChanged(Action mutate, Action? after = null)
    {
        if (_loading) return;
        mutate();
        after?.Invoke();
        LiveChanged?.Invoke();
        _settings.RequestSave();
    }

    /// <summary>卡片内开关行：左标签 + 右胶囊开关（v0.5）。</summary>
    private ToggleSwitch AddSwitchRow(StackPanel parent, string label, Action<bool> apply)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tb = new TextBlock
        {
            Text = label,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var sw = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center };
        sw.Toggled += v => OnChanged(() => apply(v));
        Grid.SetColumn(tb, 0);
        Grid.SetColumn(sw, 1);
        grid.Children.Add(tb);
        grid.Children.Add(sw);
        parent.Children.Add(grid);
        return sw;
    }

    private ColorRow BuildColorRow(StackPanel panel, string label, bool first,
        Func<string> get, Action<string> set)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13.5,
            Margin = new Thickness(0, first ? 2 : 14, 0, 0)
        });
        var row = new ColorRow
        {
            HexBox = new TextBox { Width = 110, VerticalContentAlignment = VerticalAlignment.Center },
            Swatch = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(10),
                BorderBrush = Theme.BorderBrush,
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = "点开颜色盘"
            }
        };
        var pick = new Button
        {
            Content = "颜色盘…", Height = 34, Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        pick.Click += (_, _) => OpenColorPicker(row, get, set);
        row.Swatch.MouseLeftButtonUp += (_, _) => OpenColorPicker(row, get, set);

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        hexRow.Children.Add(row.Swatch);
        hexRow.Children.Add(row.HexBox);
        hexRow.Children.Add(pick);
        row.HexBox.TextChanged += (_, _) =>
        {
            row.UpdateSwatch();
            if (Settings.IsValidHex(row.Hex))
                OnChanged(() => set(NormalizeHex(row.Hex)));
        };
        panel.Children.Add(hexRow);

        // 12 个预设色（原样），点选即填 hex 并实时生效（三处同步）
        var presets = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (string p in Presets)
        {
            var c = FloatingWindow.ParseHex(p, Colors.Transparent);
            var btn = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(c),
                BorderBrush = Theme.BorderBrush,
                Tag = p
            };
            btn.Click += (_, _) => row.Hex = (string)btn.Tag;
            presets.Children.Add(btn);
        }
        panel.Children.Add(presets);
        return row;
    }

    private ColorPickerWindow? _picker;

    /// <summary>
    /// 打开 HSV 颜色盘（v0.3 交互）：居中于本窗、选色实时回调生效、失活自动关（关窗即确认）。
    /// 非模态打开，重复点只激活已开的那个。任何异常都兜底提示；
    /// 兜底路径是下方 12 个预设色或直接填 hex，一直可用。
    /// </summary>
    private void OpenColorPicker(ColorRow row, Func<string> get, Action<string> set)
    {
        try
        {
            if (_picker != null) { _picker.Activate(); return; }
            var initial = FloatingWindow.ParseHex(get(), Colors.White);
            var dlg = new ColorPickerWindow(initial) { Owner = this };
            _picker = dlg;
            dlg.ColorChanged += c =>
            {
                row.Hex = $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";
            };
            dlg.Closed += (_, _) => { if (ReferenceEquals(_picker, dlg)) _picker = null; };
            dlg.Show();
        }
        catch (Exception ex)
        {
            _picker = null;
            MessageBox.Show(this,
                $"颜色盘打开失败（{ex.Message}）。\n可以直接点下方 12 个预设色块，或在输入框填 #AARRGGBB，效果一样实时生效。",
                "选择颜色", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshOffset()
    {
        long v = _settings.LyricsOffsetMs;
        _offsetValue.Text = v == 0 ? "当前：0 ms" : $"当前：{v:+#;-#;0} ms";
    }

    private void LoadIntoControls()
    {
        _wordByWord.IsChecked = _settings.WordByWordEnabled;
        _showTrans.IsChecked = _settings.ShowTranslation;
        _lockSwitch.IsChecked = _settings.PositionLocked;
        _doubleLine.IsChecked = !_settings.SingleLineMode;
        _singleLine.IsChecked = _settings.SingleLineMode;
        _fontSlider.Value = _settings.FontSize;
        _fontValue.Text = $"当前：{_settings.FontSize}";
        _bgSlider.Value = _settings.BgAlphaPercent;
        _bgValue.Text = $"当前：{_settings.BgAlphaPercent}";
        _hiRow.Hex = _settings.HighlightColorHex;
        _textRow.Hex = _settings.TextColorHex;
        _fontCombo.SelectedItem = _settings.FontFamily;
        _mxmKey.Text = _settings.MusixmatchApiKey;
        _syncingToken = true;
        _tokenPwd.Password = _settings.GithubToken;
        _tokenText.Text = _settings.GithubToken;
        _syncingToken = false;
        RefreshOffset();
        SetUpdateStatus(string.IsNullOrWhiteSpace(_settings.GithubToken)
            ? $"当前版本 v{UpdateService.CurrentVersion}。填只读令牌后可用自动更新。"
            : $"当前版本 v{UpdateService.CurrentVersion}。启动时会自动查一次，也可点「检查更新」。");
    }

    // ---------- v0.4 更新分区（供主程序推状态回来） ----------

    private void ToggleTokenVisibility()
    {
        _tokenShown = !_tokenShown;
        _syncingToken = true;
        if (_tokenShown)
        {
            _tokenText.Text = _tokenPwd.Password;
            _tokenText.Visibility = Visibility.Visible;
            _tokenPwd.Visibility = Visibility.Collapsed;
            _tokenEye.Content = "隐藏";
        }
        else
        {
            _tokenPwd.Password = _tokenText.Text;
            _tokenPwd.Visibility = Visibility.Visible;
            _tokenText.Visibility = Visibility.Collapsed;
            _tokenEye.Content = "显示";
        }
        _syncingToken = false;
    }

    public void SetUpdateStatus(string text) => _updateStatus.Text = text;

    /// <summary>有新版时：显示 Release 说明摘要（截断）与「立即更新」按钮。</summary>
    public void ShowUpdateAvailable(ReleaseInfo release)
    {
        string body = (release.Body ?? "").Trim();
        if (body.Length > 300) body = body.Substring(0, 300) + "…";
        _releaseNotes.Text = body;
        _releaseNotes.Visibility = body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _installBtn.Visibility = Visibility.Visible;
        _installBtn.IsEnabled = true;
    }

    public void HideUpdateAvailable()
    {
        _installBtn.Visibility = Visibility.Collapsed;
        _releaseNotes.Visibility = Visibility.Collapsed;
    }

    public void SetInstallEnabled(bool enabled) => _installBtn.IsEnabled = enabled;

    /// <summary>统一成 #AARRGGBB（6 位补 FF alpha；颜色强制不透明，照安卓颜色盘规则）</summary>
    private static string NormalizeHex(string hex)
    {
        string h = hex.Trim().TrimStart('#').ToUpperInvariant();
        if (h.Length == 6) h = "FF" + h;
        return "#" + h;
    }
}
