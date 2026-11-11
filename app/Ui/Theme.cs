using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SpotifyLyricsWin.Ui;

/// <summary>一套界面主题的调色板（v0.6 多主题）。IsLight 供个别控件决定深浅细节。</summary>
public sealed class ThemePalette
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required Color Bg { get; init; }
    public required Color Panel { get; init; }
    public required Color Border { get; init; }
    public required Color Text { get; init; }
    public required Color SubText { get; init; }
    public required Color Accent { get; init; }
    public required Color Hover { get; init; }
    public required Color Pressed { get; init; }
    public required Color SelectedRow { get; init; }
    public required Color TitleBar { get; init; }
    public required bool IsLight { get; init; }
}

/// <summary>
/// 界面主题（v0.3 自绘深色起家；v0.6 改为调色板驱动的多主题）：
/// 4 套预设——Spotify 深色（默认）/ 纯黑 OLED / 深蓝 / 浅色。
/// 强调色决策：深色三套恒为 Spotify 绿 #1ED760（品牌色，也与歌词高亮默认色呼应）；
/// 浅色套改深绿 #17A74B（#1ED760 在白底上对比不足，描边/文字会发飘）。
/// 悬浮歌词的文字颜色不走这里——那是用户自己的颜色设置，两套互不干扰。
/// Theme.*Color / *Brush 现在都是「当前调色板」的属性（构建窗口时取值）；
/// 切换主题时已打开的窗口由 Program 关掉重开来整体换装（位置记忆保证回到原位）。
/// 用法：窗口构造时 Resources.MergedDictionaries.Add(Theme.BuildResources())。
/// </summary>
public static class Theme
{
    public static readonly IReadOnlyDictionary<string, ThemePalette> Palettes =
        new Dictionary<string, ThemePalette>
        {
            ["spotify-dark"] = new ThemePalette
            {
                // v0.8：与纯黑拉开距离——底色改明显深灰（#1E1E1E）、卡片 #2A2A2A
                // （v0.7 的 #121212 与纯黑 #000000 并排仍难分，Sun 复验时明确质疑）。
                Id = "spotify-dark", DisplayName = "Spotify 深色",
                Bg = Color.FromRgb(0x1E, 0x1E, 0x1E), Panel = Color.FromRgb(0x2A, 0x2A, 0x2A),
                Border = Color.FromRgb(0x3D, 0x3D, 0x3D), Text = Colors.White,
                SubText = Color.FromRgb(0xB3, 0xB3, 0xB3), Accent = Color.FromRgb(0x1E, 0xD7, 0x60),
                Hover = Color.FromRgb(0x36, 0x36, 0x36), Pressed = Color.FromRgb(0x42, 0x42, 0x42),
                SelectedRow = Color.FromRgb(0x24, 0x3A, 0x2B), TitleBar = Color.FromRgb(0x25, 0x25, 0x25),
                IsLight = false
            },
            ["pure-black"] = new ThemePalette
            {
                Id = "pure-black", DisplayName = "纯黑（OLED）",
                Bg = Color.FromRgb(0x00, 0x00, 0x00), Panel = Color.FromRgb(0x0A, 0x0A, 0x0A),
                Border = Color.FromRgb(0x1F, 0x1F, 0x1F), Text = Colors.White,
                SubText = Color.FromRgb(0xA8, 0xA8, 0xA8), Accent = Color.FromRgb(0x1E, 0xD7, 0x60),
                Hover = Color.FromRgb(0x1A, 0x1A, 0x1A), Pressed = Color.FromRgb(0x26, 0x26, 0x26),
                SelectedRow = Color.FromRgb(0x14, 0x2E, 0x1E), TitleBar = Color.FromRgb(0x05, 0x05, 0x05),
                IsLight = false
            },
            ["deep-blue"] = new ThemePalette
            {
                // v0.7：蓝色再往深邃藏青推（背景 #0A1128、卡片 #14213D 系），
                // 与纯黑的 #000/#0A0A0A 拉开肉眼可辨的差距（v0.6 两套被 Mica 罩住看着一样，
                // 且旧蓝值本身偏灰——双侧都修，见 Program.ApplyFluentTheme 的覆写键补全）。
                Id = "deep-blue", DisplayName = "深蓝",
                Bg = Color.FromRgb(0x0A, 0x11, 0x28), Panel = Color.FromRgb(0x14, 0x21, 0x3D),
                Border = Color.FromRgb(0x2A, 0x3F, 0x66), Text = Colors.White,
                SubText = Color.FromRgb(0xA3, 0xB8, 0xD8), Accent = Color.FromRgb(0x1E, 0xD7, 0x60),
                Hover = Color.FromRgb(0x1B, 0x2A, 0x4A), Pressed = Color.FromRgb(0x24, 0x36, 0x5E),
                SelectedRow = Color.FromRgb(0x1A, 0x2F, 0x52), TitleBar = Color.FromRgb(0x0D, 0x15, 0x26),
                IsLight = false
            },
            ["light"] = new ThemePalette
            {
                Id = "light", DisplayName = "浅色（默认）",
                Bg = Color.FromRgb(0xF4, 0xF4, 0xF6), Panel = Color.FromRgb(0xFF, 0xFF, 0xFF),
                Border = Color.FromRgb(0xDC, 0xDC, 0xE2), Text = Color.FromRgb(0x17, 0x17, 0x1C),
                SubText = Color.FromRgb(0x5C, 0x5C, 0x66), Accent = Color.FromRgb(0x17, 0xA7, 0x4B),
                Hover = Color.FromRgb(0xE8, 0xE8, 0xEE), Pressed = Color.FromRgb(0xDC, 0xDC, 0xE4),
                SelectedRow = Color.FromRgb(0xDF, 0xF5, 0xE6), TitleBar = Color.FromRgb(0xEC, 0xEC, 0xF0),
                IsLight = true
            },
        };

