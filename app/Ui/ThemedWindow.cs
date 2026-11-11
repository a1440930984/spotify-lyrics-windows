using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 自绘主题窗口基类（v0.3 第 6 点）：三个页面（设置/手动选词/颜色盘）统一用它，
/// 弃用默认 Windows 窗口样式。
/// - 无系统边框：WindowStyle=None + WindowChrome（保留系统缩放边、吸附与阴影）；
/// - 圆角：Win11 走 DWM 圆角 API（DWMWA_WINDOW_CORNER_PREFERENCE），失败静默；
/// - 自绘标题栏（高 34）：左 = 18px 小 logo + 标题；右 = 窗口按钮
///   （由构造参数决定：最小化+关闭，或仅关闭）；标题栏空白处可拖动；
/// - 内容统一深色：Background #121212 + Theme 资源字典（控件全套深色样式）。
/// 子类把正文交给 SetBody()；标题用 SetWindowTitle()（同时写系统 Title 与标题栏文字）。
/// </summary>
public abstract class ThemedWindow : Window
{
    private readonly TextBlock _titleText;
    private readonly ContentControl _bodyHost;
    protected bool ShowMinimizeButton { get; }

    protected ThemedWindow(string title, bool showMinimize)
    {
        ShowMinimizeButton = showMinimize;
        WindowStyle = WindowStyle.None;
        Background = Theme.BgBrush;
        Foreground = Theme.TextBrush;
        FontFamily = Theme.UiFont;
        ShowInTaskbar = true;
        WindowIcon.Apply(this);
        Resources.MergedDictionaries.Add(Theme.BuildResources());

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,                                   // 标题栏全自绘，拖动手动处理
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        SourceInitialized += OnSourceInitialized;

        // ---- 标题栏 ----
        var titleBar = new Border
        {
            Background = Theme.Brush(Theme.TitleBarColor),
            BorderBrush = Theme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Height = 34
        };
        var tbGrid = new Grid();
        tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = new Image
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(10, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform
        };
        var logoSource = WindowIcon.LoadImageSource();
        if (logoSource != null) logo.Source = logoSource; else logo.Visibility = Visibility.Collapsed;
        Grid.SetColumn(logo, 0);
        tbGrid.Children.Add(logo);

        _titleText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12.5,
            Foreground = Theme.TextBrush
        };
        Grid.SetColumn(_titleText, 1);
        tbGrid.Children.Add(_titleText);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
        if (showMinimize)
        {
            var minBtn = new Button
            {
                Content = "—",
                Style = Theme.TitleBarButtonStyle(),
                ToolTip = "最小化"
            };
            minBtn.Click += (_, _) => WindowState = WindowState.Minimized;
            btnPanel.Children.Add(minBtn);
        }
        var closeBtn = new Button
        {
            Content = "✕",
            Style = Theme.TitleBarButtonStyle(isClose: true),
            ToolTip = "关闭"
        };
        closeBtn.Click += (_, _) => Close();
        btnPanel.Children.Add(closeBtn);
        Grid.SetColumn(btnPanel, 2);
        tbGrid.Children.Add(btnPanel);

        titleBar.Child = tbGrid;
        // 标题栏拖动：点在按钮上不拖（按钮自己吃事件，这里再判一次来源）
        titleBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 1 && e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* 状态不允许时忽略 */ }
            }
        };

        _bodyHost = new ContentControl();
        var root = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);
        root.Children.Add(_bodyHost);

        base.Content = root;
        SetWindowTitle(title);
    }

    /// <summary>子类正文入口（不要直接给窗口 Content 赋值，会覆盖自绘框架）。</summary>
    protected void SetBody(UIElement body) => _bodyHost.Content = body;

    /// <summary>同时设置系统窗口标题与自绘标题栏文字。</summary>
    protected void SetWindowTitle(string title)
    {
        Title = title;
        _titleText.Text = title;
    }

    // ---------- v0.5 窗口位置记忆（设置窗/手动选词窗用；颜色盘不用，每次居中于所有者） ----------

    /// <summary>
    /// 有合法记忆位置 → 原位打开；没有（首次）→ 主屏幕居中。
    /// 之后移动/关闭时经 save 回调落盘（调用方写 Settings 字段 + 防抖保存）。
    /// 记忆点已不在任何屏幕可见区（换显示器/改分辨率）时回退居中，不会开到屏幕外。
    /// </summary>
    protected void UsePositionMemory(double? savedX, double? savedY, Action<double, double> save)
    {
        if (savedX is double x && savedY is double y && IsPointVisible(x, y))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = x;
            Top = y;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        LocationChanged += (_, _) =>
        {
            if (IsLoaded && WindowState == WindowState.Normal) save(Left, Top);
        };
        Closed += (_, _) =>
        {
            if (WindowState == WindowState.Normal) save(Left, Top);
        };
    }

    private static bool IsPointVisible(double x, double y)
    {
        // 虚拟屏幕包围盒（DIP）内，且右缘至少留 60px、顶部不高于屏幕上沿太多、底部留标题栏高度
        double vl = SystemParameters.VirtualScreenLeft;
        double vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth;
        double vb = vt + SystemParameters.VirtualScreenHeight;
        return x + 60 >= vl && x <= vr - 60 && y >= vt - 8 && y <= vb - 40;
    }

    // ---------- DWM 圆角 + 阴影（尽力而为，失败不影响功能） ----------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            // 1px 帧扩展换回 DWM 投影（WindowChrome GlassFrameThickness=0 时默认没阴影）
            var m = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(hwnd, ref m);
            // Win11 圆角：DWMWA_WINDOW_CORNER_PREFERENCE(33) = DWMWCP_ROUND(2)
            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        }
        catch { /* 老系统/失败：方角无影，功能不受影响 */ }
    }
}
