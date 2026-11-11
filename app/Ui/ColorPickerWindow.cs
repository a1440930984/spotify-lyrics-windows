using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// HSV 颜色盘（v0.3 交互重做，继承 ThemedWindow 自绘深色主题）：
/// - 标题就叫「选择颜色」（子窗口不带程序名/版本号，照用户要求）；
/// - 打开时居中于所有者窗口（Owner + CenterOwner），标题栏只有关闭键；
/// - **没有确认/取消按钮**：选色全程实时回调 ColorChanged 应用到悬浮窗，
///   关窗即保留当前色——所以「关窗 = 确认」；
/// - **失活自动关闭**：点到窗外（别的窗口/桌面）就关，逻辑同上（已实时生效，不会丢）。
///   窗内点 hex 输入框、拖色板都只是窗内焦点变化，不触发 Deactivated，不会误关；
///   刚打开 250ms 内的失活忽略（防打开手势的尾巴误触）。
/// SV 二维板 + 色相条 + 预览块 + hex 输入 + 12 预设色四处同步（_syncing 防回环），
/// 颜色强制 alpha=FF。不依赖 WinForms ColorDialog。
/// </summary>
public sealed class ColorPickerWindow : ThemedWindow
{
    /// <summary>选色实时变化（拖动/预设/hex 都会发）。订阅方据此即时应用。</summary>
    public event Action<Color>? ColorChanged;

    private double _h;          // 0..360
    private double _s = 1;      // 0..1
    private double _v = 1;      // 0..1
    private bool _syncing;
    private bool _live;                       // 构造完成前不发 ColorChanged
    private DateTime _shownAt = DateTime.MinValue;

    private readonly Canvas _svCanvas;
    private readonly Rectangle _svBase;
    private readonly Ellipse _svMarker;
    private readonly Ellipse _svMarkerRing;
    private readonly Canvas _hueCanvas;
    private readonly Rectangle _hueMarker;
    private readonly Border _preview;
    private readonly TextBox _hexBox;
    private bool _dragSv, _dragHue;

    public static readonly string[] Presets =
    {
        "#FF1ED760", "#FFFFFFFF", "#FFB0BEC5", "#FF101010", "#FFFF5252", "#FFFFAB40",
        "#FFFFF176", "#FF69F0AE", "#FF4DD0E1", "#FF448AFF", "#FFB388FF", "#FFF48FB1"
    };

    private const double SvW = 264, SvH = 176, HueH = 22;

