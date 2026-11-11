using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;
using SpotifyLyricsWin.Core;
using SpotifyLyricsWin.Lyrics;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 手动选词（v0.2 新增，照安卓版「手动搜词」行为；v0.5 候选卡片化+位置记忆；
/// v0.6 换 WPF-UI FluentWindow 外壳 + 卡片模板应用期自检 + 全程日志）：
/// 预填当前曲目（**预填即简体化显示**，搜索关键词在服务层也统一简体）→
/// 点搜索并行查各源候选 → 每个候选一张小卡（歌名/歌手/时长 + 来源徽章/逐字徽章，选中绿描边）→
/// 点选并应用 → 立即上屏 + 按当前曲目 key 覆盖缓存 → 卡片上标「✓ 已应用」。
/// 单源失败只在底部状态行标明该源原因，不中断其他源。
/// 搜索/应用的实际工作由主程序通过委托注入，本窗只管交互。
/// </summary>
public sealed class ManualLyricsWindow : FluentWindow
{
    /// <summary>候选卡视图模型（属性供卡片 DataTemplate 绑定；Display/ToString 为模板加载失败时的纯文本兜底）。</summary>
    public sealed class CandidateRow
    {
        public required LyricCandidate Candidate { get; init; }
        public bool Applied { get; set; }
        /// <summary>v0.8：这条就是当前正在用的歌词（同源同标题，见 Core.CandidateMatch）。</summary>
        public bool IsCurrent { get; set; }
        public string Title => Candidate.Title;
        public string Artist => Candidate.Artist;
        public string ProviderName => Candidate.ProviderName;
        public string DurationText => Candidate.DurationText;
        public string WordLabel => Candidate.WordLevel ? "逐字" : "逐行";
        /// <summary>卡片右上角状态字：刚应用 > 当前使用（两者同时成立时显示已应用）。</summary>
        public string StatusText => Applied ? "✓ 已应用" : IsCurrent ? "● 当前使用" : "";
        public string Display =>
            $"[{Candidate.ProviderName}] {Candidate.Title} - {Candidate.Artist} · {Candidate.DurationText} · " +
            (Candidate.WordLevel ? "逐字" : "逐行") + (Applied ? " ✓ 已应用" : IsCurrent ? " ● 当前使用" : "");
        public override string ToString() => Display;
    }

    private readonly Settings _settings;
    private readonly TextBox _titleBox;
    private readonly TextBox _artistBox;
    private readonly Button _searchButton;
    private readonly Button _applyButton;
    private readonly ListBox _list;
    private readonly TextBlock _status = new();
    private CancellationTokenSource? _searchCts;
    private bool _applying;

    /// <summary>v0.8：当前已应用歌词的来源名与标题（主程序注入；候选里同源同标题的那条标「当前使用」并置顶）。</summary>
    public string CurrentSourceName { get; set; } = "";
    public string CurrentTitle { get; set; } = "";

    /// <summary>搜索委托：(歌名, 歌手) → 各源分组结果。</summary>
    public Func<string, string, CancellationToken, Task<IReadOnlyList<CandidateGroup>>>? SearchAsync { get; set; }
    /// <summary>应用委托：候选 → 是否成功（主程序负责取词、上屏、覆盖缓存）。</summary>
    public Func<LyricCandidate, Task<bool>>? ApplyAsync { get; set; }
    /// <summary>「标记为纯音乐」按钮（v0.6）：主程序负责落设置、停取词、切提示。</summary>
    public event Action? MarkInstrumentalRequested;