    /// <summary>v0.7：默认主题改浅色（新装默认；老用户读自己存的设置）。</summary>
    public const string DefaultPaletteId = "light";

    /// <summary>
    /// FluentWindow 的背景材质：深色三套**全关 Mica**——Mica 是系统材质、不吃应用
    /// 资源覆写，v0.6 时主题背景覆写全被它罩住、几套看着一模一样（用户两度实测质疑）。
    /// v0.7 先关了纯黑/深蓝，v0.8 把 Spotify 深色也关掉：三套深色的窗口背景都老实走
    /// ApplicationBackgroundBrush 覆写（各自调色板色），色差不再被材质吃掉。
    /// 只有浅色套保留 Mica 质感（它的底色与系统材质接近，不影响辨识）。
    /// </summary>
    public static Wpf.Ui.Controls.WindowBackdropType Backdrop =>
        Current.IsLight
            ? Wpf.Ui.Controls.WindowBackdropType.Mica
            : Wpf.Ui.Controls.WindowBackdropType.None;

    private static ThemePalette _current = Palettes[DefaultPaletteId];
    private static readonly Dictionary<string, SolidColorBrush> _brushCache = new();

    public static ThemePalette Current => _current;

    /// <summary>切换当前调色板（未知 id 回退默认套）。窗口在构造时读取，故切换后需重开窗口才整体换装。</summary>
    public static void Apply(string? id)
    {
        if (id == null || !Palettes.TryGetValue(id, out var p)) p = Palettes[DefaultPaletteId];
        _current = p;
        _brushCache.Clear();
    }

    public static Color BgColor => _current.Bg;
    public static Color PanelColor => _current.Panel;
    public static Color BorderColor => _current.Border;
    public static Color TextColor => _current.Text;
    public static Color SubTextColor => _current.SubText;
    public static Color AccentColor => _current.Accent;
    public static Color HoverColor => _current.Hover;
    public static Color PressedColor => _current.Pressed;
    public static Color SelectedRowColor => _current.SelectedRow;
    public static Color TitleBarColor => _current.TitleBar;
    public static readonly Color CloseHoverColor = Color.FromRgb(0xE8, 0x11, 0x23);

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static SolidColorBrush Cached(string key, Color c)
    {
        if (_brushCache.TryGetValue(key, out var b)) return b;
        b = Brush(c);
        _brushCache[key] = b;
        return b;
    }