    public ColorPickerWindow(Color initial) : base("选择颜色", showMinimize: false)
    {
        Width = 330;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;   // 居中于所有者窗口
        Deactivated += OnDeactivatedAutoClose;
        Activated += (_, _) => { if (_shownAt == DateTime.MinValue) _shownAt = DateTime.UtcNow; };

        (_h, _s, _v) = RgbToHsv(initial);

        var panel = new StackPanel { Margin = new Thickness(16) };

        // ---- 预览 + hex ----
        _preview = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(12),
            BorderBrush = Theme.BorderBrush,
            BorderThickness = new Thickness(1.5)
        };
        _hexBox = new TextBox
        {
            Width = 120, VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        _hexBox.TextChanged += OnHexChanged;
        var topRow = new StackPanel { Orientation = Orientation.Horizontal };
        topRow.Children.Add(_preview);
        topRow.Children.Add(_hexBox);
        panel.Children.Add(topRow);
        panel.Children.Add(new TextBlock
        {
            Text = "拖动色板选深浅，拖动色条选颜色；选色实时生效，点到窗外自动关闭并保留。",
            FontSize = 11, Foreground = Theme.SubTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        // ---- SV 二维板：底色(纯色相) × 白→透明(横) × 透明→黑(纵) ----
        _svBase = new Rectangle { Width = SvW, Height = SvH };
        var whiteGrad = new Rectangle
        {
            Width = SvW, Height = SvH,
            Fill = new LinearGradientBrush(Colors.White, Colors.Transparent, 0)
        };
        var blackGrad = new Rectangle
        {
            Width = SvW, Height = SvH,
            Fill = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90)
        };
        _svMarkerRing = new Ellipse
        {
            Width = 18, Height = 18,
            Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
            StrokeThickness = 1, Fill = Brushes.Transparent, IsHitTestVisible = false
        };
        _svMarker = new Ellipse
        {
            Width = 16, Height = 16,
            Stroke = Brushes.White, StrokeThickness = 2,
            Fill = Brushes.Transparent, IsHitTestVisible = false
        };
        _svCanvas = new Canvas
        {
            Width = SvW, Height = SvH, Margin = new Thickness(0, 10, 0, 0),
            Background = Brushes.Transparent, ClipToBounds = true
        };
        _svCanvas.Children.Add(_svBase);
        _svCanvas.Children.Add(whiteGrad);
        _svCanvas.Children.Add(blackGrad);
        _svCanvas.Children.Add(_svMarkerRing);
        _svCanvas.Children.Add(_svMarker);
        _svCanvas.MouseLeftButtonDown += (s, e) => { _dragSv = true; _svCanvas.CaptureMouse(); SvFromPoint(e); };
        _svCanvas.MouseMove += (s, e) => { if (_dragSv) SvFromPoint(e); };
        _svCanvas.MouseLeftButtonUp += (_, _) => { _dragSv = false; _svCanvas.ReleaseMouseCapture(); };
        panel.Children.Add(_svCanvas);

        // ---- 色相条 ----
        var hueGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        double[] stops = { 0, 60, 120, 180, 240, 300, 360 };
        foreach (double hh in stops)
            hueGrad.GradientStops.Add(new GradientStop(HsvToRgb(hh, 1, 1), hh / 360.0));
        var hueRect = new Rectangle { Width = SvW, Height = HueH, Fill = hueGrad };
        _hueMarker = new Rectangle
        {
            Width = 4, Height = HueH + 6, Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
            StrokeThickness = 1, IsHitTestVisible = false
        };
        _hueCanvas = new Canvas
        {
            Width = SvW, Height = HueH + 6, Margin = new Thickness(0, 8, 0, 0),
            Background = Brushes.Transparent, ClipToBounds = false
        };
        Canvas.SetTop(hueRect, 3);
        _hueCanvas.Children.Add(hueRect);
        _hueCanvas.Children.Add(_hueMarker);
        _hueCanvas.MouseLeftButtonDown += (s, e) => { _dragHue = true; _hueCanvas.CaptureMouse(); HueFromPoint(e); };
        _hueCanvas.MouseMove += (s, e) => { if (_dragHue) HueFromPoint(e); };
        _hueCanvas.MouseLeftButtonUp += (_, _) => { _dragHue = false; _hueCanvas.ReleaseMouseCapture(); };
        panel.Children.Add(_hueCanvas);

        // ---- 12 预设色 ----
        panel.Children.Add(new TextBlock
        {
            Text = "常用颜色", FontSize = 11, Foreground = Theme.SubTextBrush,
            Margin = new Thickness(0, 10, 0, 4)
        });
        var presets = new WrapPanel();
        foreach (string p in Presets)
        {
            var c = FloatingWindow.ParseHex(p, Colors.Transparent);
            var btn = new Button
            {
                Width = 38, Height = 30, Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(c),
                BorderBrush = Theme.BorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                // v0.7：扁平模板——原生 Button 模板悬停时色块四周透出底色、颜色铺不满，
                // 换纯 Border 模板（颜色铺满、悬停只给一圈强调色描边）
                Template = Theme.FlatSwatchTemplate(),
                Tag = c
            };
            btn.Click += (_, _) => SetColor((Color)btn.Tag, fromHex: false);
            presets.Children.Add(btn);
        }
        panel.Children.Add(presets);

        SetBody(panel);
        Refresh(pushHex: true);
        _live = true;
    }

    /// <summary>
    /// v0.8.6：本次失活关闭发生时，鼠标是否落在所有者（设置页）范围内。
    /// 设置页据此决定关窗后要不要把激活/前景抢回来（见 SettingsWindow 的
    /// Closed 处理）：点设置页内关掉的要还，点到别的程序关掉的不许抢。
    /// </summary>
    public bool ClosedOverOwner { get; private set; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private struct POINT { public int X; public int Y; }

    /// <summary>失活自动关闭：选色已实时生效，关窗即确认，不需要确认按钮。</summary>
    private void OnDeactivatedAutoClose(object? sender, EventArgs e)
    {
        if (_shownAt == DateTime.MinValue) return;
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 250) return;
        // v0.8.6：关之前记下这一下点在哪儿——失活正是由那次点击引起的，
        // 此刻光标位置就是点击位置；落在所有者矩形内 = 用户点的是设置页。
        ClosedOverOwner = IsCursorOverOwner();
        try { Close(); } catch { /* 已在关闭流程中 */ }
    }

    private bool IsCursorOverOwner()
    {
        try
        {
            if (Owner == null || !GetCursorPos(out var pt)) return false;
            var local = Owner.PointFromScreen(new Point(pt.X, pt.Y));
            return local.X >= 0 && local.Y >= 0
                && local.X <= Owner.ActualWidth && local.Y <= Owner.ActualHeight;
        }
        catch { return false; }
    }

    private Color CurrentColor => HsvToRgb(_h, _s, _v);

    private void SvFromPoint(MouseEventArgs e)
    {
        var p = e.GetPosition(_svCanvas);
        _s = Math.Clamp(p.X / SvW, 0, 1);
        _v = Math.Clamp(1 - p.Y / SvH, 0, 1);
        Refresh(pushHex: true);
    }

    private void HueFromPoint(MouseEventArgs e)
    {
        var p = e.GetPosition(_hueCanvas);
        _h = Math.Clamp(p.X / SvW, 0, 1) * 360.0;
        Refresh(pushHex: true);
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        string text = _hexBox.Text.Trim();
        if (!Core.Settings.IsValidHex(text)) return;
        string h = text.TrimStart('#').ToUpperInvariant();
        if (h.Length == 6) h = "FF" + h;
        byte r = Convert.ToByte(h.Substring(2, 2), 16);
        byte g = Convert.ToByte(h.Substring(4, 2), 16);
        byte b = Convert.ToByte(h.Substring(6, 2), 16);
        SetColor(Color.FromRgb(r, g, b), fromHex: true);
    }

    private void SetColor(Color c, bool fromHex)
    {
        (_h, _s, _v) = RgbToHsv(c);
        Refresh(pushHex: !fromHex);
    }

    private void Refresh(bool pushHex)
    {
        var c = CurrentColor;
        _svBase.Fill = new SolidColorBrush(HsvToRgb(_h, 1, 1));
        _preview.Background = new SolidColorBrush(c);
        double mx = _s * SvW, my = (1 - _v) * SvH;
        Canvas.SetLeft(_svMarker, mx - 8);
        Canvas.SetTop(_svMarker, my - 8);
        Canvas.SetLeft(_svMarkerRing, mx - 9);
        Canvas.SetTop(_svMarkerRing, my - 9);
        Canvas.SetLeft(_hueMarker, _h / 360.0 * SvW - 2);
        Canvas.SetTop(_hueMarker, 0);
        if (pushHex)
        {
            _syncing = true;
            _hexBox.Text = $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";
            _syncing = false;
        }
        if (_live) ColorChanged?.Invoke(c);
    }

    // ---------- HSV <-> RGB ----------

    public static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    public static (double H, double S, double V) RgbToHsv(Color col)
    {
        double r = col.R / 255.0, g = col.G / 255.0, b = col.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        double s = max == 0 ? 0 : d / max;
        return (h, s, max);
    }
}
