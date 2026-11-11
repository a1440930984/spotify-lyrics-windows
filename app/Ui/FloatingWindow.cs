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

        // ---- 右上按钮组：锁定 + 关闭（默认隐藏，轻点/悬停显形 3 秒，照安卓关闭键逻辑） ----
        _lockButton = new Button
        {
            Width = 30,
            Height = 30,
            Opacity = 0.85,
            Visibility = Visibility.Collapsed,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "锁定/解锁位置",
            FontFamily = new FontFamily("Segoe UI Emoji, Microsoft YaHei UI")
        };
        _lockButton.Click += (_, _) => ToggleLock();
        _closeButton = new Button
        {
            Content = "✕",
            Width = 30,
            Height = 30,
            Opacity = 0.85,
            Visibility = Visibility.Collapsed,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Cursor = Cursors.Hand,
            ToolTip = "隐藏歌词"
        };
        _closeButton.Click += (_, _) => CloseRequested?.Invoke();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        buttons.Children.Add(_lockButton);
        buttons.Children.Add(_closeButton);

        var grid = new Grid();
        grid.Children.Add(stack);
        grid.Children.Add(buttons);

        _rootBorder = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(16),
            Child = grid
        };
        Content = _rootBorder;

        // ---- 右击菜单（v0.2） ----
        var menu = new ContextMenu();
        var miSettings = new MenuItem { Header = "打开设置…" };
        miSettings.Click += (_, _) => SettingsRequested?.Invoke();
        var miPick = new MenuItem { Header = "手动选词…" };
        miPick.Click += (_, _) => ManualPickRequested?.Invoke();
        var miRefetch = new MenuItem { Header = "重新搜词" };
        miRefetch.Click += (_, _) => RefetchRequested?.Invoke();
        _lockMenuItem = new MenuItem { Header = "锁定位置", IsCheckable = true };
        _lockMenuItem.Click += (_, _) =>
        {
            _settings.PositionLocked = _lockMenuItem.IsChecked;
            _settings.RequestSave();
            UpdateLockButton();
        };
        var miHide = new MenuItem { Header = "隐藏歌词" };
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
            _closeButton.Visibility = Visibility.Collapsed;
            _lockButton.Visibility = Visibility.Collapsed;
            _hideButtonsTimer.Stop();
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
        // 🔒 = 已锁定（不可拖）；🔓 = 未锁定。锁定时按钮描边提示状态。
        _lockButton.Content = _settings.PositionLocked ? "🔒" : "🔓";
        _lockButton.BorderThickness = new Thickness(_settings.PositionLocked ? 1 : 0);
        _lockButton.BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
    }

    private void RevealButtons()
    {
        _closeButton.Visibility = Visibility.Visible;
        _lockButton.Visibility = Visibility.Visible;
        _hideButtonsTimer.Stop();
        _hideButtonsTimer.Start();
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
