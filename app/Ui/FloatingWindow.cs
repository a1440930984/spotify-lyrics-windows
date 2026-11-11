using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 桌面悬浮歌词窗（参数照复刻规格）：
/// 无边框、透明、置顶、不进任务栏；当前行（渐变扫光）+ 译文行 + 下一行；
/// 长句缩字号、切行上滑淡入、停播淡出 0.38、拖动阈值 8 保存位置。
/// v0.2 新增：锁定位置按钮（挨着关闭键）、右击菜单、单/双行模式、
/// 窗口宽度按内容自适应（居中以当前行文本为准，修 v0.1 单行不居中）。
/// v0.7 按钮带重做：改 Overlay 绝对层（悬浮窗顶部居中固定锚点，**不占布局高度**，
/// 单/双行、长短句位置恒定——v0.6 把带放进布局行，双行时会被歌词顶上去）；
/// 显隐改纯悬停语义：鼠标进歌词区才淡入（120ms）、离开立即淡出（80ms 后收起），
/// 不再有 3 秒定时与高度动画。锁定状态下悬停也不显示（鼠标本就为拖动/穿透让路，
/// 解锁走右击菜单/托盘的「锁定位置」）。
/// v0.7 译文行换成渐变行控件：与当前行共用行进度做整行比例扫光（按译文字数取整到字粒度，
/// 近似逐字）；译文字号改比例跟随（Core.LayoutMath.TranslationFontSize）。
/// v0.7 背景与主题彻底解耦：Window.Background 恒为透明（ApplyStyle 每次重申），
/// 可见背景只有根 Border 一处、且只由「背景透明度」设置决定——主题切换走的是另一套
/// 资源系统，碰不到这里（v0.6 真机出现过切主题后悬浮窗背景变实、改透明度无效：
/// ApplyStyle 当时只写根 Border、从不重申 Window.Background，一旦被外部改动就回不来）。
/// </summary>
public sealed class FloatingWindow : Window
{
    private readonly Settings _settings;
    private readonly Border _rootBorder;
    private readonly GradientLyricLine _currentLine;
    private readonly GradientLyricLine _translationLine;
    private readonly TextBlock _nextBlock;
    private readonly Button _closeButton;
    private readonly Button _lockButton;
    private readonly MenuItem _lockMenuItem;
    private Border _bandCapsule = null!;
    private MenuItem _notAdMenuItem = null!;
    private MenuItem _instrumentalMenuItem = null!;
    private bool _bandShown;

    /// <summary>按钮带最小宽度：两钮 36+2+36 + 胶囊内边距 5×2 + 描边 ≈ 86，取 92 留余量。
    /// 窗口宽度取「歌词行宽」与它的较大者，按钮带又恒居中，锁钮永远在窗口中轴线上。</summary>
    private const double BandMinWidth = 92;

    private string _lastCurrent = "\0";
    private bool _dimmed;
    /// <summary>v0.8：广告提示模式——提示行强制固定白色，不吃用户的高亮/文字颜色设置。</summary>
    private bool _adHintActive;
    private Point _dragStartScreen;
    private double _dragStartLeft, _dragStartTop;
    private bool _mouseDown, _dragging;

    /// <summary>点关闭键 / 右击菜单「隐藏歌词」：隐藏悬浮窗</summary>
    public event Action? CloseRequested;
    /// <summary>双击 / 右击菜单：打开设置</summary>
    public event Action? SettingsRequested;
    /// <summary>右击菜单：手动选词（v0.2）</summary>
    public event Action? ManualPickRequested;
    /// <summary>右击菜单：重新搜词（v0.2）</summary>
    public event Action? RefetchRequested;
    /// <summary>右击菜单「这不是广告」（v0.6，仅广告提示状态下可用）</summary>
    public event Action? NotAdRequested;
    /// <summary>右击菜单「标记为纯音乐 / 取消标记」（v0.6）</summary>
    public event Action? ToggleInstrumentalRequested;

