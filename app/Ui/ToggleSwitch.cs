using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 自绘胶囊开关（v0.5 现代化改版，替代设置里的 CheckBox，照用户参考图的胶囊开关）：
/// 关 = 灰轨 + 白钮在左；开 = #1ED760 绿轨 + 白钮滑到右（约 150ms 滑动动画）。
/// 纯代码 FrameworkElement（同 MiniSlider 的路子，零模板风险）。
/// IsChecked 程序赋值不发事件；只有用户点击/空格切换才发 Toggled，
/// 调用方不用再防「加载控件时误触发」。
/// </summary>
public sealed class ToggleSwitch : FrameworkElement
{
    /// <summary>用户切换开关时发出（true = 开）。程序赋值 IsChecked 不触发。</summary>
    public event Action<bool>? Toggled;

    private bool _isChecked;
    private double _knobT;                 // 0=左(关) 1=右(开)，动画中间值
    private readonly DispatcherTimer _anim;

    public ToggleSwitch()
    {
        Width = 42;
        Height = 24;
        Focusable = true;
        Cursor = Cursors.Hand;
        _anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _anim.Tick += (_, _) => StepAnimation();
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) { _knobT = value ? 1 : 0; InvalidateVisual(); return; }
            _isChecked = value;
            StartAnimation();
        }
    }

    /// <summary>用户操作入口：翻转状态并发 Toggled。</summary>
    public void ToggleFromUser()
    {
        _isChecked = !_isChecked;
        StartAnimation();
        Toggled?.Invoke(_isChecked);
    }

    private void StartAnimation()
    {
        if (!IsLoaded) { _knobT = _isChecked ? 1 : 0; InvalidateVisual(); return; }
        _anim.Start();
    }

    private void StepAnimation()
    {
        double target = _isChecked ? 1 : 0;
        double delta = target - _knobT;
        if (Math.Abs(delta) < 0.04) { _knobT = target; _anim.Stop(); }
        else _knobT += delta * 0.35;      // 指数趋近，约 8 帧到位
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double t = Math.Clamp(_knobT, 0, 1);

        // 轨道：关 #3E3E3E → 开 #1ED760，按动画进度插值；圆角 = 高/2（胶囊）
        var off = Color.FromRgb(0x3E, 0x3E, 0x3E);
        var on = Theme.AccentColor;
        var track = new SolidColorBrush(Color.FromRgb(
            (byte)(off.R + (on.R - off.R) * t),
            (byte)(off.G + (on.G - off.G) * t),
            (byte)(off.B + (on.B - off.B) * t)));
        dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), h / 2, h / 2);

        // 白圆钮：半径 9，左右各留 3 边距，带一层淡阴影
        double r = 9;
        double cx = 3 + r + t * (w - 2 * (3 + r));
        double cy = h / 2;
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), null,
            new Point(cx, cy + 1), r, r);
        dc.DrawEllipse(Brushes.White, null, new Point(cx, cy), r, r);

        if (IsKeyboardFocused)
            dc.DrawRoundedRectangle(null, new Pen(Theme.SubTextBrush, 1),
                new Rect(-2, -2, w + 4, h + 4), h / 2 + 2, h / 2 + 2);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        ToggleFromUser();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            ToggleFromUser();
            e.Handled = true;
        }
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
