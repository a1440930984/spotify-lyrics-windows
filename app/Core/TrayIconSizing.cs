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
}
