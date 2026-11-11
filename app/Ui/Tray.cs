using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SpotifyLyricsWin.Ui;

/// <summary>系统托盘（WinForms NotifyIcon 互操作）：显示/隐藏歌词、设置、手动选词、重新搜词、退出。</summary>
public sealed class Tray : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _notAdItem;
    private readonly ToolStripMenuItem _instrumentalItem;

    public event Action? ToggleFloating;
    public event Action? OpenSettings;
    public event Action? ManualPick;
    public event Action? RefetchLyrics;
    public event Action? OpenLog;
    public event Action? ExitApp;
    /// <summary>v0.4：托盘气泡被点击（用于「发现新版」气泡 → 打开设置）。</summary>
    public event Action? BalloonClicked;
    /// <summary>v0.6：广告误判纠偏（与悬浮窗右击菜单同一入口）。</summary>
    public event Action? NotAd;
    /// <summary>v0.6：标记/取消标记当前曲目为纯音乐。</summary>
    public event Action? ToggleInstrumental;
    /// <summary>v0.8：托盘菜单「检查更新」（走主程序的手动检查路径，有无新版都给反馈）。</summary>
    public event Action? CheckUpdate;
    /// <summary>v0.8：托盘菜单「重置歌词窗位置」（悬浮窗飞出屏幕时一键找回）。</summary>
    public event Action? ResetFloatingPosition;

    public Tray()
    {
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "SpotifyLyrics 悬浮歌词",
            Visible = true
        };
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke();
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示/隐藏歌词", null, (_, _) => ToggleFloating?.Invoke());
        menu.Items.Add("设置…", null, (_, _) => OpenSettings?.Invoke());
        menu.Items.Add("手动选词…", null, (_, _) => ManualPick?.Invoke());
        menu.Items.Add("重新搜词", null, (_, _) => RefetchLyrics?.Invoke());
        _notAdItem = new ToolStripMenuItem("这不是广告") { Enabled = false };
        _notAdItem.Click += (_, _) => NotAd?.Invoke();
        menu.Items.Add(_notAdItem);
        _instrumentalItem = new ToolStripMenuItem("标记为纯音乐");
        _instrumentalItem.Click += (_, _) => ToggleInstrumental?.Invoke();
        menu.Items.Add(_instrumentalItem);
        menu.Items.Add("打开日志", null, (_, _) => OpenLog?.Invoke());
        menu.Items.Add("检查更新", null, (_, _) => CheckUpdate?.Invoke());
        menu.Items.Add("重置歌词窗位置", null, (_, _) => ResetFloatingPosition?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenSettings?.Invoke();
    }

    /// <summary>v0.6：广告提示状态变化时同步托盘「这不是广告」可用性（Program 维护）。</summary>
    public void SetAdState(bool isAd) => _notAdItem.Enabled = isAd;

    /// <summary>v0.6：当前曲目纯音乐标记状态变化时同步托盘菜单文案。</summary>
    public void SetInstrumentalState(bool marked) =>
        _instrumentalItem.Text = marked ? "取消纯音乐标记" : "标记为纯音乐";

    /// <summary>v0.2：托盘图标用 app.ico（与 exe/窗口同款）；读不到时回退运行时绘制。</summary>
    private static Icon LoadIcon()
    {
        try
        {
            string ico = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(ico)) return new Icon(ico);
        }
        catch { /* 回退到运行时绘制 */ }
        return MakeIcon();
    }

    public void SetStatus(string text)
    {
        // NotifyIcon.Text 上限 63 字符
        _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    /// <summary>v0.4：托盘气泡（如「发现新版」；不弹窗、不打断听歌，点气泡打开设置）。</summary>
    public void ShowBalloon(string title, string text)
    {
        try { _icon.ShowBalloonTip(8000, title, text, ToolTipIcon.Info); }
        catch { /* 气泡失败不影响主流程 */ }
    }

    /// <summary>运行时画一个托盘图标：Spotify 绿圆 + 白色音符。</summary>
    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var green = new SolidBrush(Color.FromArgb(0x1E, 0xD7, 0x60));
            g.FillEllipse(green, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 17, System.Drawing.FontStyle.Bold);
            using var white = new SolidBrush(Color.White);
            g.DrawString("♪", font, white, 6, 3);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