    public static SolidColorBrush BgBrush => Cached("bg", BgColor);
    public static SolidColorBrush PanelBrush => Cached("panel", PanelColor);
    public static SolidColorBrush BorderBrush => Cached("border", BorderColor);
    public static SolidColorBrush TextBrush => Cached("text", TextColor);
    public static SolidColorBrush SubTextBrush => Cached("subtext", SubTextColor);
    public static SolidColorBrush AccentBrush => Cached("accent", AccentColor);
    public static SolidColorBrush HoverBrush => Cached("hover", HoverColor);
    public static SolidColorBrush PressedBrush => Cached("pressed", PressedColor);

    public static readonly FontFamily UiFont = new("Microsoft YaHei UI, Segoe UI");

    public static ResourceDictionary BuildResources()
    {
        var rd = new ResourceDictionary();
        rd[typeof(TextBlock)] = TextBlockStyle();
        rd[typeof(Button)] = ButtonStyle();
        rd[typeof(TextBox)] = TextBoxStyle();
        rd[typeof(CheckBox)] = CheckBoxStyle();
        rd[typeof(RadioButton)] = RadioButtonStyle();
        // 滑杆不用系统 Slider（Track 子部件非 DP、工厂模板装配不了）→ 改用纯代码自绘 MiniSlider 控件。
        rd[typeof(ListBox)] = ListBoxStyle();
        rd[typeof(ListBoxItem)] = ListBoxItemStyle();
        rd[typeof(ComboBox)] = ComboBoxStyle();
        rd[typeof(ComboBoxItem)] = ComboBoxItemStyle();
        // v0.5：细滚动条（XamlReader 模板）；加载失败则不注册、回退系统滚动条
        var thin = ThinScroll.Load();
        if (thin != null) rd.MergedDictionaries.Add(thin);
        return rd;
    }

    // ---------- v0.5 卡片式布局（现代化改版：大圆角、卡片分层、留白） ----------

