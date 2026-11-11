using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 桌面悬浮歌词窗（参数照复刻规格）：
/// 无边框、透明、置顶、不进任务栏；当前行（渐变扫光）+ 译文行 + 下一行；
/// 长句缩字号、切行上滑淡入、停播淡出 0.38、拖动阈值 8 保存位置、轻点显按钮 3 秒。
/// v0.2 新增：锁定位置按钮（挨着关闭键）、右击菜单、单/双行模式、
/// 窗口宽度按内容自适应（居中以当前行文本为准，修 v0.1 单行不居中）。
/// v0.3：按钮组不再覆盖歌词——显形时窗口向上扩出一条 30px 按钮带
/// （Top 同步上移，歌词区位置像素不动）；单/双行切换时保持窗口垂直中心稳定，
/// 单行模式当前行水平+垂直双居中（行高/居中公式见 Core.LayoutMath）。
/// </summary>
public sealed class FloatingWindow : Window
{
    private readonly Settings _settings;
    private readonly Border _rootBorder;
    private readonly GradientLyricLine _currentLine;
    private readonly TextBlock _translationBlock;
    private readonly TextBlock _nextBlock;
    private readonly Button _closeButton;
    private readonly Button _lockButton;
    private readonly MenuItem _lockMenuItem;
    private readonly DispatcherTimer _hideButtonsTimer;
    private readonly RowDefinition _bandRow;

    private const double BandHeight = 34;
    /// <summary>按钮带最小宽度：两钮 36+2+36 + 胶囊内边距 5×2 + 描边 ≈ 86，取 92 留余量。
    /// 窗口宽度取「歌词行宽」与它的较大者（v0.6），按钮带又恒居中，锁钮永远在窗口中轴线上。</summary>
    private const double BandMinWidth = 92;
    private Border _bandCapsule = null!;
    private MenuItem _notAdMenuItem = null!;
    private MenuItem _instrumentalMenuItem = null!;
    private bool _bandShown;
    private double _lastHeight = -1;
    // v0.6 按钮带动画（约 180ms，代码驱动）：_bandApplied 是已应用的带高（0~BandHeight），
    // 动画期间 SizeChanged 跳过「保持垂直中心」逻辑（Top 由动画自己逐帧补偿）。
    private bool _animatingBand;
    private double _bandApplied;
    private double _bandAnimFrom, _bandAnimTo;
    private long _bandAnimStartTick;
    private DispatcherTimer? _bandAnimTimer;

    private string _lastCurrent = "\0";
    private bool _dimmed;
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