    /// <summary>当前是否正显示广告提示（Program 维护；决定右击菜单「这不是广告」是否可用）。</summary>
    public bool IsAdShowing { get; set; }
    /// <summary>当前曲目是否已被标记为纯音乐（Program 维护；决定菜单项文案）。</summary>
    public bool IsInstrumental { get; set; }

    public FloatingWindow(Settings settings)
    {
        _settings = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 880;

        // v0.8 背景「雾面」定因（实锤）：歌词文字此前挂了**双重阴影**——GradientLyricLine
        // 的 OnRender 内部已按字形手绘一遍偏移阴影，外面又给每行套了一个 DropShadowEffect。
        // WPF 的 DropShadowEffect 是对元素整块位图（含行盒的全部透明区）做高斯模糊，
        // 模糊核把阴影涂满行盒的矩形范围；在浅色桌面上就成了用户截图里那层
        // 「浅色雾面矩形」——与背景画刷无关（窗口/根 Border 经审计：Window.Background
        // 恒透明、根 Border 在透明度 0 时 Background=Transparent、无边框、无其他色层）。
        // 处置：渐变行去掉 Effect，只留控件内部那遍字形阴影（跟着字走、不成矩形）；
        // 下一行 TextBlock 没有内部阴影，保留 Effect 但降到很轻（模糊 2、透明 0.5）。
        _currentLine = new GradientLyricLine();

        // 译文用渐变行控件（与当前行同机制：按进度做字粒度渐变扫光）；
        // v0.8 排版：常规字重 + 颜色 alpha 0.78 在 ApplyStyle 的 Configure 里给，
        // 这里只管间距——原文↔译文贴紧到 2（见 _translationLine.Margin），成组感靠
        // 下一行的大留白（_nextBlock.Margin 7）拉开。
        _translationLine = new GradientLyricLine
        {
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed
        };
        var nextShadow = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.5,
            Direction = 270,
            ShadowDepth = 1,
            BlurRadius = 2
        };
        _nextBlock = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0),
            Opacity = 0.6,
            Effect = nextShadow
        };

        var stack = new StackPanel();
        stack.Children.Add(_currentLine);
        stack.Children.Add(_translationLine);
        stack.Children.Add(_nextBlock);

        // ---- 按钮组：锁定 + 关闭（Overlay 层，悬停才显示） ----
        // 胶囊衬底保留 v0.5 的决定：深色半透明底让图标在白底黑底下都清晰。
        var buttonShadow = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.95,
            Direction = 270,
            ShadowDepth = 1,
            BlurRadius = 3
        };
        _lockButton = new Button
        {
            Width = 36,
            Height = 28,
            Visibility = Visibility.Collapsed,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 15,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 2, 0),
            ToolTip = "锁定/解锁位置",
            Effect = buttonShadow,
            FontFamily = new FontFamily("Segoe UI Emoji, Microsoft YaHei UI"),
            // 幽灵按钮（v0.6）：自绘模板 + 无焦点框，杜绝默认模板的选中/聚焦系统底色
            Template = GhostButtonTemplate(),
            FocusVisualStyle = null,
            Focusable = false
        };
        _lockButton.Click += (_, _) => ToggleLock();
        _closeButton = new Button
        {
            Content = "✕",
            Width = 36,
            Height = 28,
            Visibility = Visibility.Collapsed,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            Cursor = Cursors.Hand,
            Effect = buttonShadow,
            ToolTip = "隐藏歌词",
            Template = GhostButtonTemplate(),
            FocusVisualStyle = null,
            Focusable = false
        };
        _closeButton.Click += (_, _) => CloseRequested?.Invoke();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_lockButton);
        buttons.Children.Add(_closeButton);

        var capsule = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x14, 0x14, 0x14)), // rgba(20,20,20,0.55)
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            Child = buttons
        };

        // v0.8 布局：两行 Grid —— 行 0 是给按钮带预留的透明留白（高 = LayoutMath.BandReserveTop，
        // 全透明、无任何背景），歌词栈放行 1；按钮带 capsule 叠在行 0 内贴顶居中。
        // 这样「带底边（32）≤ 第一行顶边（36）」由构造保证，双行/大字号也不会再压字
        // （v0.7 带直接锚在歌词块顶部，胶囊正好压住第一行字形上沿，Sun 截图实锤）。
        // 留白是纯透明区，不算背景——窗口透明度=0 时它什么都不画。
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LayoutMath.BandReserveTop) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(stack, 1);
        grid.Children.Add(stack);
        Grid.SetRow(capsule, 0);
        grid.Children.Add(capsule);
        _bandCapsule = capsule;

        _rootBorder = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(16),
            Child = grid
        };
        Content = _rootBorder;

        // ---- 右击菜单（v0.2；v0.3 套深色样式与三窗统一） ----
        var menu = new ContextMenu { Style = Theme.ContextMenuStyle() };
        var miSettings = new MenuItem { Header = "打开设置…", Style = Theme.MenuItemStyle() };
        miSettings.Click += (_, _) => SettingsRequested?.Invoke();
        var miPick = new MenuItem { Header = "手动选词…", Style = Theme.MenuItemStyle() };
        miPick.Click += (_, _) => ManualPickRequested?.Invoke();
        var miRefetch = new MenuItem { Header = "重新搜词", Style = Theme.MenuItemStyle() };
        miRefetch.Click += (_, _) => RefetchRequested?.Invoke();
        _notAdMenuItem = new MenuItem { Header = "这不是广告", Style = Theme.MenuItemStyle() };
        _notAdMenuItem.Click += (_, _) => NotAdRequested?.Invoke();
        _instrumentalMenuItem = new MenuItem { Header = "标记为纯音乐", Style = Theme.MenuItemStyle() };
        _instrumentalMenuItem.Click += (_, _) => ToggleInstrumentalRequested?.Invoke();
        _lockMenuItem = new MenuItem { Header = "锁定位置", IsCheckable = true, Style = Theme.MenuItemStyle() };
        _lockMenuItem.Click += (_, _) =>
        {
            _settings.PositionLocked = _lockMenuItem.IsChecked;
            _settings.RequestSave();
            UpdateLockButton();
        };
        var miHide = new MenuItem { Header = "隐藏歌词", Style = Theme.MenuItemStyle() };
        miHide.Click += (_, _) => CloseRequested?.Invoke();
        menu.Items.Add(miSettings);
        menu.Items.Add(miPick);
        menu.Items.Add(miRefetch);
        menu.Items.Add(_notAdMenuItem);
        menu.Items.Add(_instrumentalMenuItem);
        menu.Items.Add(_lockMenuItem);
        menu.Items.Add(new Separator { Style = Theme.MenuSeparatorStyle() });
        menu.Items.Add(miHide);
        menu.Opened += (_, _) =>
        {
            _lockMenuItem.IsChecked = _settings.PositionLocked;
            // 「这不是广告」只在正显示广告提示时可点；纯音乐菜单项按当前标记状态换文案
            _notAdMenuItem.IsEnabled = IsAdShowing;
            _instrumentalMenuItem.Header = IsInstrumental ? "取消纯音乐标记" : "标记为纯音乐";
        };
        ContextMenu = menu;

        // 内容高度变化（单/双行切换、字号变化）时保持窗口垂直中心不动，用户感知的歌词位置不跳。
        // 按钮带已移出布局流，不再有带高动画需要豁免。
        double lastHeight = -1;
        SizeChanged += (_, e) =>
        {
            if (lastHeight > 0 && !_dragging &&
                Math.Abs(e.NewSize.Height - lastHeight) > 0.5 && !double.IsNaN(Top))
            {
                Top += (lastHeight - e.NewSize.Height) / 2.0;
            }
            lastHeight = e.NewSize.Height;
        };

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        // v0.7 悬停显隐：进歌词区淡入、离开立即淡出；锁定状态不显示（解锁走右击菜单/托盘）。
        MouseEnter += (_, _) => { if (!_settings.PositionLocked) ShowBand(); };
        MouseLeave += (_, _) => HideBand();
        MouseDoubleClick += (_, _) => SettingsRequested?.Invoke();

        // 初始位置：设置里存的坐标，或顶部居中（安卓 floatingY 默认 120）
        if (settings.FloatingX is double fx && settings.FloatingY is double fy)
        { Left = fx; Top = fy; }
        else
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + 120;
        }
        UpdateLockButton();
        ApplyStyle();
    }

    // ---------- 锁定 ----------

    private void ToggleLock()
    {
        _settings.PositionLocked = !_settings.PositionLocked;
        _settings.RequestSave();
        UpdateLockButton();
        // 刚锁定：按钮带按新规则立刻收起（锁定后悬停也不显示）；刚解锁：保持显示方便确认
        if (_settings.PositionLocked) HideBand(); else ShowBand();
    }

    private void UpdateLockButton()
    {
        // 🔒 = 已锁定（不可拖）；🔓 = 未锁定。v0.6：锁定态改用绿色图标本身表达状态，
        // 不再刷整块底色（用户反馈底色出戏）；背景恒透明，悬停高亮由幽灵模板负责。
        _lockButton.Content = _settings.PositionLocked ? "🔒" : "🔓";
        _lockButton.Foreground = _settings.PositionLocked ? Theme.AccentBrush : Brushes.White;
        _lockButton.Background = Brushes.Transparent;
    }

    /// <summary>
    /// 幽灵按钮模板（v0.6，纯代码构建）：默认 Button 模板在聚焦/按下/悬停时会画系统
    /// 描边与底色，在半透明悬浮窗上很出戏（用户实测反馈）。换成全自绘模板：
    /// 透明底、圆角 6、无焦点框；悬停/按下只在按钮自身范围叠一层白色微光（14%/22%）。
    /// </summary>
    private static ControlTemplate GhostButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty,
            new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Button.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF))));
        template.Triggers.Add(pressed);
        return template;
    }

    // ---------- 按钮带显隐（v0.7：悬停语义，Overlay 层只动透明度） ----------

    private void ShowBand()
    {
        if (_bandShown) return;
        _bandShown = true;
        _bandCapsule.Visibility = Visibility.Visible;
        _closeButton.Visibility = Visibility.Visible;
        _lockButton.Visibility = Visibility.Visible;
        _bandCapsule.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120)));
    }

    private void HideBand()
    {
        if (!_bandShown) return;
        _bandShown = false;
        // 离开立即消失：80ms 淡出后收起，不拖泥带水（Sun 明确要求）
        var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(80));
        anim.Completed += (_, _) =>
        {
            if (_bandShown) return; // 淡出途中又悬停回来了
            _bandCapsule.Visibility = Visibility.Collapsed;
            _closeButton.Visibility = Visibility.Collapsed;
            _lockButton.Visibility = Visibility.Collapsed;
        };
        _bandCapsule.BeginAnimation(OpacityProperty, anim);
    }

    // ---------- 样式 ----------

    public static Color ParseHex(string hex, Color fallback)
    {
        try
        {
            object? o = ColorConverter.ConvertFromString(hex.Trim());
            return o is Color c ? c : fallback;
        }
        catch { return fallback; }
    }

    public void ApplyStyle()
    {
        // v0.7 解耦铁律：窗口本体背景恒透明，每次套样式都重申——主题系统或任何外部
        // 改动都不能让悬浮窗长出底色；可见背景只有下方根 Border 一处。
        Background = Brushes.Transparent;

        var hi = ParseHex(_settings.HighlightColorHex, Color.FromArgb(0xFF, 0x1E, 0xD7, 0x60));
        var normal = ParseHex(_settings.TextColorHex, Colors.White);
        double size = _settings.FontSize;

        // v0.8：广告提示期间当前行强制固定默认色（纯白），与用户颜色设置无关
        _currentLine.Configure(
            _adHintActive ? Colors.White : hi,
            _adHintActive ? Colors.White : normal,
            size, _settings.FontFamily);

        // 译文字号比例跟随（round（原文 × 0.62)，钳制规则见 LayoutMath.TranslationFontSize）；
        // v0.8 排版规格：字重 Normal（原文 Bold，主次分明）、两色同乘 alpha 0.78
        // （译文整体退后一层；扫光已唱段也在 0.78 的高亮色上走，不与原文的满色高亮撞）。
        _translationLine.Configure(hi, normal,
            LayoutMath.TranslationFontSize(size), _settings.FontFamily,
            FontWeights.Normal, 0.78);

        var ff = new FontFamily(_settings.FontFamily);
        _nextBlock.FontFamily = ff;
        _nextBlock.FontWeight = FontWeights.Bold;
        _nextBlock.FontSize = Math.Max(10, size - 4);         // 下一行 -4，下限 10
        _nextBlock.Foreground = new SolidColorBrush(normal);
        if (!_dimmed) _nextBlock.Opacity = 0.6;

        // 唯一的背景来源：「背景透明度」设置（0 = 全透明纯文字）
        int alpha = _settings.BgAlphaPercent;
        _rootBorder.Background = alpha <= 0
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb((byte)(alpha * 255 / 100), 0, 0, 0));

        UpdateLockButton();
        AdaptWidth();
    }

    // ---------- 宽度自适应（v0.2 居中修正） ----------

    private double MeasurePlain(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(_settings.FontFamily), FontStyles.Normal,
                FontWeights.Bold, FontStretches.Normal),
            fontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return ft.WidthIncludingTrailingWhitespace;
    }

    /// <summary>
    /// 窗口宽度 = 当前可见各行自然宽度的最大值 + 内边距（上限为工作区宽度）。
    /// v0.1 固定 880 宽：短歌词行在 880 的空窗里居中，窗口摆位稍偏就显得「不居中」；
    /// 现在宽度以内容为准——单行模式即以当前行文本为准，当前行必然正中。
    /// 宽度变化时保持窗口中心不动，避免视觉跳动。
    /// </summary>
    private void AdaptWidth()
    {
        double needed = _currentLine.NaturalWidth();
        if (_nextBlock.Visibility == Visibility.Visible)
            needed = Math.Max(needed, MeasurePlain(_nextBlock.Text, Math.Max(10, _settings.FontSize - 4)));
        if (_translationLine.Visibility == Visibility.Visible)
            needed = Math.Max(needed, _translationLine.NaturalWidth());
        // 窗口宽度不小于按钮带宽——按钮带居中、歌词居中，长短句共用同一中轴，
        // 锁钮位置与歌词长度彻底脱钩（用户验收点）。
        needed = Math.Max(needed, BandMinWidth);
        needed += 12 + 2; // 根内边距 6×2 + 余量
        var wa = SystemParameters.WorkArea;
        double w = Math.Clamp(needed, 200, Math.Max(200, wa.Width));
        if (Math.Abs(w - Width) < 1 || double.IsNaN(Left)) { Width = w; return; }
        double center = Left + Width / 2;
        Width = w;
        Left = Math.Clamp(center - w / 2, wa.Left - w + 100, wa.Right - 100);
    }

    // ---------- 内容 ----------

    /// <summary>切行时调用：设置文本 + 切行动画。同一行重复调用只更新进度不重播动画。</summary>
    public void SetLine(string current, string next, string translation)
    {
        if (_adHintActive) { _adHintActive = false; ApplyStyle(); }
        bool changed = current != _lastCurrent;
        _lastCurrent = current;
        _currentLine.SetText(current);

        // 单行模式：不显示下一行（译文行仍按其开关）
        bool showNext = !_settings.SingleLineMode && !string.IsNullOrEmpty(next);
        _nextBlock.Text = showNext ? next : "";
        _nextBlock.Visibility = showNext ? Visibility.Visible : Visibility.Collapsed;

        string tr = _settings.ShowTranslation ? translation ?? "" : "";
        _translationLine.SetText(tr);
        _translationLine.Visibility = string.IsNullOrWhiteSpace(tr)
            ? Visibility.Collapsed : Visibility.Visible;

        AdaptWidth();

        if (changed && current.Length > 0)
        {
            // 当前行：上滑 16→0 + 淡入 0.2→1，280ms，减速
            var tt = new TranslateTransform(0, 16);
            _currentLine.RenderTransform = tt;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
            _currentLine.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.2, _dimmed ? 0.38 : 1.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
            // 下一行：0.25→0.6，260ms
            if (showNext)
                _nextBlock.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.25, 0.6, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }
    }

    /// <summary>纯提示行（等待播放/正在找歌词/未找到）：无逐字、正常色整行显示。</summary>
    public void SetHint(string hint)
    {
        if (_adHintActive) { _adHintActive = false; ApplyStyle(); }
        ShowHintCore(hint);
    }

    /// <summary>
    /// 广告提示行（v0.8）：文案精简为「♪ 广告播放中」，且颜色固定默认白——
    /// 不随用户改高亮/文字颜色而变（Sun 要求）。进入/退出该模式时重套一次配色，
    /// tick 每帧重复调用本方法不会反复重排（SetText 自带同文早退）。
    /// </summary>
    public void SetAdHint(string hint)
    {
        if (!_adHintActive) { _adHintActive = true; ApplyStyle(); }
        ShowHintCore(hint);
    }

    private void ShowHintCore(string hint)
    {
        _lastCurrent = hint;
        _currentLine.SetText(hint);
        _currentLine.SetProgress(0f, false);
        _nextBlock.Text = "";
        _nextBlock.Visibility = Visibility.Collapsed;
        _translationLine.SetText("");
        _translationLine.Visibility = Visibility.Collapsed;
        AdaptWidth();
    }

    public void SetProgress(float fraction, bool wordByWord)
    {
        _currentLine.SetProgress(fraction, wordByWord);
        // v0.7：译文与当前行共用行进度扫光（渐变行内部按译文自身字数取字粒度边界）
        if (_translationLine.Visibility == Visibility.Visible)
            _translationLine.SetProgress(fraction, wordByWord);
    }

    /// <summary>停播/无会话/广告：整块淡出到 0.38；恢复播放回到 1。</summary>
    public void SetDimmed(bool dimmed)
    {
        if (_dimmed == dimmed) return;
        _dimmed = dimmed;
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(dimmed ? 0.38 : 1.0, TimeSpan.FromMilliseconds(250)));
    }

    // ---------- 拖动 / 轻点（照安卓 attachDrag：阈值 8px；锁定时不拖） ----------

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (node == ancestor) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private bool IsOnButton(MouseButtonEventArgs e) =>
        IsWithin(e.OriginalSource as DependencyObject, _closeButton) ||
        IsWithin(e.OriginalSource as DependencyObject, _lockButton);

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOnButton(e)) return;            // 按钮自己处理点击
        if (_settings.PositionLocked) return; // 锁定：整窗不可拖（菜单仍可开、解锁在菜单里）
        _mouseDown = true;
        _dragging = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown) return;
        var now = PointToScreen(e.GetPosition(this));
        double dx = now.X - _dragStartScreen.X;
        double dy = now.Y - _dragStartScreen.Y;
        if (!_dragging && (Math.Abs(dx) > 8 || Math.Abs(dy) > 8)) _dragging = true;
        if (_dragging)
        {
            // PointToScreen 是物理像素，换算回 DIP
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = _dragStartLeft + dx / dpi.DpiScaleX;
            Top = _dragStartTop + dy / dpi.DpiScaleY;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = false;
        ReleaseMouseCapture();
        if (_dragging)
        {
            _settings.FloatingX = Left;
            _settings.FloatingY = Top;
            _settings.RequestSave();
        }
        else if (!IsOnButton(e) && !_settings.PositionLocked)
        {
            // 轻点：也显形按钮组（鼠标还在窗内，悬停语义下本就该显示；离开时 MouseLeave 收起）
            ShowBand();
        }
        _dragging = false;
    }
}
