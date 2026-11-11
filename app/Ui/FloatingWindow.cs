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
    private Border _bandCapsule = null!;
    private bool _bandShown;
    private double _lastHeight = -1;
    private bool _suppressCenterKeep;

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
            FontFamily = new FontFamily("Segoe UI Emoji, Microsoft YaHei UI")
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
            ToolTip = "隐藏歌词"
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
            HorizontalAlignment = HorizontalAlignment.Right,
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
        menu.Items.Add(_lockMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(miHide);
        menu.Opened += (_, _) => _lockMenuItem.IsChecked = _settings.PositionLocked;
        ContextMenu = menu;

        _hideButtonsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideButtonsTimer.Tick += (_, _) =>
        {
            SetBand(false);
            _hideButtonsTimer.Stop();
        };

        // 内容高度变化（单/双行切换、字号变化）时保持窗口垂直中心不动，
        // 用户感知的歌词位置不跳。按钮带的上扩/收回自己做全量 Top 补偿，走 _suppressCenterKeep 豁免。
        SizeChanged += (_, e) =>
        {
            if (_suppressCenterKeep)
            {
                _suppressCenterKeep = false;
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
        // 🔒 = 已锁定（不可拖）；🔓 = 未锁定。v0.5：锁定态用绿色半透明底衬提示状态
        // （胶囊带里不再用描边，避免与胶囊自身的描边混淆）。
        _lockButton.Content = _settings.PositionLocked ? "🔒" : "🔓";
        _lockButton.Background = _settings.PositionLocked
            ? new SolidColorBrush(Color.FromArgb(0x40, 0x1E, 0xD7, 0x60))
            : Brushes.Transparent;
    }

    private void RevealButtons()
    {
        SetBand(true);
        _hideButtonsTimer.Stop();
        _hideButtonsTimer.Start();
    }

    /// <summary>
    /// 按钮带显/隐（v0.3）：扩高 BandHeight 的同时 Top 反向移动同等距离，
    /// 歌词区在屏幕上的位置像素级不动，按钮永远不会盖住歌词；
    /// 收回时反向操作。拖动判定不含按钮本身（见 IsOnButton），带内空白区仍可拖。
    /// </summary>
    private void SetBand(bool show)
    {
        if (_bandShown == show) return;
        _bandShown = show;
        double delta = show ? BandHeight : -BandHeight;
        _suppressCenterKeep = true;
        _bandRow.Height = new GridLength(show ? BandHeight : 0);
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        _bandCapsule.Visibility = vis;
        _closeButton.Visibility = vis;
        _lockButton.Visibility = vis;
        if (!double.IsNaN(Top) && IsLoaded) Top -= delta;
        else _suppressCenterKeep = false;
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
