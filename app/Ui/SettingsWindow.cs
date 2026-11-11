using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 设置窗（v0.2 改版）：**所有改动实时生效**——控件一变就写 Settings、通知主程序
/// 重套悬浮窗样式，并防抖落盘；不再有「保存」动作，窗口不自动关闭（「完成」仅关窗）。
/// 条目照复刻规格：逐字开关、字号 12~36、背景 0~100、高亮/文字色（hex + 色块 + 颜色盘 + 12 预设）、
/// 偏移 ±100ms（±30s 钳制）；新增：字体、显示行数（单/双行）、锁定位置、Musixmatch Key。
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly CheckBox _wordByWord;
    private readonly CheckBox _showTrans;
    private readonly CheckBox _lockCheck;
    private readonly RadioButton _doubleLine;
    private readonly RadioButton _singleLine;
    private readonly Slider _fontSlider;
    private readonly TextBlock _fontValue;
    private readonly Slider _bgSlider;
    private readonly TextBlock _bgValue;
    private readonly ColorRow _hiRow;
    private readonly ColorRow _textRow;
    private readonly TextBlock _offsetValue;
    private readonly ComboBox _fontCombo;
    private readonly TextBox _mxmKey;
    private bool _loading = true;

    /// <summary>任意设置变化（主程序据此重套悬浮窗样式；持久化走 Settings.RequestSave 防抖）。</summary>
    public event Action? LiveChanged;

    private static readonly string[] Presets = ColorPickerWindow.Presets;

    private sealed class ColorRow
    {
        public required TextBox HexBox;
        public required Border Swatch;
        public string Hex
        {
            get => HexBox.Text.Trim();
            set { HexBox.Text = value; UpdateSwatch(); }
        }
        public void UpdateSwatch()
        {
            var c = FloatingWindow.ParseHex(HexBox.Text, Colors.Transparent);
            Swatch.Background = new SolidColorBrush(c);
        }
    }

    public SettingsWindow(Settings settings)
    {
        _settings = settings;
        Title = "SpotifyLyrics 设置 · v0.2";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;   // v0.2：普通窗口，带最小化+关闭
        ShowInTaskbar = true;
        WindowIcon.Apply(this);

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock
        {
            Text = "逐字与外观（改动实时生效，无需保存）",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _wordByWord = new CheckBox { Content = "平滑逐字高亮（渐变扫光）", Margin = new Thickness(0, 2, 0, 2) };
        _wordByWord.Checked += (_, _) => OnChanged(() => _settings.WordByWordEnabled = true);
        _wordByWord.Unchecked += (_, _) => OnChanged(() => _settings.WordByWordEnabled = false);
        panel.Children.Add(_wordByWord);
        _showTrans = new CheckBox { Content = "显示译文（歌词源带译文时）", Margin = new Thickness(0, 2, 0, 2) };
        _showTrans.Checked += (_, _) => OnChanged(() => _settings.ShowTranslation = true);
        _showTrans.Unchecked += (_, _) => OnChanged(() => _settings.ShowTranslation = false);
        panel.Children.Add(_showTrans);
        _lockCheck = new CheckBox { Content = "锁定悬浮窗位置（锁定后拖不动）", Margin = new Thickness(0, 2, 0, 8) };
        _lockCheck.Checked += (_, _) => OnChanged(() => _settings.PositionLocked = true);
        _lockCheck.Unchecked += (_, _) => OnChanged(() => _settings.PositionLocked = false);
        panel.Children.Add(_lockCheck);

        // 显示行数（v0.2 新增）
        panel.Children.Add(new TextBlock { Text = "显示行数", FontWeight = FontWeights.Bold });
        var lineRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        _doubleLine = new RadioButton { Content = "双行（当前行 + 下一行）", Margin = new Thickness(0, 0, 16, 0), GroupName = "lines" };
        _singleLine = new RadioButton { Content = "单行（仅当前行）", GroupName = "lines" };
        _doubleLine.Checked += (_, _) => OnChanged(() => _settings.SingleLineMode = false);
        _singleLine.Checked += (_, _) => OnChanged(() => _settings.SingleLineMode = true);
        lineRow.Children.Add(_doubleLine);
        lineRow.Children.Add(_singleLine);
        panel.Children.Add(lineRow);

        // 字号 12~36
        _fontValue = new TextBlock();
        panel.Children.Add(new TextBlock { Text = "歌词字号（12 ~ 36，长句会自动缩小）", FontWeight = FontWeights.Bold });
        _fontSlider = new Slider { Minimum = 12, Maximum = 36, TickFrequency = 1, IsSnapToTickEnabled = true };
        _fontSlider.ValueChanged += (_, _) =>
        {
            _fontValue.Text = $"当前：{(int)_fontSlider.Value}";
            OnChanged(() => _settings.FontSize = (int)_fontSlider.Value);
        };
        panel.Children.Add(_fontSlider);
        panel.Children.Add(_fontValue);

        // 颜色行（高亮 / 文字）
        _hiRow = BuildColorRow(panel, "当前行高亮颜色（已唱过的部分）",
            () => _settings.HighlightColorHex, v => _settings.HighlightColorHex = v);
        _textRow = BuildColorRow(panel, "其余文字颜色（未唱部分 / 下一行）",
            () => _settings.TextColorHex, v => _settings.TextColorHex = v);

        // 背景透明度 0~100
        _bgValue = new TextBlock();
        panel.Children.Add(new TextBlock
        {
            Text = "背景框透明度（0 ~ 100，0 = 只显示文字无背景框，推荐）",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 0)
        });
        _bgSlider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
        _bgSlider.ValueChanged += (_, _) =>
        {
            _bgValue.Text = $"当前：{(int)_bgSlider.Value}";
            OnChanged(() => _settings.BgAlphaPercent = (int)_bgSlider.Value);
        };
        panel.Children.Add(_bgSlider);
        panel.Children.Add(_bgValue);

        // 歌词偏移 ±100ms
        panel.Children.Add(new TextBlock
        {
            Text = "歌词时间偏移（正数 = 歌词提前）",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 0)
        });
        var offsetRow = new StackPanel { Orientation = Orientation.Horizontal };
        var minus = new Button { Content = "− 0.1s", Width = 70, Margin = new Thickness(0, 4, 6, 0) };
        var plus = new Button { Content = "+ 0.1s", Width = 70, Margin = new Thickness(0, 4, 6, 0) };
        _offsetValue = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        minus.Click += (_, _) => OnChanged(() =>
            _settings.LyricsOffsetMs = Math.Clamp(_settings.LyricsOffsetMs - 100, -30000, 30000), RefreshOffset);
        plus.Click += (_, _) => OnChanged(() =>
            _settings.LyricsOffsetMs = Math.Clamp(_settings.LyricsOffsetMs + 100, -30000, 30000), RefreshOffset);
        offsetRow.Children.Add(minus);
        offsetRow.Children.Add(plus);
        offsetRow.Children.Add(_offsetValue);
        panel.Children.Add(offsetRow);

        // 字体选择（新增项）
        panel.Children.Add(new TextBlock
        {
            Text = "字体（新增项；默认 Microsoft YaHei UI）",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 0)
        });
        _fontCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        var families = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(s => s).ToList();
        foreach (string f in families) _fontCombo.Items.Add(f);
        _fontCombo.SelectionChanged += (_, _) =>
        {
            if (_fontCombo.SelectedItem is string f)
                OnChanged(() => _settings.FontFamily = f);
        };
        panel.Children.Add(_fontCombo);

        // Musixmatch API Key（可选）
        panel.Children.Add(new TextBlock
        {
            Text = "Musixmatch API Key（可选，留空走桌面免 key 通道）",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 0)
        });
        _mxmKey = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
        _mxmKey.TextChanged += (_, _) => OnChanged(() => _settings.MusixmatchApiKey = _mxmKey.Text.Trim());
        panel.Children.Add(_mxmKey);

        // 按钮区：完成（仅关窗）/ 恢复默认（实时生效）
        var done = new Button { Content = "完成", Height = 34, Margin = new Thickness(0, 16, 0, 0) };
        done.Click += (_, _) => Close();
        panel.Children.Add(done);
        var reset = new Button { Content = "恢复默认外观（纯文字 + 绿色平滑逐字）", Height = 30, Margin = new Thickness(0, 8, 0, 0) };
        reset.Click += (_, _) =>
        {
            _loading = true;
            _settings.ResetStyle();
            LoadIntoControls();
            _loading = false;
            LiveChanged?.Invoke();
            _settings.RequestSave();
        };
        panel.Children.Add(reset);

        Content = panel;
        LoadIntoControls();
        _loading = false;
    }

    /// <summary>统一的实时变更入口：写设置 → 通知主程序 → 防抖落盘。加载控件时不触发。</summary>
    private void OnChanged(Action mutate, Action? after = null)
    {
        if (_loading) return;
        mutate();
        after?.Invoke();
        LiveChanged?.Invoke();
        _settings.RequestSave();
    }

    private ColorRow BuildColorRow(StackPanel panel, string label,
        Func<string> get, Action<string> set)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 0)
        });
        var row = new ColorRow
        {
            HexBox = new TextBox { Width = 110, VerticalContentAlignment = VerticalAlignment.Center },
            Swatch = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0, 0, 0)),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = "点开颜色盘"
            }
        };
        var pick = new Button
        {
            Content = "颜色盘…", Height = 30, Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        pick.Click += (_, _) => OpenColorPicker(row, get, set);
        // v0.2 修复：色块预览也可点开颜色盘（v0.1 的 Border 无任何点击处理，点了没反应）
        row.Swatch.MouseLeftButtonUp += (_, _) => OpenColorPicker(row, get, set);

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        hexRow.Children.Add(row.Swatch);
        hexRow.Children.Add(row.HexBox);
        hexRow.Children.Add(pick);
        row.HexBox.TextChanged += (_, _) =>
        {
            row.UpdateSwatch();
            if (Settings.IsValidHex(row.Hex))
                OnChanged(() => set(NormalizeHex(row.Hex)));
        };
        panel.Children.Add(hexRow);

        // 12 个预设色（原样），点选即填 hex 并实时生效（三处同步）
        var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (string p in Presets)
        {
            var c = FloatingWindow.ParseHex(p, Colors.Transparent);
            var btn = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 5, 5),
                Background = new SolidColorBrush(c),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0, 0, 0)),
                Tag = p
            };
            btn.Click += (_, _) => row.Hex = (string)btn.Tag;
            presets.Children.Add(btn);
        }
        panel.Children.Add(presets);
        return row;
    }

    /// <summary>
    /// 打开 HSV 颜色盘；任何异常都兜底提示（不许点了一点反应没有）。
    /// 兜底路径：提示用下方 12 个预设色或直接填 hex——它们一直在设置窗里可用。
    /// </summary>
    private void OpenColorPicker(ColorRow row, Func<string> get, Action<string> set)
    {
        try
        {
            var initial = FloatingWindow.ParseHex(get(), Colors.White);
            var dlg = new ColorPickerWindow(initial) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.PickedColor is Color picked)
                row.Hex = $"#FF{picked.R:X2}{picked.G:X2}{picked.B:X2}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"颜色盘打开失败（{ex.Message}）。\n可以直接点下方 12 个预设色块，或在输入框填 #AARRGGBB，效果一样实时生效。",
                "颜色盘", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshOffset()
    {
        long v = _settings.LyricsOffsetMs;
        _offsetValue.Text = v == 0 ? "当前：0 ms" : $"当前：{v:+#;-#;0} ms";
    }

    private void LoadIntoControls()
    {
        _wordByWord.IsChecked = _settings.WordByWordEnabled;
        _showTrans.IsChecked = _settings.ShowTranslation;
        _lockCheck.IsChecked = _settings.PositionLocked;
        _doubleLine.IsChecked = !_settings.SingleLineMode;
        _singleLine.IsChecked = _settings.SingleLineMode;
        _fontSlider.Value = _settings.FontSize;
        _fontValue.Text = $"当前：{_settings.FontSize}";
        _bgSlider.Value = _settings.BgAlphaPercent;
        _bgValue.Text = $"当前：{_settings.BgAlphaPercent}";
        _hiRow.Hex = _settings.HighlightColorHex;
        _textRow.Hex = _settings.TextColorHex;
        _fontCombo.SelectedItem = _settings.FontFamily;
        _mxmKey.Text = _settings.MusixmatchApiKey;
        RefreshOffset();
    }

    /// <summary>统一成 #AARRGGBB（6 位补 FF alpha；颜色强制不透明，照安卓颜色盘规则）</summary>
    private static string NormalizeHex(string hex)
    {
        string h = hex.Trim().TrimStart('#').ToUpperInvariant();
        if (h.Length == 6) h = "FF" + h;
        return "#" + h;
    }
}
