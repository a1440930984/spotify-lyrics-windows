using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;
using SpotifyLyricsWin.Core;
using SpotifyLyricsWin.Lyrics;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 手动选词（v0.2 新增，照安卓版「手动搜词」行为；v0.3 改自绘深色主题；v0.5 候选卡片化+位置记忆）：
/// 预填当前曲目（**预填即简体化显示**，搜索关键词在服务层也统一简体）→
/// 点搜索并行查各源候选 → 每个候选一张小卡（歌名/歌手/时长 + 来源徽章/逐字徽章，选中绿描边）→
/// 点选并应用 → 立即上屏 + 按当前曲目 key 覆盖缓存 → 卡片上标「✓ 已应用」。
/// 单源失败只在底部状态行标明该源原因，不中断其他源。
/// 搜索/应用的实际工作由主程序通过委托注入，本窗只管交互。
/// </summary>
public sealed class ManualLyricsWindow : ThemedWindow
{
    /// <summary>候选卡视图模型（属性供卡片 DataTemplate 绑定；Display/ToString 为模板加载失败时的纯文本兜底）。</summary>
    public sealed class CandidateRow
    {
        public required LyricCandidate Candidate { get; init; }
        public bool Applied { get; set; }
        public string Title => Candidate.Title;
        public string Artist => Candidate.Artist;
        public string ProviderName => Candidate.ProviderName;
        public string DurationText => Candidate.DurationText;
        public string WordLabel => Candidate.WordLevel ? "逐字" : "逐行";
        public string AppliedText => Applied ? "✓ 已应用" : "";
        public string Display =>
            $"[{Candidate.ProviderName}] {Candidate.Title} - {Candidate.Artist} · {Candidate.DurationText} · " +
            (Candidate.WordLevel ? "逐字" : "逐行") + (Applied ? " ✓ 已应用" : "");
        public override string ToString() => Display;
    }

    private readonly Settings _settings;
    private readonly TextBox _titleBox;
    private readonly TextBox _artistBox;
    private readonly Button _searchButton;
    private readonly Button _applyButton;
    private readonly ListBox _list;
    private readonly TextBlock _status;
    private CancellationTokenSource? _searchCts;
    private bool _applying;

    /// <summary>搜索委托：(歌名, 歌手) → 各源分组结果。</summary>
    public Func<string, string, CancellationToken, Task<IReadOnlyList<CandidateGroup>>>? SearchAsync { get; set; }
    /// <summary>应用委托：候选 → 是否成功（主程序负责取词、上屏、覆盖缓存）。</summary>
    public Func<LyricCandidate, Task<bool>>? ApplyAsync { get; set; }