        var shadow = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.9,           // #E6000000
            Direction = 270,         // 向下 dy=1
            ShadowDepth = 1,
            BlurRadius = 4
        };

        _currentLine = new GradientLyricLine { Effect = shadow };

        _translationBlock = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 60,
            Margin = new Thickness(0, 3, 0, 0),
            Visibility = Visibility.Collapsed,
            Effect = shadow
        };
        _nextBlock = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = 0.6,
            Effect = shadow
        };

        var stack = new StackPanel();
        stack.Children.Add(_currentLine);
        stack.Children.Add(_translationBlock);
        stack.Children.Add(_nextBlock);

        // ---- 按钮组：锁定 + 关闭（v0.3 移到独立按钮带，不再压在歌词上） ----
        // v0.5：按钮带改深色半透明胶囊衬底 + 图标白色描边带深色阴影——
        // 用户反馈在白色背景下旧版的裸图标「跟没有一样」，胶囊底让白底黑底都清晰。
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
            // v0.6：幽灵按钮——自绘模板 + 无焦点框，杜绝默认模板的选中/聚焦系统底色
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
            // v0.6：按钮带改水平居中——窗口宽度 = max(歌词行宽, 按钮带宽)（见 AdaptWidth），
            // 歌词行本身也居中，两者同在窗口中轴线上，长短句切换时锁钮位置纹丝不动。
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = buttons
        };

        // 两行布局：第 0 行按钮带（默认高 0，显形时扩到 BandHeight）、第 1 行歌词区
        _bandRow = new RowDefinition { Height = new GridLength(0) };
        var grid = new Grid();
        grid.RowDefinitions.Add(_bandRow);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(capsule, 0);
        Grid.SetRow(stack, 1);
        _bandCapsule = capsule;
        grid.Children.Add(capsule);
        grid.Children.Add(stack);

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
        menu.Items.Add(new Separator());
        menu.Items.Add(miHide);
        menu.Opened += (_, _) =>
        {
            _lockMenuItem.IsChecked = _settings.PositionLocked;
            // 「这不是广告」只在正显示广告提示时可点；纯音乐菜单项按当前标记状态换文案
            _notAdMenuItem.IsEnabled = IsAdShowing;
            _instrumentalMenuItem.Header = IsInstrumental ? "取消纯音乐标记" : "标记为纯音乐";
        };
        ContextMenu = menu;

        _hideButtonsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideButtonsTimer.Tick += (_, _) =>
        {
            SetBand(false);
            _hideButtonsTimer.Stop();
        };

        // 内容高度变化（单/双行切换、字号变化）时保持窗口垂直中心不动，
        // 用户感知的歌词位置不跳。按钮带动画期间高度由动画逐帧驱动并自行补偿 Top，这里豁免。
        SizeChanged += (_, e) =>
        {
            if (_animatingBand)
            {
                _lastHeight = e.NewSize.Height;
                return;
            }
            if (_lastHeight > 0 && !_dragging &&
                Math.Abs(e.NewSize.Height - _lastHeight) > 0.5 && !double.IsNaN(Top))
            {
                Top += (_lastHeight - e.NewSize.Height) / 2.0;
            }
            _lastHeight = e.NewSize.Height;
        };

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseEnter += (_, _) => RevealButtons();
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
        RevealButtons();
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
    /// v0.6 幽灵按钮模板（纯代码构建）：默认 Button 模板在聚焦/按下/悬停时会画系统
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

    private void RevealButtons()
    {
        SetBand(true);
        _hideButtonsTimer.Stop();
        _hideButtonsTimer.Start();
    }

    /// <summary>
    /// 按钮带显/隐。v0.3 起按钮带扩高时 Top 反向补偿，歌词区屏幕位置像素不动；
    /// v0.6 改为约 180ms 代码动画（DispatcherTimer 逐帧，EaseOut 三次方）：
    /// 带高 0↔BandHeight 平滑增减、Top 按已应用增量逐帧补偿、胶囊同步淡入淡出，
    /// 不再一帧跳变。动画中途反向调用时从当前已应用高度接着走，不回跳。
    /// 拖动判定不含按钮本身（见 IsOnButton），带内空白区仍可拖。
    /// </summary>
    private void SetBand(bool show)
    {
        if (_bandShown == show && !_animatingBand) return;
        _bandShown = show;
        if (show)
        {
            _bandCapsule.Visibility = Visibility.Visible;
            _closeButton.Visibility = Visibility.Visible;
            _lockButton.Visibility = Visibility.Visible;
        }
        _bandAnimFrom = _bandApplied;
        _bandAnimTo = show ? BandHeight : 0;
        _bandAnimStartTick = Environment.TickCount64;
        _animatingBand = true;
        if (_bandAnimTimer == null)
        {
            _bandAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _bandAnimTimer.Tick += OnBandAnimTick;
        }
        _bandAnimTimer.Stop();
        _bandAnimTimer.Start();
    }

    private void OnBandAnimTick(object? sender, EventArgs e)
    {
        const double DurationMs = 180;
        double p = Math.Clamp((Environment.TickCount64 - _bandAnimStartTick) / DurationMs, 0, 1);
        double eased = 1 - Math.Pow(1 - p, 3); // EaseOutCubic
        double h = _bandAnimFrom + (_bandAnimTo - _bandAnimFrom) * eased;
        double step = h - _bandApplied;
        _bandApplied = h;
        _bandRow.Height = new GridLength(Math.Max(0, h));
        _bandCapsule.Opacity = BandHeight > 0 ? h / BandHeight : 0;
        // 带在歌词上方扩/收：Top 反向补偿已应用增量，歌词区屏幕位置不动
        if (step != 0 && !double.IsNaN(Top) && IsLoaded) Top -= step;
        if (p >= 1)
        {
            _bandAnimTimer?.Stop();
            _animatingBand = false;
            if (_bandAnimTo <= 0)
            {
                _bandCapsule.Visibility = Visibility.Collapsed;
                _closeButton.Visibility = Visibility.Collapsed;
                _lockButton.Visibility = Visibility.Collapsed;
            }
        }
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
        var hi = ParseHex(_settings.HighlightColorHex, Color.FromArgb(0xFF, 0x1E, 0xD7, 0x60));
        var normal = ParseHex(_settings.TextColorHex, Colors.White);
        double size = _settings.FontSize;

        _currentLine.Configure(hi, normal, size, _settings.FontFamily);

        var ff = new FontFamily(_settings.FontFamily);
        _translationBlock.FontFamily = ff;
        _translationBlock.FontWeight = FontWeights.Bold;
        _translationBlock.FontSize = Math.Max(9, size - 6);   // 译文 -6，下限 9
        _translationBlock.Foreground = new SolidColorBrush(normal);
        _translationBlock.Opacity = 0.78;

        _nextBlock.FontFamily = ff;
        _nextBlock.FontWeight = FontWeights.Bold;
        _nextBlock.FontSize = Math.Max(10, size - 4);         // 下一行 -4，下限 10
        _nextBlock.Foreground = new SolidColorBrush(normal);
        if (!_dimmed) _nextBlock.Opacity = 0.6;

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
        if (_translationBlock.Visibility == Visibility.Visible)
            needed = Math.Max(needed, MeasurePlain(_translationBlock.Text, Math.Max(9, _settings.FontSize - 6)));
        // v0.6：窗口宽度不小于按钮带宽——按钮带居中、歌词居中，长短句共用同一中轴，
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
        bool changed = current != _lastCurrent;
        _lastCurrent = current;
        _currentLine.SetText(current);

        // 单行模式：不显示下一行（译文行仍按其开关）
        bool showNext = !_settings.SingleLineMode && !string.IsNullOrEmpty(next);
        _nextBlock.Text = showNext ? next : "";
        _nextBlock.Visibility = showNext ? Visibility.Visible : Visibility.Collapsed;

        string tr = _settings.ShowTranslation ? translation ?? "" : "";
        _translationBlock.Text = tr;
        _translationBlock.Visibility = string.IsNullOrWhiteSpace(tr)
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

    /// <summary>纯提示行（等待播放/正在找歌词/未找到/广告提示）：无逐字、正常色整行显示。</summary>
    public void SetHint(string hint)
    {
        _lastCurrent = hint;
        _currentLine.SetText(hint);
        _currentLine.SetProgress(0f, false);
        _nextBlock.Text = "";
        _nextBlock.Visibility = Visibility.Collapsed;
        _translationBlock.Visibility = Visibility.Collapsed;
        AdaptWidth();
    }

    public void SetProgress(float fraction, bool wordByWord)
    {
        _currentLine.SetProgress(fraction, wordByWord);
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
        if (_settings.PositionLocked) return; // 锁定：整窗不可拖（按钮仍可点）
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
        else if (!IsOnButton(e))
        {
            // 轻点：显形按钮组，3 秒后自动藏回去
            RevealButtons();
        }
        _dragging = false;
    }
}
