using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using SpotifyLyricsWin.Core;
using SpotifyLyricsWin.Lyrics;
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
    // v0.8 新增控件（译文与翻译卡 / 诊断与缓存卡 / 更新与启动卡）
    private readonly ComboBox _transServiceCombo;
    private readonly TextBox _mmEmail;
    private readonly TextBox _baiduId;
    private readonly TextBox _baiduKey;
    private readonly TextBox _youdaoId;
    private readonly TextBox _youdaoKey;
    private readonly TextBox _msKey;
    private readonly TextBox _msRegion;
    private readonly StackPanel _credBaidu;
    private readonly StackPanel _credYoudao;
    private readonly StackPanel _credMs;
    private readonly TextBlock _cacheText;
    private readonly ToggleSwitch _autoStartSwitch;
    private readonly ToggleSwitch _openOnStartupSwitch;
    private InstrumentalListWindow? _marksWindow;
    private bool _loading = true;
    // v0.8.2 网格布局：卡片按 id 注册，容器（2×4 网格 / 单列回落）由 ApplyCardLayout 按窗宽切换
    private readonly System.Collections.Generic.Dictionary<string, Card> _cardsById = new();
    private readonly Grid _cardsHost = new();
    private readonly StackPanel _singlePanel = new();
    private bool? _gridMode;

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
    // 系统 ProgressBar（WPF-UI 无自带 ProgressBar 控件，靠 ControlsDictionary 给它套 Fluent 样式）
    private readonly ProgressBar _updateProgress;
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

    /// <summary>翻译服务下拉项：显示名 + 服务 id（v0.8；id 见 Lyrics.TranslateServiceIds）。</summary>
    private static readonly (string Id, string Display)[] TranslateChoices =
    {
        (TranslateServiceIds.Auto, "自动（Google → MyMemory）"),
        (TranslateServiceIds.MyMemory, "MyMemory"),
        (TranslateServiceIds.Google, "Google 翻译"),
        (TranslateServiceIds.Baidu, "百度翻译（需自备 key）"),
        (TranslateServiceIds.Youdao, "有道智云（需自备 key）"),
        (TranslateServiceIds.Microsoft, "微软翻译（需自备 key）")
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

    /// <param name="centerOnScreen">v0.8.2：本次打开是否强制屏幕居中。
    /// 程序**自动打开**的两条路径传 true（启动时自动打开、更新完成重启后的自动打开——
    /// 共用同一入口）：那是程序替用户开的窗，应居中展示；用户手动打开（托盘/悬浮窗/气泡）
    /// 一律走位置记忆。且居中打开**不回写**位置记忆，两条路径互不污染。</param>
    public SettingsWindow(Settings settings, bool centerOnScreen = false)
    {
        _settings = settings;
        // v0.8：版本号动态取程序集版本，别再硬编码（旧标题里写死过旧版号）
        Title = $"Spotify 歌词 · 设置 v{UpdateService.CurrentVersion}";
        // v0.8.2：近方形网格窗（980×730），八卡 2×4 方格排布（见 Core.SettingsLayout）
        Width = SettingsLayout.DefaultWidth;
        Height = SettingsLayout.DefaultHeight;
        MinWidth = SettingsLayout.MinWidth;
        MinHeight = SettingsLayout.MinHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ExtendsContentIntoTitleBar = true;
        // 深色三套全关 Mica（系统材质不吃背景覆写，几套会被罩成一个样，见 Theme.Backdrop），浅色保留
        WindowBackdropType = Theme.Backdrop;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titleBar = new TitleBar
        {
            Title = "Spotify 歌词 · 设置",
            ShowMaximize = false
        };
        WindowIcon.Apply(this);
        if (WindowIcon.LoadImageSource() is { } iconSrc) titleBar.Icon = new ImageIcon { Source = iconSrc };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        _cardsHost.Margin = new Thickness(14, 10, 14, 14);

        // ===== 卡片 1：外观与逐字（含界面主题） =====
        var look = CardPanel("look", "外观与逐字（改动实时生效，自动保存）");

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
        _lockSwitch = AddSwitchRow(look, "锁定悬浮窗位置（锁定后拖不动）",
            v => _settings.PositionLocked = v);

        // ===== 卡片 2：译文与翻译（v0.8 新卡：译文相关从「外观与逐字」拆出独立成卡） =====
        var transBody = CardPanel("trans", "译文与翻译");
        _showTrans = AddSwitchRow(transBody, "显示译文（歌词源带译文时）",
            v => { _settings.ShowTranslation = v; AppLog.WriteLimited("set-showtrans", $"[设置] 显示译文 = {v}", 5000); });
        _simpSwitch = AddSwitchRow(transBody, "繁体转简体显示（原文与自带译文，只转显示不改原文）",
            v => { _settings.ConvertTradToSimp = v; AppLog.WriteLimited("set-simp", $"[设置] 繁体转简体 = {v}", 5000); });
        _autoTransSwitch = AddSwitchRow(transBody, "无译文时自动翻译成中文（机器翻译，所有语种）",
            v => { _settings.AutoTranslate = v; AppLog.WriteLimited("set-autotrans", $"[设置] 无译文自动翻译 = {v}", 5000); });

        transBody.Children.Add(new TextBlock { Text = "翻译服务", FontSize = 13.5, Margin = new Thickness(0, 10, 0, 0) });
        _transServiceCombo = new ComboBox { Margin = new Thickness(0, 6, 0, 4) };
        foreach (var (id, display) in TranslateChoices)
            _transServiceCombo.Items.Add(new ComboBoxItem { Content = display, Tag = id });
        GuardWheel(_transServiceCombo);
        _transServiceCombo.SelectionChanged += (_, _) =>
        {
            if (_transServiceCombo.SelectedItem is ComboBoxItem item && item.Tag is string id)
                OnChanged(() =>
                {
                    _settings.TranslateService = id;
                    AppLog.WriteLimited("set-transsvc", $"[设置] 翻译服务 = {id}", 5000);
                }, UpdateCredVisibility);
        };
        transBody.Children.Add(_transServiceCombo);
        transBody.Children.Add(new TextBlock
        {
            Text = "「自动」= Google 免费通道、失败自动切 MyMemory。选了单个服务会优先用它，" +
                   "失败自动回落这条自动链——不会因为选了路反而翻不出来。百度 / 有道 / 微软需自备免费 key，只存本机。",
            Foreground = Theme.SubTextBrush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap
        });

        transBody.Children.Add(new TextBlock
        {
            Text = "MyMemory 邮箱（可选）", FontSize = 13.5, Margin = new Thickness(0, 12, 0, 0)
        });
        _mmEmail = new TextBox { Margin = new Thickness(0, 6, 0, 0) };
        _mmEmail.TextChanged += (_, _) => OnChanged(() => _settings.MyMemoryEmail = _mmEmail.Text.Trim());
        transBody.Children.Add(_mmEmail);
        transBody.Children.Add(new TextBlock
        {
            Text = "仅用于提高 MyMemory 翻译额度（匿名每天 5000 字符 → 填邮箱 50000 字符）：随翻译请求以 de 参数发给 MyMemory，不做他用；留空则匿名。",
            Foreground = Theme.SubTextBrush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        // 凭据区：按选中的服务动态显示对应一组（UpdateCredVisibility 控制）
        _credBaidu = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        _credBaidu.Children.Add(new TextBlock { Text = "百度 APP ID", FontSize = 12.5 });
        _baiduId = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
        _baiduId.TextChanged += (_, _) => OnChanged(() => _settings.BaiduAppId = _baiduId.Text.Trim());
        _credBaidu.Children.Add(_baiduId);
        _credBaidu.Children.Add(new TextBlock { Text = "百度密钥", FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) });
        _baiduKey = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
        _baiduKey.TextChanged += (_, _) => OnChanged(() => _settings.BaiduKey = _baiduKey.Text.Trim());
        _credBaidu.Children.Add(_baiduKey);
        _credBaidu.Children.Add(new TextBlock
        {
            Text = "申请处：百度翻译开放平台 fanyi-api.baidu.com → 开发者中心创建应用（通用翻译 API），有免费额度。",
            Foreground = Theme.SubTextBrush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        transBody.Children.Add(_credBaidu);

        _credYoudao = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        _credYoudao.Children.Add(new TextBlock { Text = "有道应用 ID", FontSize = 12.5 });
        _youdaoId = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
        _youdaoId.TextChanged += (_, _) => OnChanged(() => _settings.YoudaoAppId = _youdaoId.Text.Trim());
        _credYoudao.Children.Add(_youdaoId);
        _credYoudao.Children.Add(new TextBlock { Text = "有道应用密钥", FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) });
        _youdaoKey = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
        _youdaoKey.TextChanged += (_, _) => OnChanged(() => _settings.YoudaoKey = _youdaoKey.Text.Trim());
        _credYoudao.Children.Add(_youdaoKey);
        _credYoudao.Children.Add(new TextBlock
        {
            Text = "申请处：有道智云 ai.youdao.com → 创建应用并绑定「文本翻译」服务，新用户有体验额度。",
            Foreground = Theme.SubTextBrush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        transBody.Children.Add(_credYoudao);

        _credMs = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        _credMs.Children.Add(new TextBlock { Text = "微软翻译密钥", FontSize = 12.5 });
        _msKey = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
        _msKey.TextChanged += (_, _) => OnChanged(() => _settings.MsTranslatorKey = _msKey.Text.Trim());
        _credMs.Children.Add(_msKey);
        _credMs.Children.Add(new TextBlock { Text = "微软区域（可留空 = 全球端点）", FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) });
        _msRegion = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
        _msRegion.TextChanged += (_, _) => OnChanged(() => _settings.MsTranslatorRegion = _msRegion.Text.Trim());
        _credMs.Children.Add(_msRegion);
        _credMs.Children.Add(new TextBlock
        {
            Text = "申请处：Azure 门户创建「翻译」资源（F0 免费层每月 200 万字符），在「密钥和终结点」里拿密钥，区域填资源所在区域如 eastasia。",
            Foreground = Theme.SubTextBrush, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        transBody.Children.Add(_credMs);

        // ===== 卡片 3：颜色 =====
        var colorBody = CardPanel("color", "颜色");
        _hiRow = BuildColorRow(colorBody, "当前行高亮颜色（已唱过的部分）", first: true,
            () => _settings.HighlightColorHex, v => _settings.HighlightColorHex = v);
        _textRow = BuildColorRow(colorBody, "其余文字颜色（未唱部分 / 下一行）", first: false,
            () => _settings.TextColorHex, v => _settings.TextColorHex = v);
        // v0.8：恢复默认外观按钮挪进本卡末尾（原先单独吊在整窗最底部）；文案与逻辑不变
        var reset = new Button { Content = "恢复默认外观（纯文字 + 绿色平滑逐字）", Height = 34, Margin = new Thickness(0, 12, 0, 0) };
        reset.Click += (_, _) =>
        {
            _loading = true;
            _settings.ResetStyle();
            LoadIntoControls();
            _loading = false;
            LiveChanged?.Invoke();
            _settings.RequestSave();
        };
        colorBody.Children.Add(reset);

        // ===== 卡片 4：背景与时间 =====
        var timeBody = CardPanel("time", "背景与时间");
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

        // ===== 卡片 5：字体与歌词源 =====
        var fontBody = CardPanel("font", "字体与歌词源");
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

        // ===== 卡片 6：纯音乐标记 =====
        var marks = CardPanel("marks", "纯音乐标记（被标记的曲目不再取词）");
        _marksCount = new TextBlock { Foreground = Theme.SubTextBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        marks.Children.Add(_marksCount);
        // v0.8：名单管理独立成窗（逐条移除 + 清空全部都在那边），本卡只留计数 + 入口按钮
        var viewMarks = new Button { Content = "查看已标记歌曲…", Height = 30, Margin = new Thickness(0, 8, 0, 0) };
        viewMarks.Click += (_, _) => OpenMarksWindow();
        marks.Children.Add(viewMarks);

        // ===== 卡片 7：诊断与缓存 =====
        var logBody = CardPanel("log", "诊断与缓存（日志 + 歌词缓存）");
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
        // v0.8：歌词缓存统计与清空（只删缓存文件，设置与纯音乐标记不在缓存目录、不受影响）
        logBody.Children.Add(new TextBlock { Text = "歌词缓存", FontSize = 13.5, Margin = new Thickness(0, 14, 0, 0) });
        _cacheText = new TextBlock { Foreground = Theme.SubTextBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        logBody.Children.Add(_cacheText);
        var clearCacheBtn = new Button { Content = "清空歌词缓存", Height = 30, Margin = new Thickness(0, 8, 0, 0) };
        clearCacheBtn.Click += (_, _) =>
        {
            int removed = CacheStats.Clear(Settings.CacheDir);
            AppLog.Write($"[缓存] 已清空歌词缓存（删除 {removed} 个文件）");
            RefreshCacheStats();
        };
        logBody.Children.Add(clearCacheBtn);

        // ===== 卡片 8：更新与启动 =====
        var upd = CardPanel("update", "更新与启动");
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
        // v0.8.2：下载进度条（平时藏起，下载时由主程序经 SetUpdateProgress 驱动；
        // 文案同步写在 _updateStatus 里：已下载 MB / 总 MB · 百分比）
        _updateProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };
        upd.Children.Add(_updateProgress);
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
        // v0.8：开机自动启动（走 HKCU Run 注册表，见 Ui.AutoStartRegistry）
        _autoStartSwitch = AddSwitchRow(upd, "开机自动启动（登录 Windows 后自动运行）",
            v => SetAutoStart(v));
        // v0.8.2：启动时自动打开设置页（默认开；更新后重启也走这条自动打开路径）
        _openOnStartupSwitch = AddSwitchRow(upd, "启动时打开设置页（程序启动后自动打开一次，居中显示）",
            v => { _settings.OpenSettingsOnStartup = v; AppLog.WriteLimited("set-openonstartup", $"[设置] 启动时打开设置页 = {v}", 5000); });

        // 没有「完成」按钮——所有改动实时生效，关窗请用标题栏 ✕。

        var scroll = new ScrollViewer
        {
            Content = _cardsHost,
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
        }, centerOnScreen);

        // v0.8.2：卡片排布按窗宽切换 2×4 网格/单列回落（阈值见 SettingsLayout.TwoColumnMinWidth）
        ApplyCardLayout(SettingsLayout.UseTwoColumns(Width));
        SizeChanged += (_, _) => ApplyCardLayout(SettingsLayout.UseTwoColumns(ActualWidth));

        LoadIntoControls();
        _loading = false;
    }

    /// <summary>
    /// 卡片容器（v0.8.1 改注册制）：库 Card + 标题行 + 内容面板，返回内容面板供填充；
    /// Card 本身先按 id 登记进 _cardsById，挂到哪列由 ApplyCardLayout 统一排（双列/单列可切换）。
    /// </summary>
    private StackPanel CardPanel(string id, string title)
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
        _cardsById[id] = card;
        return body;
    }

    /// <summary>
    /// v0.8.2 卡片排布：双列时把八张卡放进 **2 列 × 4 行** 的 Grid 方格——行高取同行
    /// 两卡的较高者（行定义 Auto + 卡片 VerticalAlignment=Stretch 拉伸填满），
    /// 同行左右严格等高、方方正正；单列回落按网格阅读序顺排（StackPanel）。
    /// 行列序全在 Core.SettingsLayout，本方法只搬控件；模式未变时直接返回。
    /// 网格整体仍装在外层 ScrollViewer 里：窗口被拉矮/内容超高时滚动兜底，卡不被压坏。
    /// </summary>
    private void ApplyCardLayout(bool grid)
    {
        if (_gridMode == grid) return;
        _gridMode = grid;
        foreach (var card in _cardsById.Values)
            (card.Parent as Panel)?.Children.Remove(card);
        _cardsHost.Children.Clear();
        _cardsHost.ColumnDefinitions.Clear();
        _cardsHost.RowDefinitions.Clear();
        _singlePanel.Children.Clear();
        if (grid)
        {
            _cardsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _cardsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < SettingsLayout.GridRows.Length; r++)
            {
                _cardsHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                string[] row = SettingsLayout.GridRows[r];
                for (int c = 0; c < row.Length; c++)
                {
                    var card = _cardsById[row[c]];
                    bool lastRow = r == SettingsLayout.GridRows.Length - 1;
                    card.Margin = new Thickness(
                        c == 1 ? SettingsLayout.ColumnGap : 0, 0, 0,
                        lastRow ? 0 : SettingsLayout.RowGap);
                    card.VerticalAlignment = VerticalAlignment.Stretch;
                    Grid.SetRow(card, r);
                    Grid.SetColumn(card, c);
                    _cardsHost.Children.Add(card);
                }
            }
        }
        else
        {
            _cardsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach (string id in SettingsLayout.SingleColumn)
            {
                var card = _cardsById[id];
                card.Margin = new Thickness(0, 0, 0, 10);
                card.VerticalAlignment = VerticalAlignment.Stretch;
                _singlePanel.Children.Add(card);
            }
            Grid.SetColumn(_singlePanel, 0);
            _cardsHost.Children.Add(_singlePanel);
        }
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

    /// <summary>
    /// 窗口位置记忆（原 ThemedWindow 同语义的最小实现）：有存档则定位，否则主屏居中；关窗时存当前坐标。
    /// v0.8.2：centerOnScreen 时强制居中且**不读不写**位置记忆——自动打开（启动/更新后）
    /// 的居中展示是一次性的，不能把用户手动摆放的记忆位置冲掉。
    /// </summary>
    private void InitPositionMemory(double? savedX, double? savedY, Action<double, double> save,
        bool centerOnScreen = false)
    {
        if (centerOnScreen)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
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
        _marksCount.Text = _settings.InstrumentalKeys.Count == 0
            ? "暂无标记。在悬浮窗右击菜单、托盘菜单或手动选词窗里可把当前曲目标记为纯音乐（标题带「纯音乐/Instrumental」等词的也会自动标记）。"
            : $"共 {_settings.InstrumentalKeys.Count} 条（上限 500，新标记自动挤掉最旧的）。";
    }

    /// <summary>v0.8：打开纯音乐名单独立窗（单例；窗内变更后回刷本卡计数）。</summary>
    private void OpenMarksWindow()
    {
        try
        {
            if (_marksWindow != null) { _marksWindow.Activate(); return; }
            var win = new InstrumentalListWindow(_settings, () =>
            {
                RefreshInstrumentalMarks();
                InstrumentalMarksChanged?.Invoke();
            })
            { Owner = this };
            _marksWindow = win;
            win.Closed += (_, _) => { if (ReferenceEquals(_marksWindow, win)) _marksWindow = null; };
            win.Show();
        }
        catch (Exception ex)
        {
            AppLog.Write($"[纯音乐] 名单窗打开失败：{ex.Message}");
        }
    }

    /// <summary>v0.8：翻译凭据区按选中服务动态显示（自动/Google/MyMemory 时三组全收起）。</summary>
    private void UpdateCredVisibility()
    {
        string svc = _settings.TranslateService;
        _credBaidu.Visibility = svc == TranslateServiceIds.Baidu ? Visibility.Visible : Visibility.Collapsed;
        _credYoudao.Visibility = svc == TranslateServiceIds.Youdao ? Visibility.Visible : Visibility.Collapsed;
        _credMs.Visibility = svc == TranslateServiceIds.Microsoft ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>v0.8：刷新歌词缓存统计文案。</summary>
    private void RefreshCacheStats()
    {
        var (songs, bytes) = CacheStats.Measure(Settings.CacheDir);
        _cacheText.Text = "本地缓存：" + CacheStats.Format(songs, bytes) +
            "（清掉后下次播放会重新联网取词；设置与纯音乐标记不受影响）";
    }

    /// <summary>
    /// v0.8：开机自启开关落地——写 HKCU Run 注册表；写失败记日志并把开关显示
    /// 回滚到注册表实际状态（AddSwitchRow 的事件里 _loading 守卫防递归触发）。
    /// </summary>
    private void SetAutoStart(bool enable)
    {
        if (!AutoStartRegistry.SetEnabled(enable))
        {
            bool actual = AutoStartRegistry.IsEnabled();
            AppLog.Write($"[自启] 注册表写入失败，开机自启保持{(actual ? "开启" : "关闭")}");
            _loading = true;
            _autoStartSwitch.IsChecked = actual;
            _loading = false;
            return;
        }
        _settings.AutoStartEnabled = enable;
        AppLog.Write($"[自启] 开机自动启动 = {enable}");
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
        // v0.8：译文与翻译卡的回填
        _transServiceCombo.SelectedItem = null;
        foreach (ComboBoxItem item in _transServiceCombo.Items)
            if (item.Tag as string == _settings.TranslateService) { _transServiceCombo.SelectedItem = item; break; }
        if (_transServiceCombo.SelectedItem == null && _transServiceCombo.Items.Count > 0)
            _transServiceCombo.SelectedIndex = 0;
        _mmEmail.Text = _settings.MyMemoryEmail;
        _baiduId.Text = _settings.BaiduAppId;
        _baiduKey.Text = _settings.BaiduKey;
        _youdaoId.Text = _settings.YoudaoAppId;
        _youdaoKey.Text = _settings.YoudaoKey;
        _msKey.Text = _settings.MsTranslatorKey;
        _msRegion.Text = _settings.MsTranslatorRegion;
        UpdateCredVisibility();
        RefreshCacheStats();
        // v0.8：开机自启以注册表实际值为准对齐（设置里的记录与注册表不一致时回写）
        bool autoStart = AutoStartRegistry.IsEnabled();
        if (autoStart != _settings.AutoStartEnabled)
        {
            _settings.AutoStartEnabled = autoStart;
            _settings.RequestSave();
        }
        _autoStartSwitch.IsChecked = autoStart;
        // v0.8.2：启动时打开设置页（纯设置项，无外部状态对齐）
        _openOnStartupSwitch.IsChecked = _settings.OpenSettingsOnStartup;
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

    /// <summary>
    /// v0.8.2：下载进度呈现——进度条 + 状态文案（已下载 MB / 总 MB · 百分比；
    /// 总大小未知时进度条转不定量、文案只报已下载量）。文案口径在 Core.DownloadProgressText。
    /// </summary>
    public void SetUpdateProgress(long downloadedBytes, long totalBytes)
    {
        _updateProgress.Visibility = Visibility.Visible;
        double pct = DownloadProgressText.Percent(downloadedBytes, totalBytes);
        if (pct >= 0)
        {
            _updateProgress.IsIndeterminate = false;
            _updateProgress.Value = pct;
        }
        else
        {
            _updateProgress.IsIndeterminate = true;
        }
        _updateStatus.Text = DownloadProgressText.Format(downloadedBytes, totalBytes);
    }

    /// <summary>v0.8.2：下载结束（成功/失败都算）后收起进度条，状态文案由调用方接管。</summary>
    public void HideUpdateProgress()
    {
        _updateProgress.Visibility = Visibility.Collapsed;
        _updateProgress.IsIndeterminate = false;
    }

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
