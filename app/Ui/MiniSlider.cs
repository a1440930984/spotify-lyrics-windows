using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 纯代码自绘滑杆（v0.3，替代系统 Slider）：
/// 系统 Slider 的 Track 子部件（DecreaseRepeatButton 等）不是依赖属性，
/// FrameworkElementFactory 模板在代码里装配不出来，硬套必翻车；
/// 这个控件自己画轨道（已滑过=绿 #1ED760、未滑过=#4D4D4D）+ 白色圆钮，
/// 鼠标按下/拖动定位、方向键 ±1，全程深色与主题一致。
/// API 精简到设置窗够用：Minimum/Maximum/Value（整数吸附）+ ValueChanged 事件。
/// </summary>
public sealed class MiniSlider : FrameworkElement
{
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    private double _value;

    /// <summary>值变化（用户拖动/按键/代码赋值都会发；调用方用自己的 _loading 守卫）。</summary>
    public event Action<double>? ValueChanged;

    public double Value
    {
        get => _value;
        set
        {
            double v = Math.Round(Math.Clamp(value, Minimum, Maximum));
            if (Math.Abs(v - _value) < 0.001) { _value = v; return; }
            _value = v;
            InvalidateVisual();
            ValueChanged?.Invoke(v);
        }
    }

    private bool _dragging;
    private const double ThumbR = 6.5;
    private const double PadX = 8;

    public MiniSlider()
    {
        Height = 18;
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        double y = h / 2;
        double x0 = PadX, x1 = w - PadX;
        double t = Maximum <= Minimum ? 0 : (_value - Minimum) / (Maximum - Minimum);
        double tx = x0 + (x1 - x0) * t;
        var trackPen = new Pen(new SolidColorBrush(Color.FromRgb(0x4D, 0x4D, 0x4D)), 4);
        var accPen = new Pen(Theme.AccentBrush, 4);
        dc.DrawLine(trackPen, new Point(x0, y), new Point(x1, y));
        if (tx > x0) dc.DrawLine(accPen, new Point(x0, y), new Point(tx, y));
        dc.DrawEllipse(Brushes.White, null, new Point(tx, y), ThumbR, ThumbR);
        if (IsKeyboardFocused)
            dc.DrawEllipse(null, new Pen(Theme.SubTextBrush, 1), new Point(tx, y), ThumbR + 2.5, ThumbR + 2.5);
    }

    private void SetFromPoint(Point p)
    {
        double w = ActualWidth;
        if (w <= 0) return;
        double t = Math.Clamp((p.X - PadX) / (w - 2 * PadX), 0, 1);
        Value = Minimum + t * (Maximum - Minimum);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        _dragging = true;
        CaptureMouse();
        SetFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
            SetFromPoint(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Left || e.Key == Key.Down) { Value -= 1; e.Handled = true; }
        else if (e.Key == Key.Right || e.Key == Key.Up) { Value += 1; e.Handled = true; }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }
}
