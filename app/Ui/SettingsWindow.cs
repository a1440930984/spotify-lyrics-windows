using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using SpotifyLyricsWin.Core;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using PasswordBox = Wpf.Ui.Controls.PasswordBox;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = Wpf.Ui.Controls.TextBox;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 设置窗（v0.6 换 Fluent 外壳）：WPF-UI（lepoco WPF-UI 4.3.0）的 FluentWindow
/// （Mica 质感 + 库标题栏）+ 库控件（Card/ToggleSwitch/NumberBox/TextBox/PasswordBox/Button），
/// 下拉用库主题化的原生 ComboBox。v0.5 的自绘 ThemedWindow 外壳就此退役（颜色盘窗仍用）。
/// - **所有改动实时生效**：控件一变就写 Settings、通知主程序、防抖落盘；没有「完成」按钮；
/// - v0.6 新增：界面主题选择（4 套预设，与库主题系统对齐，切换后本窗由主程序关掉重开换装）、
///   字体本地化名 + 每项自身字体预览 + 未展开时滚轮不改选、令牌改「可选」（仓库已转公开）、
///   纯音乐标记名单查看/清空；
/// - 关键设置变更记日志（主题/字体/开关/令牌只记「已更新」不记值）。
/// 条目照复刻规格：逐字开关、译文开关、字号 12~60（v0.7 上限放宽）、背景 0~100、高亮/文字色
/// （hex + 色块 + 颜色盘 + 12 预设）、偏移 ±100ms（±30s 钳制）；另有字体、单/双行、
/// 锁定位置、Musixmatch Key、自动更新、诊断日志。
/// </summary>
public sealed class SettingsWindow : FluentWindow
{
    private readonly Settings _settings;
    private readonly ToggleSwitch _wordByWord;
    private readonly ToggleSwitch _showTrans;
    private readonly ToggleSwitch _simpSwitch;
    private readonly ToggleSwitch _autoTransSwitch;
    private readonly ToggleSwitch _lockSwitch;
    private readonly RadioButton _doubleLine;
    private readonly RadioButton _singleLine;
    private readonly NumberBox _fontSizeBox;
    private readonly NumberBox _bgAlphaBox;
    private readonly ColorRow _hiRow;
    private readonly ColorRow _textRow;
    private readonly TextBlock _offsetValue;
    private readonly ComboBox _fontCombo;
    private readonly ComboBox _themeCombo;
    private readonly TextBox _mxmKey;
    private readonly TextBlock _marksCount;
    private readonly ListBox _marksList;
    private bool _loading = true;

    /// <summary>任意设置变化（主程序据此重套悬浮窗样式；持久化走 Settings.RequestSave 防抖）。</summary>
    public event Action? LiveChanged;
    /// <summary>点「打开日志」（主程序负责用记事本打开 log.txt）。</summary>
    public event Action? OpenLogRequested;
    /// <summary>v0.4：点「检查更新」（主程序跑检查并把结果推回本窗）。</summary>
    public event Action? CheckUpdateRequested;
    /// <summary>v0.4：点「立即更新」（主程序下载、拉起 Updater 后退出本程序）。</summary>
    public event Action? InstallUpdateRequested;
    /// <summary>v0.6：界面主题被切换（主程序负责套调色板 + Fluent 主题并重开本窗换装）。</summary>
    public event Action? ThemeChanged;
    /// <summary>v0.6：纯音乐标记名单被清空（主程序记一行日志；标记在轮询里实时读取、无需其他动作）。</summary>
    public event Action? InstrumentalMarksChanged;

    // 更新分区控件
    private readonly PasswordBox _tokenPwd;
    private readonly TextBox _tokenText;
    private readonly Button _tokenEye;
    private bool _tokenShown;
    private bool _syncingToken;
    private readonly TextBlock _updateStatus;
    private readonly TextBlock _releaseNotes;
    private readonly Button _installBtn;

    private static readonly string[] Presets = ColorPickerWindow.Presets;

    /// <summary>主题下拉项：显示名 + 调色板 id（顺序与 Theme.Palettes 的推荐顺序一致）。</summary>
    private static readonly (string Id, string Display)[] ThemeChoices =
    {
        ("light", "浅色（默认）"),
        ("spotify-dark", "Spotify 深色"),
        ("pure-black", "纯黑（OLED）"),
        ("deep-blue", "深蓝")
    };

    /// <summary>字体下拉项：存真实字体名 Source，显示本地化名 Display，并以自身字体渲染。</summary>
    private sealed class FontChoice
    {
        public required string Source { get; init; }
        public required FontFamily Family { get; init; }
        public required string Display { get; init; }
        public override string ToString() => Display;
    }

