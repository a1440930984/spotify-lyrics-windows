using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SpotifyLyricsWin.Ui;

/// <summary>系统托盘（WinForms NotifyIcon 互操作）：显示/隐藏歌词、设置、手动选词、重新搜词、退出。</summary>
public sealed class Tray : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? ToggleFloating;
    public event Action? OpenSettings;
    public event Action? ManualPick;
    public event Action? RefetchLyrics;
    public event Action? OpenLog;
    public event Action? ExitApp;

    public Tray()
    {
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "SpotifyLyrics 悬浮歌词 v0.3",
            Visible = true
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示/隐藏歌词", null, (_, _) => ToggleFloating?.Invoke());
        menu.Items.Add("设置…", null, (_, _) => OpenSettings?.Invoke());
        menu.Items.Add("手动选词…", null, (_, _) => ManualPick?.Invoke());
        menu.Items.Add("重新搜词", null, (_, _) => RefetchLyrics?.Invoke());
        menu.Items.Add("打开日志", null, (_, _) => OpenLog?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenSettings?.Invoke();
    }

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
