using System.Drawing;
using System.Windows;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Ui;

/// <summary>
/// 悬浮窗透明自检探针（v0.8.3，**只取证、不修复，不改任何渲染行为**）。
///
/// 背景：v0.8.1 起的诊断日志显示悬浮窗背景属性全透明，Sun 真机截图取样却是
/// 纯 #FFFFFF 实心白——涂色层不在 WPF 属性层，光看属性永远查不到。本探针换一层
/// 证据：用 CopyFromScreen 采悬浮窗在屏幕上**实际呈现**的像素（窗内四角内侧 +
/// 窗外参考点），写一行 [透明自检] 日志。若窗内四角实测为白而窗外为桌面原色，
/// 白色就是窗口矩形内被画出来的；若窗内≈窗外，屏幕呈现其实是透的，问题另在他处。
///
/// 采样点：四角取窗内距边 4px（避开文字主体与顶部胶囊；角部是根容器的圆角/
/// 留白区，本就无内容）；窗外参考点取窗口左侧 10px、同垂直中点，越界改取右侧。
/// 窗口不可见/太小/采样 API 失败一律静默跳过，绝不影响主流程。
/// 时机由 Program 安排：启动后 5 秒、每次主题切换后 2 秒。
/// </summary>
public static class TransparencyProbe
{
    private const int InsetPx = 4;    // 四角采样距窗边的物理像素
    private const int OutsidePx = 10; // 窗外参考点距窗边的物理像素

    public static void Sample(FloatingWindow window, Settings settings)
    {
        try
        {
            if (!window.IsVisible) return;
            double w = window.ActualWidth, h = window.ActualHeight;
            if (w < 24 || h < 24) return;

            // PointToScreen 返回物理像素坐标，与 CopyFromScreen 同一坐标系（含 DPI 缩放）
            var tl = window.PointToScreen(new System.Windows.Point(0, 0));
            var br = window.PointToScreen(new System.Windows.Point(w, h));
            int left = (int)Math.Round(tl.X), top = (int)Math.Round(tl.Y);
            int right = (int)Math.Round(br.X), bottom = (int)Math.Round(br.Y);
            if (right - left < 24 || bottom - top < 24) return;

            var corners = new[]
            {
                (left + InsetPx, top + InsetPx),                    // 左上
                (right - 1 - InsetPx, top + InsetPx),               // 右上
                (left + InsetPx, bottom - 1 - InsetPx),             // 左下
                (right - 1 - InsetPx, bottom - 1 - InsetPx)         // 右下
            };
            var hexes = new List<string>(4);
            foreach (var (x, y) in corners)
            {
                var c = SamplePixel(x, y);
                if (c == null) return; // 任一角采样失败整行跳过（证据不全不如不记）
                hexes.Add(TransparencyProbeText.Hex(c.Value.R, c.Value.G, c.Value.B));
            }

            int midY = (top + bottom) / 2;
            var outside = SamplePixel(left - OutsidePx, midY) ?? SamplePixel(right + OutsidePx, midY);
            if (outside == null) return;
            string outsideHex = TransparencyProbeText.Hex(outside.Value.R, outside.Value.G, outside.Value.B);

            int alpha = settings.BgAlphaPercent;
            string expectedBg = alpha <= 0 ? "Transparent" : "半透明黑";
            AppLog.Write(TransparencyProbeText.Format(hexes, outsideHex, expectedBg, alpha));
        }
        catch { /* 探针失败静默：取证不能反过来影响听歌 */ }
    }

    /// <summary>采一个屏幕像素；失败（越界/权限等）返回 null。</summary>
    private static Color? SamplePixel(int x, int y)
    {
        try
        {
            using var bmp = new Bitmap(1, 1);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(1, 1));
            return bmp.GetPixel(0, 0);
        }
        catch { return null; }
    }
}