    /// <summary>
    /// 分区卡片：#1E1E1E 底 + 1px 描边 + 圆角 12，顶部小号灰色分区标题，
    /// 正文 StackPanel（内边距 16）交给调用方填充。卡片之间留 12 间距。
    /// </summary>
    public static (Border Card, StackPanel Body) MakeCard(string title)
    {
        var body = new StackPanel { Margin = new Thickness(16, 4, 16, 16) };
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = SubTextBrush,
            Margin = new Thickness(16, 13, 16, 6)
        });
        inner.Children.Add(body);
        var card = new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = inner,
            Margin = new Thickness(0, 0, 0, 12)
        };
        return (card, body);
    }

    // ---------- 通用小工具 ----------

    private static FrameworkElementFactory BorderWithContent(string name = "Bd")
    {
        var border = new FrameworkElementFactory(typeof(Border), name);
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        return border;
    }

    private static Trigger HoverTrigger(string elementName, DependencyProperty prop, object value)
    {
        var t = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        t.Setters.Add(new Setter(prop, value, elementName));
        return t;
    }

    // ---------- TextBlock ----------

    private static Style TextBlockStyle()
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(TextBlock.FontFamilyProperty, UiFont));
        return s;
    }

    // ---------- Button ----------

    public static Style ButtonStyle()
    {
        var border = BorderWithContent();
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(cp);
        var tpl = new ControlTemplate(typeof(Button)) { VisualTree = border };
        tpl.Triggers.Add(HoverTrigger("Bd", Border.BackgroundProperty, HoverBrush));
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, PressedBrush, "Bd"));
        tpl.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "Bd"));
        tpl.Triggers.Add(disabled);

        var s = new Style(typeof(Button));
        s.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 6, 16, 6)));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }

    /// <summary>强调按钮（绿底深字），给主操作用。</summary>
    public static Style AccentButtonStyle()
    {
        var s = ButtonStyle();
        s.Setters.Add(new Setter(Control.BackgroundProperty, AccentBrush));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Brush(Color.FromRgb(0x0A, 0x0A, 0x0A))));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush));
        s.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        return s;
    }

    /// <summary>
    /// 预设色块的扁平模板（v0.7）：原生 Button 默认模板带悬停背景与内边距，
    /// 鼠标悬停时色块四周会透出底色、颜色铺不满（颜色盘窗与设置窗颜色卡两处同病）。
    /// 换成纯 Border 模板：背景=色块自己的颜色、铺满整个方块、无内边距；
    /// 悬停反馈只给一圈强调色描边（边框加粗），不再有底色透出。
    /// </summary>
    public static ControlTemplate FlatSwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty,
            new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty,
            new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var tpl = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "Bd"));
        hover.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Bd"));
        tpl.Triggers.Add(hover);
        return tpl;
    }

    /// <summary>自绘标题栏窗口按钮（最小化/关闭）：透明底、悬停变色。</summary>
    public static Style TitleBarButtonStyle(bool isClose = false)
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        var tpl = new ControlTemplate(typeof(Button)) { VisualTree = border };
        tpl.Triggers.Add(HoverTrigger("Bd", Border.BackgroundProperty,
            isClose ? Brush(CloseHoverColor) : HoverBrush));
        var s = new Style(typeof(Button));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        s.Setters.Add(new Setter(FrameworkElement.WidthProperty, 42.0));
        s.Setters.Add(new Setter(FrameworkElement.HeightProperty, 34.0));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }

    // ---------- TextBox ----------

    private static Style TextBoxStyle()
    {
        var border = BorderWithContent();
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        border.AppendChild(host);
        var tpl = new ControlTemplate(typeof(TextBox)) { VisualTree = border };
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "Bd"));
        tpl.Triggers.Add(focus);

        var s = new Style(typeof(TextBox));
        s.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(TextBox.CaretBrushProperty, TextBrush));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        s.Setters.Add(new Setter(TextBox.SelectionBrushProperty, AccentBrush));
        return s;
    }

    // ---------- CheckBox / RadioButton ----------

    private static FrameworkElementFactory CheckLayout(string markText, bool round)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        FrameworkElementFactory box;
        if (round)
        {
            box = new FrameworkElementFactory(typeof(Ellipse), "Box");
            box.SetValue(FrameworkElement.WidthProperty, 16.0);
            box.SetValue(FrameworkElement.HeightProperty, 16.0);
            box.SetValue(Shape.StrokeProperty, SubTextBrush);
            box.SetValue(Shape.StrokeThicknessProperty, 1.4);
            box.SetValue(Shape.FillProperty, PanelBrush);
        }
        else
        {
            box = new FrameworkElementFactory(typeof(Border), "Box");
            box.SetValue(FrameworkElement.WidthProperty, 16.0);
            box.SetValue(FrameworkElement.HeightProperty, 16.0);
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1.4));
            box.SetValue(Border.BorderBrushProperty, SubTextBrush);
            box.SetValue(Border.BackgroundProperty, PanelBrush);
        }
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var mark = new FrameworkElementFactory(typeof(TextBlock), "Mark");
        mark.SetValue(TextBlock.TextProperty, markText);
        mark.SetValue(TextBlock.FontSizeProperty, round ? 9.0 : 11.0);
        mark.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        mark.SetValue(TextBlock.ForegroundProperty, round ? AccentBrush : Brush(BgColor));
        mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        mark.SetValue(UIElement.OpacityProperty, 0.0);
        if (round)
        {
            // 圆点标记放进一个 Grid 容器里与圆圈重叠
            var wrap = new FrameworkElementFactory(typeof(Grid));
            var dotHolder = new FrameworkElementFactory(typeof(Border));
            dotHolder.SetValue(FrameworkElement.WidthProperty, 16.0);
            dotHolder.SetValue(FrameworkElement.HeightProperty, 16.0);
            wrap.AppendChild(box);
            var markWrap = new FrameworkElementFactory(typeof(Border));
            markWrap.AppendChild(mark);
            wrap.AppendChild(markWrap);
            panel.AppendChild(wrap);
        }
        else
        {
            var wrap = new FrameworkElementFactory(typeof(Grid));
            wrap.AppendChild(box);
            var markWrap = new FrameworkElementFactory(typeof(Border));
            markWrap.AppendChild(mark);
            wrap.AppendChild(markWrap);
            panel.AppendChild(wrap);
        }
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 0, 0, 0));
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        panel.AppendChild(cp);
        return panel;
    }

    private static Style CheckBoxStyle()
    {
        var root = CheckLayout("✓", round: false);
        var tpl = new ControlTemplate(typeof(CheckBox)) { VisualTree = root };
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, AccentBrush, "Box"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "Box"));
        on.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "Mark"));
        tpl.Triggers.Add(on);
        var s = new Style(typeof(CheckBox));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }

    private static Style RadioButtonStyle()
    {
        var root = CheckLayout("●", round: true);
        var tpl = new ControlTemplate(typeof(RadioButton)) { VisualTree = root };
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Shape.StrokeProperty, AccentBrush, "Box"));
        on.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "Mark"));
        tpl.Triggers.Add(on);
        var s = new Style(typeof(RadioButton));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }

    // ---------- ListBox ----------

    private static Style ListBoxStyle()
    {
        var border = BorderWithContent("Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        var sv = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ScrollViewer");
        sv.SetValue(ScrollViewer.CanContentScrollProperty, true);
        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        sv.AppendChild(presenter);
        border.AppendChild(sv);
        var tpl = new ControlTemplate(typeof(ListBox)) { VisualTree = border };
        var s = new Style(typeof(ListBox));
        s.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }

    private static ControlTemplate RowTemplate(Type target, DependencyProperty activeProperty)
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        var tpl = new ControlTemplate(target) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, HoverBrush, "Bd"));
        tpl.Triggers.Add(hover);
        var sel = new Trigger { Property = activeProperty, Value = true };
        sel.Setters.Add(new Setter(Border.BackgroundProperty, Brush(SelectedRowColor), "Bd"));
        tpl.Triggers.Add(sel);
        return tpl;
    }

    private static Style ListBoxItemStyle()
    {
        var s = new Style(typeof(ListBoxItem));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(Control.TemplateProperty,
            RowTemplate(typeof(ListBoxItem), ListBoxItem.IsSelectedProperty)));
        return s;
    }

    private static Style ComboBoxItemStyle()
    {
        var s = new Style(typeof(ComboBoxItem));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(Control.TemplateProperty,
            RowTemplate(typeof(ComboBoxItem), ComboBoxItem.IsHighlightedProperty)));
        return s;
    }

    // ---------- ComboBox ----------

    private static Style ComboBoxStyle()
    {
        var grid = new FrameworkElementFactory(typeof(Grid));

        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        grid.AppendChild(border);

        var contentSite = new FrameworkElementFactory(typeof(ContentPresenter), "ContentSite");
        contentSite.SetValue(FrameworkElement.IsHitTestVisibleProperty, false);
        contentSite.SetValue(ContentPresenter.ContentProperty,
            new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
        contentSite.SetValue(ContentPresenter.ContentTemplateProperty,
            new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
        contentSite.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        contentSite.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(contentSite);

        var arrow = new FrameworkElementFactory(typeof(Path), "Arrow");
        arrow.SetValue(Path.DataProperty, Geometry.Parse("M 0 0 L 9 0 L 4.5 5.5 Z"));
        arrow.SetValue(Shape.FillProperty, SubTextBrush);
        arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
        arrow.SetValue(FrameworkElement.IsHitTestVisibleProperty, false);
        grid.AppendChild(arrow);

        var toggle = new FrameworkElementFactory(typeof(ToggleButton), "DropDownToggle");
        toggle.SetValue(ToggleButton.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(ToggleButton.BorderThicknessProperty, new Thickness(0));
        var toggleTplBorder = new FrameworkElementFactory(typeof(Border));
        toggleTplBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var toggleTpl = new ControlTemplate(typeof(ToggleButton)) { VisualTree = toggleTplBorder };
        toggle.SetValue(Control.TemplateProperty, toggleTpl);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
        {
            RelativeSource = RelativeSource.TemplatedParent,
            Mode = BindingMode.TwoWay
        });
        grid.AppendChild(toggle);

        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
        popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen")
        { RelativeSource = RelativeSource.TemplatedParent });
        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty, PanelBrush);
        popupBorder.SetValue(Border.BorderBrushProperty, BorderBrush);
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        popupBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));
        popupBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth")
        { RelativeSource = RelativeSource.TemplatedParent });
        var popupSv = new FrameworkElementFactory(typeof(ScrollViewer));
        popupSv.SetValue(ScrollViewer.CanContentScrollProperty, true);
        popupSv.SetValue(FrameworkElement.MaxHeightProperty, 260.0);
        var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        itemsPresenter.SetValue(KeyboardNavigation.DirectionalNavigationProperty,
            KeyboardNavigationMode.Contained);
        popupSv.AppendChild(itemsPresenter);
        popupBorder.AppendChild(popupSv);
        popup.AppendChild(popupBorder);
        grid.AppendChild(popup);

        var tpl = new ControlTemplate(typeof(ComboBox)) { VisualTree = grid };
        tpl.Triggers.Add(HoverTrigger("Bd", Border.BackgroundProperty, HoverBrush));

        var s = new Style(typeof(ComboBox));
        s.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 26, 4)));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }

    /// <summary>
    /// 悬浮窗右击菜单样式（v0.8 重做）：v0.7 以前这里只给 ContextMenu/MenuItem 设了
    /// 背景/前景色、**没给模板**——WPF 菜单项的实际外观（白底高亮、复选标记列、内边距）
    /// 全由系统默认模板决定，深色主题下菜单仍是浅底、与界面脱节（Sun 真机截图实锤）。
    /// 现在补齐纯代码模板：菜单面 = 调色板卡片色 + 描边 + 圆角；菜单项 = 统一项高、
    /// 复选列对齐、悬停走调色板 Hover 色。深色套下深底、浅色套下浅底，跟随当前调色板。
    /// </summary>
    public static Style ContextMenuStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(4));
        var sv = new FrameworkElementFactory(typeof(ScrollViewer));
        sv.SetValue(ScrollViewer.CanContentScrollProperty, true);
        sv.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        sv.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        sv.AppendChild(items);
        border.AppendChild(sv);
        var tpl = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };

        var s = new Style(typeof(ContextMenu));
        s.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }

    public static Style MenuItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var colMark = new FrameworkElementFactory(typeof(ColumnDefinition));
        colMark.SetValue(ColumnDefinition.WidthProperty, new GridLength(24));
        var colText = new FrameworkElementFactory(typeof(ColumnDefinition));
        colText.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        grid.AppendChild(colMark);
        grid.AppendChild(colText);
        var mark = new FrameworkElementFactory(typeof(TextBlock), "Mark");
        mark.SetValue(TextBlock.TextProperty, "✓");
        mark.SetValue(TextBlock.FontSizeProperty, 12.0);
        mark.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        mark.SetValue(TextBlock.ForegroundProperty, AccentBrush);
        mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        mark.SetValue(UIElement.OpacityProperty, 0.0);
        grid.AppendChild(mark);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetBinding(ContentPresenter.ContentProperty,
            new Binding(nameof(MenuItem.Header))
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        cp.SetValue(Grid.ColumnProperty, 1);
        grid.AppendChild(cp);
        border.AppendChild(grid);
        var tpl = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, HoverBrush, "Bd"));
        tpl.Triggers.Add(hover);
        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(Border.BackgroundProperty, HoverBrush, "Bd"));
        tpl.Triggers.Add(highlighted);
        var checkedT = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
        checkedT.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "Mark"));
        tpl.Triggers.Add(checkedT);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        tpl.Triggers.Add(disabled);

        var s = new Style(typeof(MenuItem));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 14, 0)));
        s.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }

    /// <summary>菜单分隔线（v0.8）：1px 调色板描边色、上下留 4、左右缩进与文字对齐。</summary>
    public static Style MenuSeparatorStyle()
    {
        var rect = new FrameworkElementFactory(typeof(Border));
        rect.SetValue(Border.BackgroundProperty, BorderBrush);
        rect.SetValue(FrameworkElement.HeightProperty, 1.0);
        rect.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 4, 10, 4));
        var tpl = new ControlTemplate(typeof(Separator)) { VisualTree = rect };
        var s = new Style(typeof(Separator));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }
}