    /// <summary>
    /// 字体本地化名（v0.6）：把 FamilyNames 摊平成 (语言标签, 名称) 列表，
    /// 选择顺序走 Core.FontNamePick（zh-cn → zh-Hans → en-us → 第一个非空，纯逻辑可无头测试）。
    /// </summary>
    public static string LocalizedFontName(FontFamily family)
    {
        var list = new List<(string Lang, string Name)>();
        foreach (var kv in family.FamilyNames)
            list.Add((kv.Key.IetfLanguageTag, kv.Value));
        return FontNamePick.Pick(list, family.Source);
    }

    private sealed class ColorRow
    {
        public required System.Windows.Controls.TextBox HexBox;
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

    public SettingsWindow(Settings settings)
    {
        _settings = settings;
        Title = "Spotify 歌词 · 设置 v0.7";
        Width = 500;
        Height = 760;
        MinWidth = 440;
        MinHeight = 480;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ExtendsContentIntoTitleBar = true;
        // v0.7：纯黑/深蓝两套关 Mica（系统材质不吃背景覆写，两套会被罩成一个样），其余保留 Mica
        WindowBackdropType = Theme.Backdrop;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titleBar = new TitleBar
        {
            Title = "Spotify 歌词 · 设置",
            ShowMaximize = false
        };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var panel = new StackPanel { Margin = new Thickness(14, 10, 14, 14) };

        // ===== 卡片 1：外观与逐字（含界面主题） =====
        var look = CardPanel(panel, "外观与逐字（改动实时生效，自动保存）");

        look.Children.Add(new TextBlock { Text = "界面主题", FontSize = 13.5, Margin = new Thickness(0, 2, 0, 0) });
        _themeCombo = new ComboBox { Margin = new Thickness(0, 6, 0, 4) };
        foreach (var (id, display) in ThemeChoices)
            _themeCombo.Items.Add(new ComboBoxItem { Content = display, Tag = id });
        GuardWheel(_themeCombo);
        _themeCombo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (_themeCombo.SelectedItem is ComboBoxItem item && item.Tag is string id
                && id != _settings.UiTheme)
            {
                _settings.UiTheme = id;
                _settings.RequestSave();
                ThemeChanged?.Invoke(); // 主程序套新主题后会关掉重开本窗，之后的代码不要再碰控件
            }
        };
        look.Children.Add(_themeCombo);
        look.Children.Add(new TextBlock
        {
            Text = "主题只换设置/选词窗的界面配色；悬浮歌词的文字颜色仍由下方「颜色」决定。",
            Foreground = Theme.SubTextBrush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _wordByWord = AddSwitchRow(look, "平滑逐字高亮（渐变扫光）",
            v => { _settings.WordByWordEnabled = v; AppLog.WriteLimited("set-wordbyword", $"[设置] 逐字扫光 = {v}", 5000); });
        _showTrans = AddSwitchRow(look, "显示译文（歌词源带译文时）",
            v => { _settings.ShowTranslation = v; AppLog.WriteLimited("set-showtrans", $"[设置] 显示译文 = {v}", 5000); });
        _simpSwitch = AddSwitchRow(look, "繁体转简体显示（原文与自带译文，只转显示不改原文）",
            v => { _settings.ConvertTradToSimp = v; AppLog.WriteLimited("set-simp", $"[设置] 繁体转简体 = {v}", 5000); });
        _autoTransSwitch = AddSwitchRow(look, "无译文时自动翻译成中文（机器翻译，所有语种）",
            v => { _settings.AutoTranslate = v; AppLog.WriteLimited("set-autotrans", $"[设置] 无译文自动翻译 = {v}", 5000); });
        _lockSwitch = AddSwitchRow(look, "锁定悬浮窗位置（锁定后拖不动）",
            v => _settings.PositionLocked = v);

        look.Children.Add(new TextBlock { Text = "显示行数", Margin = new Thickness(0, 10, 0, 0), FontSize = 13.5 });
        var lineRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
        _doubleLine = new RadioButton { Content = "双行（当前行 + 下一行）", Margin = new Thickness(0, 0, 16, 0), GroupName = "lines" };
        _singleLine = new RadioButton { Content = "单行（仅当前行）", GroupName = "lines" };
        _doubleLine.Checked += (_, _) => OnChanged(() => _settings.SingleLineMode = false);
        _singleLine.Checked += (_, _) => OnChanged(() => _settings.SingleLineMode = true);
        lineRow.Children.Add(_doubleLine);
        lineRow.Children.Add(_singleLine);
        look.Children.Add(lineRow);

        look.Children.Add(new TextBlock { Text = "歌词字号（12 ~ 60，长句会自动缩小）", Margin = new Thickness(0, 10, 0, 2), FontSize = 13.5 });
        _fontSizeBox = new NumberBox
        {
            Minimum = 12, Maximum = 60, SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        _fontSizeBox.ValueChanged += (_, _) =>
        {
            if (_fontSizeBox.Value is double v)
                OnChanged(() => _settings.FontSize = (int)Math.Round(v));
        };
        look.Children.Add(_fontSizeBox);

        // ===== 卡片 2：颜色 =====
        var colorBody = CardPanel(panel, "颜色");
        _hiRow = BuildColorRow(colorBody, "当前行高亮颜色（已唱过的部分）", first: true,
            () => _settings.HighlightColorHex, v => _settings.HighlightColorHex = v);
        _textRow = BuildColorRow(colorBody, "其余文字颜色（未唱部分 / 下一行）", first: false,
            () => _settings.TextColorHex, v => _settings.TextColorHex = v);

        // ===== 卡片 3：背景与时间 =====
        var timeBody = CardPanel(panel, "背景与时间");
        timeBody.Children.Add(new TextBlock
        {
            Text = "背景框透明度（0 ~ 100，0 = 只显示文字无背景框，推荐）",
            FontSize = 13.5, TextWrapping = TextWrapping.Wrap
        });
        _bgAlphaBox = new NumberBox
        {
            Minimum = 0, Maximum = 100, SmallChange = 5, Margin = new Thickness(0, 6, 0, 0),
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        _bgAlphaBox.ValueChanged += (_, _) =>
        {
            if (_bgAlphaBox.Value is double v)
                OnChanged(() => _settings.BgAlphaPercent = (int)Math.Round(v));
        };
        timeBody.Children.Add(_bgAlphaBox);

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
        var fontBody = CardPanel(panel, "字体与歌词源");
        fontBody.Children.Add(new TextBlock { Text = "字体（默认 Microsoft YaHei UI，列表显示中文名）", FontSize = 13.5 });
        _fontCombo = new ComboBox { Margin = new Thickness(0, 6, 0, 0), ItemTemplate = FontItemTemplate() };
        var choices = Fonts.SystemFontFamilies
            .GroupBy(f => f.Source).Select(g => g.First())
            // v0.7：剔除符号/图标字体（Wingdings/Symbol/MDL2 类，渲染歌词是乱码方块）
            .Where(f => !FontFilter.IsSymbolFont(f.Source))
            .Select(f => new FontChoice { Source = f.Source, Family = f, Display = LocalizedFontName(f) })
            .OrderBy(c => c.Display).ToList();
        foreach (var c in choices) _fontCombo.Items.Add(c);
        GuardWheel(_fontCombo);
        _fontCombo.SelectionChanged += (_, _) =>
        {
            if (_fontCombo.SelectedItem is FontChoice c)
                OnChanged(() =>
                {
                    _settings.FontFamily = c.Source; // 存真实字体名，与旧设置完全兼容
                    AppLog.WriteLimited("set-font", $"[设置] 字体改为 {c.Source}", 5000);
                });
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

        // ===== 卡片 5：自动更新 =====
        var upd = CardPanel(panel, "自动更新（GitHub）");
        upd.Children.Add(new TextBlock
        {
            Text = "仓库已转公开：不填令牌也能检查更新（匿名）。令牌框保留备用——仓库以后切回私有时，" +
                   "填一枚只读令牌（GitHub → Fine-grained tokens，只选本仓库、Contents 只读）即可继续用。" +
                   "令牌只存本机，不用发到聊天里。",
            Foreground = Theme.SubTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        });
        var tokenRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        tokenRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tokenRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _tokenPwd = new PasswordBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        _tokenText = new TextBox
        {
            Height = 32, VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _tokenEye = new Button { Content = "显示", Width = 56, Height = 32, Margin = new Thickness(6, 0, 0, 0) };
        _tokenPwd.PasswordChanged += (_, _) =>
        {
            if (_syncingToken || _tokenShown) return;
            OnChanged(() =>
            {
                _settings.GithubToken = _tokenPwd.Password.Trim();
                AppLog.WriteLimited("set-token", "[设置] GitHub 令牌已更新（只存本机，日志不记内容）", 30_000);
            });
        };
        _tokenText.TextChanged += (_, _) =>
        {
            if (_syncingToken || !_tokenShown) return;
            OnChanged(() =>
            {
                _settings.GithubToken = _tokenText.Text.Trim();
                AppLog.WriteLimited("set-token", "[设置] GitHub 令牌已更新（只存本机，日志不记内容）", 30_000);
            });
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
            Appearance = ControlAppearance.Primary
        };
        _installBtn.Click += (_, _) => InstallUpdateRequested?.Invoke();
        updateBtnRow.Children.Add(checkBtn);
        updateBtnRow.Children.Add(_installBtn);
        upd.Children.Add(updateBtnRow);

        // ===== 卡片 6：纯音乐标记 =====
        var marks = CardPanel(panel, "纯音乐标记（被标记的曲目不再取词）");
        _marksCount = new TextBlock { Foreground = Theme.SubTextBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        marks.Children.Add(_marksCount);
        _marksList = new ListBox
        {
            MaxHeight = 110,
            Margin = new Thickness(0, 6, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        marks.Children.Add(_marksList);
        var clearMarks = new Button { Content = "清空全部标记", Height = 30, Margin = new Thickness(0, 8, 0, 0) };
        clearMarks.Click += (_, _) =>
        {
            if (_settings.InstrumentalKeys.Count == 0) return;
            _settings.InstrumentalKeys.Clear();
            _settings.RequestSave();
            RefreshInstrumentalMarks();
            InstrumentalMarksChanged?.Invoke();
        };
        marks.Children.Add(clearMarks);

        // ===== 卡片 7：诊断日志 =====
        var logBody = CardPanel(panel, "诊断日志（广告识别/播放状态都记在这里）");
        logBody.Children.Add(new TextBlock
        {
            Text = AppLog.LogPath,
            Foreground = Theme.SubTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        });
        var logBtn = new Button { Content = "打开日志", Height = 32, Margin = new Thickness(0, 8, 0, 0) };
        logBtn.Click += (_, _) => OpenLogRequested?.Invoke();
        logBody.Children.Add(logBtn);

        // 恢复默认（实时生效）。没有「完成」按钮——关窗请用标题栏 ✕。
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

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;

        InitPositionMemory(settings.SettingsWindowX, settings.SettingsWindowY, (x, y) =>
        {
            _settings.SettingsWindowX = x;
            _settings.SettingsWindowY = y;
            _settings.RequestSave();
        });

        LoadIntoControls();
        _loading = false;
    }

    /// <summary>卡片容器：库 Card + 标题行 + 内容面板，返回内容面板供填充。</summary>
    private static StackPanel CardPanel(StackPanel parent, string title)
    {
        var body = new StackPanel();
        var card = new Card
        {
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Content = body
        };
        body.Children.Add(new TextBlock
        {
            Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        parent.Children.Add(card);
        return body;
    }

    /// <summary>
    /// 滚轮守卫（v0.7 重做）：ComboBox 未展开时不能让滚轮改选（v0.6 已防），
    /// 但也不能吞事件——v0.6 直接 Handled 导致鼠标经过下拉框时页面滚动「卡住」。
    /// 现在把滚动量转发给祖先 ScrollViewer（页面照常滚动）后再 Handled 防改选；
    /// 展开时不拦，滚轮归下拉列表。决策/偏移公式在 Core.WheelForward（无头测试覆盖）。
    /// </summary>
    private static void GuardWheel(ComboBox combo) =>
        combo.PreviewMouseWheel += (_, e) =>
        {
            if (WheelForward.Decide(combo.IsDropDownOpen)
                == WheelForward.Action.LetComboHandle) return;
            var sv = FindAncestor<ScrollViewer>(combo);
            if (sv != null)
                sv.ScrollToVerticalOffset(
                    WheelForward.NewOffset(sv.VerticalOffset, e.Delta, sv.ScrollableHeight));
            e.Handled = true;
        };

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        var cur = VisualTreeHelper.GetParent(node);
        while (cur != null)
        {
            if (cur is T hit) return hit;
            cur = VisualTreeHelper.GetParent(cur);
        }
        return null;
    }

    /// <summary>字体项模板：显示本地化名，且每项以其自身字体渲染（纯代码工厂构建）。</summary>
    private static DataTemplate FontItemTemplate()
    {
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding(nameof(FontChoice.Display)));
        tb.SetBinding(TextBlock.FontFamilyProperty, new Binding(nameof(FontChoice.Family)));
        tb.SetValue(TextBlock.FontSizeProperty, 13.0);
        return new DataTemplate { VisualTree = tb };
    }

    /// <summary>窗口位置记忆（原 ThemedWindow 同语义的最小实现）：有存档则定位，否则主屏居中；关窗时存当前坐标。</summary>
    private void InitPositionMemory(double? savedX, double? savedY, Action<double, double> save)
    {
        if (savedX is double x && savedY is double y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = x;
            Top = y;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        Closing += (_, _) =>
        {
            if (WindowState == WindowState.Normal) save(Left, Top);
        };
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

    /// <summary>卡片内开关行：左标签 + 右库 ToggleSwitch。</summary>
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
        // WPF-UI 4.3 的 ToggleSwitch 没有 Click，用 ToggleButton 的 Checked/Unchecked
        sw.Checked += (_, _) => OnChanged(() => apply(true));
        sw.Unchecked += (_, _) => OnChanged(() => apply(false));
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
            HexBox = new System.Windows.Controls.TextBox { Width = 110, VerticalContentAlignment = VerticalAlignment.Center },
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
            var btn = new System.Windows.Controls.Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(c),
                BorderBrush = Theme.BorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                // v0.7：扁平模板——原生模板悬停会透出底色、颜色铺不满（与颜色盘窗同修）
                Template = Theme.FlatSwatchTemplate(),
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
    /// 打开 HSV 颜色盘（自绘窗保留，配色走当前调色板）：居中于本窗、选色实时回调生效、
    /// 失活自动关（关窗即确认）。非模态打开，重复点只激活已开的那个。异常兜底提示；
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
            System.Windows.MessageBox.Show(this,
                $"颜色盘打开失败（{ex.Message}）。\n可以直接点下方 12 个预设色块，或在输入框填 #AARRGGBB，效果一样实时生效。",
                "选择颜色", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void RefreshOffset()
    {
        long v = _settings.LyricsOffsetMs;
        _offsetValue.Text = v == 0 ? "当前：0 ms" : $"当前：{v:+#;-#;0} ms";
    }

    private void RefreshInstrumentalMarks()
    {
        _marksList.Items.Clear();
        foreach (string k in _settings.InstrumentalKeys) _marksList.Items.Add(k);
        _marksCount.Text = _settings.InstrumentalKeys.Count == 0
            ? "暂无标记。在悬浮窗右击菜单、托盘菜单或手动选词窗里可把当前曲目标记为纯音乐（标题带「纯音乐/Instrumental」等词的也会自动标记）。"
            : $"共 {_settings.InstrumentalKeys.Count} 条（上限 500，新标记自动挤掉最旧的）。标记按「歌名|歌手」记录：";
    }

    private void LoadIntoControls()
    {
        _wordByWord.IsChecked = _settings.WordByWordEnabled;
        _showTrans.IsChecked = _settings.ShowTranslation;
        _simpSwitch.IsChecked = _settings.ConvertTradToSimp;
        _autoTransSwitch.IsChecked = _settings.AutoTranslate;
        _lockSwitch.IsChecked = _settings.PositionLocked;
        _doubleLine.IsChecked = !_settings.SingleLineMode;
        _singleLine.IsChecked = _settings.SingleLineMode;
        _fontSizeBox.Value = _settings.FontSize;
        _bgAlphaBox.Value = _settings.BgAlphaPercent;
        _hiRow.Hex = _settings.HighlightColorHex;
        _textRow.Hex = _settings.TextColorHex;
        foreach (ComboBoxItem item in _themeCombo.Items)
            if (item.Tag as string == _settings.UiTheme) { _themeCombo.SelectedItem = item; break; }
        if (_themeCombo.SelectedItem == null && _themeCombo.Items.Count > 0)
            _themeCombo.SelectedIndex = 0;
        _fontCombo.SelectedItem = null;
        foreach (var item in _fontCombo.Items)
            if (item is FontChoice c && c.Source == _settings.FontFamily)
            { _fontCombo.SelectedItem = c; break; }
        _mxmKey.Text = _settings.MusixmatchApiKey;
        _syncingToken = true;
        _tokenPwd.Password = _settings.GithubToken;
        _tokenText.Text = _settings.GithubToken;
        _syncingToken = false;
        RefreshOffset();
        RefreshInstrumentalMarks();
        SetUpdateStatus(string.IsNullOrWhiteSpace(_settings.GithubToken)
            ? $"当前版本 v{UpdateService.CurrentVersion}。仓库已公开，未填令牌走匿名检查；启动时会自动查一次，也可点「检查更新」。"
            : $"当前版本 v{UpdateService.CurrentVersion}。已填令牌（优先使用）。启动时会自动查一次，也可点「检查更新」。");
    }

    // ---------- 更新分区（供主程序推状态回来） ----------

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
