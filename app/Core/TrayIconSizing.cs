namespace SpotifyLyricsWin.Core;

/// <summary>
/// 托盘图标的构造尺寸归一化（v0.8.5，纯逻辑、无头可测）。
///
/// 托盘图标改走 `new Icon(stream, size)` 让 GDI 在多帧 ICO 里选最接近的一帧：
/// size 应当是系统小图标尺寸（SM_CXSMICON，已含 DPI 缩放，如 100% 时 16、
/// 150% 时 24）。实测值落在合理区间 [8, 128] 才采信；取不到（0/负数）或
/// 离谱一律回退 20——宁可用一个接近的中档尺寸，也不能拿 256 大帧硬缩
/// （v0.8.3 的失真教训）或拿不存在的尺寸让 GDI 乱选。
/// </summary>
public static class TrayIconSizing
{
    /// <summary>系统小图标尺寸取不到/不可信时的回退边长（像素）。</summary>
    public const int Fallback = 20;

    /// <summary>把实测的系统小图标边长归一化为可用的构造尺寸。</summary>
    public static int Normalize(int measured)
        => measured is >= 8 and <= 128 ? measured : Fallback;

    /// <summary>
    /// 托盘专用图的放大倍数（v0.8.6）：主图标里绿圆约占帧宽 92%，托盘格
    /// 又小，Sun 要求托盘单独「调大一点点」——把原图按此倍数放大居中
    /// 绘制（溢出裁掉），绿圆占幅随之升到约 99%（0.92 × 1.08 ≈ 0.99），
    /// 只动托盘这一处，app.ico 本体与任务栏/标题栏图标都不受影响。
    /// </summary>
    public const double FullBleedZoom = 1.08;

    /// <summary>
    /// 放大居中绘制的目标矩形（纯函数）：在边长 canvas 的画布上，以 zoom
    /// 倍绘制正方形原图时的 (左上偏移, 绘制边长)；偏移为负即溢出被裁。
    /// </summary>
    public static (double Offset, double DrawSize) ZoomRect(int canvas, double zoom)
    {
        double draw = canvas * zoom;
        return ((canvas - draw) / 2.0, draw);
    }
}
