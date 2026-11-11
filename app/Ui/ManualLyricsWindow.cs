using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpotifyLyricsWin.Lyrics;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 手动选词（v0.2 新增，照安卓版「手动搜词」行为；v0.3 改自绘深色主题）：
/// 预填当前曲目（**预填即简体化显示**，搜索关键词在服务层也统一简体）→
/// 点搜索并行查各源候选 → 列表显示 来源/歌名/歌手/时长/逐字·逐行 →
/// 点选并应用 → 立即上屏 + 按当前曲目 key 覆盖缓存 → 行尾标「✓ 已应用」。
/// 单源失败只在底部状态行标明该源原因，不中断其他源。
/// 搜索/应用的实际工作由主程序通过委托注入，本窗只管交互。
/// </summary>
public sealed class ManualLyricsWindow : ThemedWindow
{
    /// <summary>候选行视图模型（Display 即列表显示文本，应用后刷新）。</summary>
    public sealed class CandidateRow
    {
        public required LyricCandidate Candidate { get; init; }
        public bool Applied { get; set; }
        public string Display =>
            $"[{Candidate.ProviderName}] {Candidate.Title} - {Candidate.Artist} · {Candidate.DurationText} · " +
            (Candidate.WordLevel ? "逐字" : "逐行") + (Applied ? " ✓ 已应用" : "");
        public override string ToString() => Display;
    }

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

    public ManualLyricsWindow(string title, string artist) : base("手动选词", showMinimize: true)
    {
        Width = 560;
        Height = 520;
        MinWidth = 440;
        MinHeight = 380;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new DockPanel { Margin = new Thickness(12) };

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "歌名", FontWeight = FontWeights.Bold });
        // v0.3：预填显示为简体（SMTC 常给繁体标题/歌手，安卓版用 ChineseConverter 转简后再填）
        _titleBox = new TextBox { Text = Core.Simplifier.ToSimplified(title), Margin = new Thickness(0, 4, 0, 8) };
        top.Children.Add(_titleBox);
        top.Children.Add(new TextBlock { Text = "歌手", FontWeight = FontWeights.Bold });
        _artistBox = new TextBox { Text = Core.Simplifier.ToSimplified(artist), Margin = new Thickness(0, 4, 0, 8) };
        top.Children.Add(_artistBox);
        _searchButton = new Button { Content = "搜索（并行查各歌词源）", Height = 32, Style = Theme.AccentButtonStyle() };
        _searchButton.Click += async (_, _) => await DoSearchAsync();
        top.Children.Add(_searchButton);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        _applyButton = new Button
        {
            Content = "应用选中歌词", Height = 32, IsEnabled = false
        };
        _applyButton.Click += async (_, _) => await DoApplyAsync();
        bottom.Children.Add(_applyButton);
        _status = new TextBlock
        {
            Text = "改歌名/歌手后点搜索；选中一行再点「应用」即可换词。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme.SubTextBrush,
            Margin = new Thickness(1, 6, 0, 0)
        };
        bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);

        _list = new ListBox { Margin = new Thickness(0, 10, 0, 0) };
        _list.SelectionChanged += (_, _) =>
            _applyButton.IsEnabled = _list.SelectedItem is CandidateRow && !_applying;
        _list.MouseDoubleClick += async (_, _) => await DoApplyAsync();
        panel.Children.Add(_list);

        SetBody(panel);
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
