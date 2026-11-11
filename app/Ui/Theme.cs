using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// Spotify 风深色主题（v0.3）：所有页面弃用默认 Windows 样式。
/// 配色：背景 #121212、面板 #1E1E1E、描边 #2A2A2A、主文字 #FFFFFF、
/// 次文字 #B3B3B3、强调 #1ED760。
/// 样式与控件模板全部用代码构建（FrameworkElementFactory），零 XAML。
/// 用法：窗口构造时 Resources.MergedDictionaries.Add(Theme.BuildResources())。
/// </summary>
public static class Theme
{
    public static readonly Color BgColor = Color.FromRgb(0x12, 0x12, 0x12);
    public static readonly Color PanelColor = Color.FromRgb(0x1E, 0x1E, 0x1E);
    public static readonly Color BorderColor = Color.FromRgb(0x2A, 0x2A, 0x2A);
    public static readonly Color TextColor = Colors.White;
    public static readonly Color SubTextColor = Color.FromRgb(0xB3, 0xB3, 0xB3);
    public static readonly Color AccentColor = Color.FromRgb(0x1E, 0xD7, 0x60);
    public static readonly Color HoverColor = Color.FromRgb(0x2A, 0x2A, 0x2A);
    public static readonly Color PressedColor = Color.FromRgb(0x33, 0x33, 0x33);
    public static readonly Color SelectedRowColor = Color.FromRgb(0x24, 0x3A, 0x2B);
    public static readonly Color TitleBarColor = Color.FromRgb(0x18, 0x18, 0x18);
    public static readonly Color CloseHoverColor = Color.FromRgb(0xE8, 0x11, 0x23);

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static readonly SolidColorBrush BgBrush = Brush(BgColor);
    public static readonly SolidColorBrush PanelBrush = Brush(PanelColor);
    public static readonly SolidColorBrush BorderBrush = Brush(BorderColor);
    public static readonly SolidColorBrush TextBrush = Brush(TextColor);
    public static readonly SolidColorBrush SubTextBrush = Brush(SubTextColor);
    public static readonly SolidColorBrush AccentBrush = Brush(AccentColor);
    public static readonly SolidColorBrush HoverBrush = Brush(HoverColor);
    public static readonly SolidColorBrush PressedBrush = Brush(PressedColor);

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

    /// <summary>悬浮窗右击菜单的深色样式（菜单不是三窗之一，顺手统一）。</summary>
    public static Style ContextMenuStyle()
    {
        var s = new Style(typeof(ContextMenu));
        s.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.BorderBrushProperty, BorderBrush));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        return s;
    }

    public static Style MenuItemStyle()
    {
        var s = new Style(typeof(MenuItem));
        s.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
        return s;
    }
}
