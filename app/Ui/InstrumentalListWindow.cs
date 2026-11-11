using System.Windows;
using System.Windows.Controls;
using SpotifyLyricsWin.Core;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 纯音乐标记名单窗（v0.8 从设置卡里独立出来）：列出全部已标记曲目
/// （Key 易读化为「标题 - 歌手」），每条可单独移除，底部可清空全部。
/// 行是纯代码生成的 Grid（不用 XAML 模板，免模板应用期风险）；变更即写设置+防抖落盘，
/// 并通过回调通知设置窗刷新计数。标题不带版本号、带 app 图标（照 v0.8 规范）。
/// </summary>
public sealed class InstrumentalListWindow : FluentWindow
{
    private readonly Settings _settings;
    private readonly Action _onChanged;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _countText = new();

    public InstrumentalListWindow(Settings settings, Action onChanged)
    {
        _settings = settings;
        _onChanged = onChanged;
        Title = "纯音乐标记";
        Width = 480;
        Height = 460;
        MinWidth = 380;
        MinHeight = 300;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = Theme.Backdrop;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titleBar = new TitleBar { Title = "纯音乐标记", ShowMaximize = false };
        WindowIcon.Apply(this);
        if (WindowIcon.LoadImageSource() is { } iconSrc)
            titleBar.Icon = new ImageIcon { Source = iconSrc };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var panel = new DockPanel { Margin = new Thickness(14, 6, 14, 14) };
        _countText.Foreground = Theme.SubTextBrush;
        _countText.FontSize = 12;
        _countText.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(_countText, Dock.Top);
        panel.Children.Add(_countText);

        var clearBtn = new Button
        {
            Content = "清空全部标记", Height = 32, Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        clearBtn.Click += (_, _) =>
        {
            if (_settings.InstrumentalKeys.Count == 0) return;
            _settings.InstrumentalKeys.Clear();
            _settings.RequestSave();
            AppLog.Write("[纯音乐] 名单窗内清空全部标记");
            RefreshRows();
            _onChanged();
        };
        DockPanel.SetDock(clearBtn, Dock.Bottom);
        panel.Children.Add(clearBtn);

        var scroll = new ScrollViewer
        {
            Content = _rows,
            Margin = new Thickness(0, 8, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        panel.Children.Add(scroll);

        Grid.SetRow(panel, 1);
        root.Children.Add(panel);
        Content = root;
        RefreshRows();
    }

    private void RefreshRows()
    {
        _rows.Children.Clear();
        var keys = _settings.InstrumentalKeys;
        _countText.Text = keys.Count == 0
            ? "暂无标记。在悬浮窗右击菜单、托盘菜单或手动选词窗里可把曲目标记为纯音乐；标题带「纯音乐/Instrumental」等词的也会自动标记。"
            : $"共 {keys.Count} 条（上限 {InstrumentalMarks.Cap}，新标记自动挤掉最旧的）。被标记的曲目不再自动取词：";
        foreach (string key in keys)
        {
            string captured = key;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = InstrumentalMarks.FormatKey(key),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);
            var remove = new Button { Content = "移除", Height = 28, Width = 64, Margin = new Thickness(8, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                InstrumentalMarks.Unmark(_settings, captured);
                AppLog.Write($"[纯音乐] 名单窗内移除标记：{InstrumentalMarks.FormatKey(captured)}");
                RefreshRows();
                _onChanged();
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            var card = new Border
            {
                Background = Theme.PanelBrush,
                BorderBrush = Theme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 8, 6),
                Child = row
            };
            _rows.Children.Add(card);
        }
    }
}