    public ManualLyricsWindow(string title, string artist, Settings settings)
    {
        _settings = settings;
        Title = "手动选词";
        Width = 560;
        Height = 540;
        MinWidth = 440;
        MinHeight = 380;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ExtendsContentIntoTitleBar = true;
        // v0.7：与设置窗同一规则（纯黑/深蓝关 Mica 让主题色真正生效，其余保留 Mica）
        WindowBackdropType = Theme.Backdrop;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titleBar = new TitleBar { Title = "手动选词", ShowMaximize = false };
        // v0.8：每个窗都带 app 图标（窗口 Icon + Fluent 标题栏图标）
        WindowIcon.Apply(this);
        if (WindowIcon.LoadImageSource() is { } iconSrc)
            titleBar.Icon = new ImageIcon { Source = iconSrc };
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var panel = new DockPanel { Margin = new Thickness(14, 6, 14, 14) };

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "歌名", FontSize = 13.5 });
        // 预填显示为简体（SMTC 常给繁体标题/歌手，安卓版用 ChineseConverter 转简后再填）
        _titleBox = new TextBox { Text = Simplifier.ToSimplified(title), Margin = new Thickness(0, 5, 0, 10) };
        top.Children.Add(_titleBox);
        top.Children.Add(new TextBlock { Text = "歌手", FontSize = 13.5 });
        _artistBox = new TextBox { Text = Simplifier.ToSimplified(artist), Margin = new Thickness(0, 5, 0, 10) };
        top.Children.Add(_artistBox);
        _searchButton = new Button
        {
            Content = "搜索（并行查各歌词源）", Height = 34,
            Appearance = ControlAppearance.Primary,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _searchButton.Click += async (_, _) => await DoSearchAsync();
        top.Children.Add(_searchButton);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _applyButton = new Button
        {
            Content = "应用选中歌词", Height = 34, IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _applyButton.Click += async (_, _) => await DoApplyAsync();
        bottom.Children.Add(_applyButton);
        var markButton = new Button
        {
            Content = "把当前曲目标记为纯音乐（不再取词）", Height = 30,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        markButton.Click += (_, _) =>
        {
            MarkInstrumentalRequested?.Invoke();
            _status.Text = "已标记为纯音乐：本曲不再自动取词，悬浮窗显示「纯音乐」提示。可在右击菜单或设置里取消标记。";
            AppLog.Write("[手动选词] 用户在本窗把当前曲目标记为纯音乐");
        };
        bottom.Children.Add(markButton);
        _status.Text = "改歌名/歌手后点搜索；选中一张卡再点「应用」即可换词。";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Theme.SubTextBrush;
        _status.FontSize = 12;
        _status.Margin = new Thickness(1, 7, 0, 0);
        bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);

        _list = new ListBox
        {
            Margin = new Thickness(0, 12, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemContainerStyle = CardItemStyle()
        };
        // v0.8：横向滚动条默认不出现（卡片宽度自适应窗宽，不需要横滚）；纵向保持 Auto
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        var cardTpl = LoadCardTemplate();
        if (cardTpl != null) _list.ItemTemplate = cardTpl; // 加载/自检失败保持纯文本行（CandidateRow.ToString）
        _list.SelectionChanged += (_, _) =>
            _applyButton.IsEnabled = _list.SelectedItem is CandidateRow && !_applying;
        _list.MouseDoubleClick += async (_, _) => await DoApplyAsync();
        panel.Children.Add(_list);

        Grid.SetRow(panel, 1);
        root.Children.Add(panel);
        Content = root;

        // 位置记忆：首开居中，之后回到上次位置（关窗时存坐标）
        if (settings.ManualWindowX is double mx && settings.ManualWindowY is double my)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = mx;
            Top = my;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        Closing += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
            {
                _settings.ManualWindowX = Left;
                _settings.ManualWindowY = Top;
                _settings.RequestSave();
            }
        };
    }

    /// <summary>
    /// 候选卡片模板（XamlReader 加载；失败返回 null → 回退纯文本行显示）。
    /// v0.6 两点加固：① 颜色不再写死深色，取当前主题调色板（切主题重开窗口即换装）；
    /// ② 应用期自检（照 v0.5.1 ThinScroll 的教训：XAML 解析成功 ≠ 模板能应用——
    /// v0.5.0 正是模板在「应用期」抛 XamlParseException 把设置窗打开即崩，用户反馈的
    /// 「手动搜词后整程序退出」高度怀疑同类问题）。加载后立刻把模板离屏套到一条
    /// 探针候选上强制走完模板实例化+绑定+布局，任何异常都回退纯文本行并记日志。
    /// </summary>
    private static DataTemplate? LoadCardTemplate()
    {
        string card = Theme.PanelColor.ToString();
        string edge = Theme.BorderColor.ToString();
        string text = Theme.TextColor.ToString();
        string sub = Theme.SubTextColor.ToString();
        string accent = Theme.AccentColor.ToString();
        string badge = Theme.SelectedRowColor.ToString();
        string chip = Theme.HoverColor.ToString();
        string xaml = $$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Border CornerRadius="10" Padding="12,9" Margin="2,2,2,5"
                      Background="{{card}}" BorderBrush="{{edge}}" BorderThickness="1">
                <StackPanel>
                  <Grid>
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="*"/>
                      <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="{Binding Title}" FontSize="12" FontWeight="Bold"
                               Foreground="{{text}}" TextTrimming="CharacterEllipsis"/>
                    <TextBlock Grid.Column="1" Text="{Binding StatusText}" Foreground="{{accent}}"
                               FontSize="11" Margin="8,0,0,0"/>
                  </Grid>
                  <TextBlock Margin="0,3,0,0" FontSize="11" Foreground="{{sub}}"
                             TextTrimming="CharacterEllipsis"><Run Text="{Binding Artist}"/><Run Text=" · "/><Run Text="{Binding DurationText}"/></TextBlock>
                  <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
                    <Border CornerRadius="6" Background="{{badge}}" Padding="7,2" Margin="0,0,6,0">
                      <TextBlock Text="{Binding ProviderName}" FontSize="11" Foreground="{{accent}}"/>
                    </Border>
                    <Border CornerRadius="6" Background="{{chip}}" Padding="7,2">
                      <TextBlock Text="{Binding WordLabel}" FontSize="11" Foreground="{{sub}}"/>
                    </Border>
                  </StackPanel>
                </StackPanel>
              </Border>
            </DataTemplate>
            """;
        DataTemplate tpl;
        try
        {
            using var sr = new StringReader(xaml);
            using var xr = XmlReader.Create(sr);
            tpl = (DataTemplate)XamlReader.Load(xr);
        }
        catch (Exception ex)
        {
            AppLog.LogException("手动选词·卡片模板解析失败（已回退纯文本行）", ex);
            return null;
        }

        // 应用期自检：离屏 ContentPresenter 套一遍模板（实例化 + 绑定求值 + 布局）
        try
        {
            var probe = new CandidateRow
            {
                Candidate = new LyricCandidate
                {
                    ProviderName = "自检", Title = "自检", Artist = "自检",
                    DurationMs = 1000, WordLevel = true, Key = "selftest"
                }
            };
            var presenter = new ContentPresenter
            {
                Content = probe, ContentTemplate = tpl, Width = 420
            };
            presenter.Measure(new Size(420, double.PositiveInfinity));
            presenter.Arrange(new Rect(0, 0, 420, Math.Max(1, presenter.DesiredSize.Height)));
            presenter.UpdateLayout();
        }
        catch (Exception ex)
        {
            AppLog.LogException("手动选词·卡片模板应用期自检失败（已回退纯文本行）", ex);
            return null;
        }
        return tpl;
    }

    /// <summary>卡片外框：选中时绿描边、悬停时描边微亮（纯代码工厂模板）。</summary>
    private static Style CardItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(cp);
        var tpl = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty,
            Theme.Brush(Color.FromRgb(0x44, 0x44, 0x44)), "Bd"));
        tpl.Triggers.Add(hover);
        var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        sel.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.AccentBrush, "Bd"));
        tpl.Triggers.Add(sel);

        var s = new Style(typeof(ListBoxItem));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Theme.TextBrush));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        s.Setters.Add(new Setter(Control.FontFamilyProperty, Theme.UiFont));
        s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
        return s;
    }

    private async Task DoSearchAsync()
    {
        if (SearchAsync == null) return;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        _searchButton.IsEnabled = false;
        _applyButton.IsEnabled = false;
        _list.Items.Clear();
        _status.Text = "正在并行查询各源…";
        try
        {
            var groups = await SearchAsync(_titleBox.Text.Trim(), _artistBox.Text.Trim(), ct);
            if (ct.IsCancellationRequested) return;
            int total = 0;
            var summary = new List<string>();
            var rows = new List<CandidateRow>();
            foreach (var g in groups)
            {
                if (g.Result.Error is string err)
                {
                    summary.Add($"{g.ProviderName}：失败（{err}）");
                    continue;
                }
                summary.Add($"{g.ProviderName}：{g.Result.Candidates.Count} 条");
                foreach (var c in g.Result.Candidates)
                {
                    rows.Add(new CandidateRow
                    {
                        Candidate = c,
                        // v0.8：与当前已应用歌词同源同标题 → 标「当前使用」
                        IsCurrent = CandidateMatch.IsCurrent(
                            c.ProviderName, c.Title, CurrentSourceName, CurrentTitle)
                    });
                    total++;
                }
            }
            // v0.8：「当前使用」置顶（稳定排序，其余维持原分数/来源顺序）
            foreach (var row in rows.OrderByDescending(r => r.IsCurrent))
                _list.Items.Add(row);
            _status.Text = total > 0
                ? $"共 {total} 条候选。来源情况——{string.Join("｜", summary)}"
                : $"没有搜到候选。来源情况——{string.Join("｜", summary)}";
            // v0.6 日志补全：手动搜索的关键词与逐源结果落盘（v0.5 时这里一个字都不记，
            // 出问题只能靠用户口述）。
            AppLog.Write($"[手动选词] 搜索「{_titleBox.Text.Trim()} - {_artistBox.Text.Trim()}」：共 {total} 条候选；{string.Join("；", summary)}");
        }
        catch (OperationCanceledException) { /* 新的搜索接管 */ }
        catch (Exception ex)
        {
            _status.Text = $"搜索失败：{ex.Message}";
            AppLog.LogException("手动选词·搜索", ex);
        }
        finally { _searchButton.IsEnabled = true; }
    }

    private async Task DoApplyAsync()
    {
        if (ApplyAsync == null || _applying) return;
        if (_list.SelectedItem is not CandidateRow row) return;
        _applying = true;
        _applyButton.IsEnabled = false;
        _status.Text = $"正在从「{row.Candidate.ProviderName}」取词并应用…";
        try
        {
            bool ok = await ApplyAsync(row.Candidate);
            if (ok)
            {
                foreach (var item in _list.Items)
                    if (item is CandidateRow r)
                    {
                        r.Applied = ReferenceEquals(r, row);
                        r.IsCurrent = ReferenceEquals(r, row);
                    }
                _list.Items.Refresh();
                _status.Text = $"已应用「{row.Candidate.ProviderName}」的歌词，并记为当前曲目的歌词（覆盖缓存）。";
                AppLog.Write($"[手动选词] 已应用候选：{row.Candidate.ProviderName}「{row.Candidate.Title} - {row.Candidate.Artist}」");
            }
            else
            {
                _status.Text = "取词失败：该候选没能取到可用歌词，换一条试试。";
                AppLog.Write($"[手动选词] 应用候选失败（源未返回可用歌词）：{row.Candidate.ProviderName}「{row.Candidate.Title} - {row.Candidate.Artist}」key={row.Candidate.Key}");
            }
        }
        catch (Exception ex)
        {
            _status.Text = $"应用失败：{ex.Message}";
            AppLog.LogException("手动选词·应用候选", ex);
        }
        finally
        {
            _applying = false;
            _applyButton.IsEnabled = _list.SelectedItem is CandidateRow;
        }
    }
}
