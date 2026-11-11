using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 当前行歌词的自绘控件 —— 安卓 LyricTextView 的 WPF 等效实现。
/// 横向 LinearGradient（MappingMode.Absolute）：左高亮、右普通、柔边 ±16 DIP；
/// 边界 x 按字符实测宽度插值；居中时补 startX 偏移。
/// 只改渐变不改文本，逐帧调用 SetProgress 只重绘不重排。
/// 长句逐级缩字号（下限 11），最多 3 行折行、不截断。
/// </summary>
public sealed class GradientLyricLine : FrameworkElement
{
    private string _text = "";
    private double _desiredFontSize = 18;
    private string _fontFamily = "Microsoft YaHei UI";
    private Color _hiColor = Color.FromArgb(0xFF, 0x1E, 0xD7, 0x60);
    private Color _normalColor = Colors.White;
    private float _fraction;
    private bool _smoothEnabled;

    private FormattedText? _cachedText;
    private double _cachedWidth;
    private double _fittedSize = 18;
    private double[]? _charWidths;
    private double _singleLineWidth;

    public string Text => _text;

    /// <summary>
    /// 当前文本在设定字号下的单行自然宽度（不折行）。供悬浮窗按内容自适应宽度：
    /// 窗口宽度取可见各行的最大值，当前行因此以自身文本为居中基准（v0.2）。
    /// </summary>
    public double NaturalWidth()
    {
        if (_text.Length == 0) return 0;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText(_text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface, _desiredFontSize, Brushes.White, dpi);
        return ft.WidthIncludingTrailingWhitespace;
    }

    public void Configure(Color highlight, Color normal, double fontSize, string fontFamily)
    {
        _hiColor = highlight;
        _normalColor = normal;
        _desiredFontSize = fontSize;
        _fontFamily = fontFamily;
        _cachedText = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetText(string text)
    {
        if (text == _text) return;
        _text = text ?? "";
        _cachedText = null;
        _charWidths = null;
        _fraction = 0f;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>每帧调用；变化太小（≤0.0015）就跳过（照安卓版阈值）。</summary>
    public void SetProgress(float f, bool enabled)
    {
        if (!enabled)
        {
            if (_smoothEnabled) { _smoothEnabled = false; _fraction = 0f; InvalidateVisual(); }
            return;
        }
        float v = Math.Clamp(f, 0f, 1f);
        if (!_smoothEnabled || Math.Abs(v - _fraction) > 0.0015f)
        {
            _smoothEnabled = true;
            _fraction = v;
            InvalidateVisual();
        }
    }

    private Typeface Typeface => new(new FontFamily(_fontFamily),
        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private FormattedText BuildWrapped(double width, double fontSize)
    {
        var ft = new FormattedText(_text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface, fontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(10, width),
            MaxTextHeight = fontSize * 1.45 * 3 + 2,
            Trimming = TextTrimming.None,
            TextAlignment = TextAlignment.Center
        };
        return ft;
    }

    /// <summary>从设定字号逐级 -1 缩到整行能在 3 行内放下，下限 11。</summary>
    private double FitFontSize(double width)
    {
        double size = _desiredFontSize;
        if (width <= 0 || _text.Length == 0) return size;
        while (size > 11)
        {
            var ft = BuildWrapped(width, size);
            // MaxTextHeight 截断时 Height 会顶满；行数估算：高 / 行高 ≤ 3 即放得下
            if (ft.Height <= size * 1.45 * 3 + 1) break;
            size -= 1;
        }
        return size;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 880 : availableSize.Width;
        if (_text.Length == 0) return new Size(w, _desiredFontSize * 1.45);
        _fittedSize = FitFontSize(w);
        var ft = BuildWrapped(w, _fittedSize);
        _cachedText = ft;
        _cachedWidth = w;
        return new Size(w, Math.Ceiling(ft.Height));
    }

    private void EnsureCharWidths()
    {
        if (_charWidths != null) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _charWidths = new double[_text.Length];
        double total = 0;
        for (int i = 0; i < _text.Length; i++)
        {
            var one = new FormattedText(_text[i].ToString(), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface, _fittedSize, Brushes.White, dpi);
            _charWidths[i] = one.WidthIncludingTrailingWhitespace;
            total += _charWidths[i];
        }
        _singleLineWidth = total;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || _text.Length == 0) return;
        var ft = (_cachedText != null && Math.Abs(_cachedWidth - w) < 0.5)
            ? _cachedText : BuildWrapped(w, _fittedSize);
        double y = Math.Max(0, (h - ft.Height) / 2);

        // 阴影：同文本以 #E6000000 在 (0,1) 偏移处先画一遍（规格：dx0 dy1 radius4，WPF 侧用 Effect 亦可，这里用偏移画法更可控）
        var shadow = BuildWrapped(w, _fittedSize);
        shadow.SetForegroundBrush(new SolidColorBrush(Color.FromArgb(0xE6, 0, 0, 0)));
        dc.DrawText(shadow, new Point(0, y + 1));

        Brush brush;
        if (!_smoothEnabled)
        {
            // 逐字关：整行高亮色（安卓 applyStyle 把当前行 textColor 设为高亮色）
            brush = new SolidColorBrush(_hiColor);
        }
        else
        {
            EnsureCharWidths();
            double charPos = _fraction * _text.Length;
            int i = Math.Clamp((int)charPos, 0, _text.Length);
            double partial = charPos - i;
            double x = 0;
            for (int k = 0; k < i && k < _charWidths!.Length; k++) x += _charWidths[k];
            if (i < _text.Length) x += _charWidths![i] * partial;

            // v0.2 居中修正：居中基准必须用 FormattedText 的实际排版宽度 ft.Width，
            // 不能用逐字宽度之和 _singleLineWidth——逐字求和忽略字距/连写塑形，
            // 与真实渲染宽度不等，会让扫光边界与视觉中心错位（v0.1「单行不居中」根因之一）。
            // 逐字进度先按比例折算到实际排版宽度上：
            double textWidth = ft.Width;
            if (_singleLineWidth > 0) x = x / _singleLineWidth * textWidth;
            double startX = Math.Max(0, (w - textWidth) / 2.0);
            double absX = startX + x;

            double edge = 16.0;
            double a = Math.Clamp((absX - edge) / w, 0.0001, 0.98);
            double b = Math.Clamp((absX + edge) / w, a + 0.0002, 1.0);
            var grad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(w, 0),
                MappingMode = BrushMappingMode.Absolute
            };
            grad.GradientStops.Add(new GradientStop(_hiColor, 0));
            grad.GradientStops.Add(new GradientStop(_hiColor, a));
            grad.GradientStops.Add(new GradientStop(_normalColor, b));
            grad.GradientStops.Add(new GradientStop(_normalColor, 1));
            brush = grad;
        }

        ft.SetForegroundBrush(brush);
        dc.DrawText(ft, new Point(0, y));
    }
}