    public ManualLyricsWindow(string title, string artist, Settings settings)
        : base("手动选词", showMinimize: true)
    {
        _settings = settings;
        Width = 560;
        Height = 520;
        MinWidth = 440;
        MinHeight = 380;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var panel = new DockPanel { Margin = new Thickness(14) };

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "歌名", FontSize = 13.5 });
        // v0.3：预填显示为简体（SMTC 常给繁体标题/歌手，安卓版用 ChineseConverter 转简后再填）
        _titleBox = new TextBox { Text = Simplifier.ToSimplified(title), Margin = new Thickness(0, 5, 0, 10) };
        top.Children.Add(_titleBox);
        top.Children.Add(new TextBlock { Text = "歌手", FontSize = 13.5 });
        _artistBox = new TextBox { Text = Simplifier.ToSimplified(artist), Margin = new Thickness(0, 5, 0, 10) };
        top.Children.Add(_artistBox);
        _searchButton = new Button { Content = "搜索（并行查各歌词源）", Height = 34, Style = Theme.AccentButtonStyle() };
        _searchButton.Click += async (_, _) => await DoSearchAsync();
        top.Children.Add(_searchButton);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _applyButton = new Button
        {
            Content = "应用选中歌词", Height = 34, IsEnabled = false
        };
        _applyButton.Click += async (_, _) => await DoApplyAsync();
        bottom.Children.Add(_applyButton);
        _status = new TextBlock
        {
            Text = "改歌名/歌手后点搜索；选中一张卡再点「应用」即可换词。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme.SubTextBrush,
            FontSize = 12,
            Margin = new Thickness(1, 7, 0, 0)
        };
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
        var cardTpl = LoadCardTemplate();
        if (cardTpl != null) _list.ItemTemplate = cardTpl; // 加载失败保持纯文本行（CandidateRow.ToString）
        _list.SelectionChanged += (_, _) =>
            _applyButton.IsEnabled = _list.SelectedItem is CandidateRow && !_applying;
        _list.MouseDoubleClick += async (_, _) => await DoApplyAsync();
        panel.Children.Add(_list);

        SetBody(panel);

        // v0.5 位置记忆：首开居中，之后回到上次位置
        UsePositionMemory(settings.ManualWindowX, settings.ManualWindowY, (x, y) =>
        {
            _settings.ManualWindowX = x;
            _settings.ManualWindowY = y;
            _settings.RequestSave();
        });
    }

    /// <summary>候选卡片模板（XamlReader 加载；失败返回 null → 回退纯文本行显示）。</summary>
    private static DataTemplate? LoadCardTemplate()
    {
        const string xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Border CornerRadius="10" Padding="12,9" Margin="2,2,2,5"
                      Background="#242424" BorderBrush="#2E2E2E" BorderThickness="1">
                <StackPanel>
                  <Grid>
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="*"/>
                      <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="{Binding Title}" FontSize="13.5" FontWeight="Bold"
                               Foreground="#FFFFFF" TextTrimming="CharacterEllipsis"/>
                    <TextBlock Grid.Column="1" Text="{Binding AppliedText}" Foreground="#1ED760"
                               FontSize="12" Margin="8,0,0,0"/>
                  </Grid>
                  <TextBlock Margin="0,3,0,0" FontSize="12" Foreground="#B3B3B3"
                             TextTrimming="CharacterEllipsis"><Run Text="{Binding Artist}"/><Run Text=" · "/><Run Text="{Binding DurationText}"/></TextBlock>
                  <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
                    <Border CornerRadius="6" Background="#123B21" Padding="7,2" Margin="0,0,6,0">
                      <TextBlock Text="{Binding ProviderName}" FontSize="11" Foreground="#1ED760"/>
                    </Border>
                    <Border CornerRadius="6" Background="#2A2A2A" Padding="7,2">
                      <TextBlock Text="{Binding WordLabel}" FontSize="11" Foreground="#B3B3B3"/>
                    </Border>
                  </StackPanel>
                </StackPanel>
              </Border>
            </DataTemplate>
            """;
        try
        {
            using var sr = new StringReader(xaml);
            using var xr = XmlReader.Create(sr);
            return (DataTemplate)XamlReader.Load(xr);
        }
        catch { return null; }
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
                    _list.Items.Add(new CandidateRow { Candidate = c });
                    total++;
                }
            }
            _status.Text = total > 0
                ? $"共 {total} 条候选。来源情况——{string.Join("｜", summary)}"
                : $"没有搜到候选。来源情况——{string.Join("｜", summary)}";
        }
        catch (OperationCanceledException) { /* 新的搜索接管 */ }
        catch (Exception ex) { _status.Text = $"搜索失败：{ex.Message}"; }
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
                    if (item is CandidateRow r) r.Applied = ReferenceEquals(r, row);
                _list.Items.Refresh();
                _status.Text = $"已应用「{row.Candidate.ProviderName}」的歌词，并记为当前曲目的歌词（覆盖缓存）。";
            }
            else
            {
                _status.Text = "取词失败：该候选没能取到可用歌词，换一条试试。";
            }
        }
        catch (Exception ex) { _status.Text = $"应用失败：{ex.Message}"; }
        finally
        {
            _applying = false;
            _applyButton.IsEnabled = _list.SelectedItem is CandidateRow;
        }
    }
}
