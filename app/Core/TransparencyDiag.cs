namespace SpotifyLyricsWin.Core;

/// <summary>
/// 悬浮窗透明诊断快照（v0.8.1）：ApplyStyle 时把「窗口背景 / 根容器背景 /
/// 歌词区 Effect 残留数 / 背景透明度设置值」拼成一行日志，Sun 若仍复现
/// 背景不透明，回传 log.txt 里这行即可定因，不用再猜。
/// v0.8.5：新增 compBg——窗口**合成层**背景色（HwndSource.CompositionTarget
/// .BackgroundColor）。白底案最终定案是涂色发生在合成层（WPF-UI 的
/// RemoveBackdrop 会把它涂成系统窗口色）、属性层看不见，故诊断必须带上它：
/// compBg=Transparent 才是合成层真正清零的证据。
/// 纯函数：入参都是已归类的字符串/计数，格式化在此处统一（无头可测）。
/// </summary>
public static class TransparencyDiag
{
    /// <summary>画刷归类：null / Transparent / Solid(#AARRGGBB) / Other。</summary>
    public static string Describe(bool isNull, string? argbHex)
        => isNull ? "null" : argbHex ?? "Other";

    public static string Format(string windowBg, string rootBg, string compBg, int effectCount, int bgAlphaPercent)
        => $"[透明诊断] windowBg={windowBg} rootBg={rootBg} compBg={compBg} effects={effectCount} alpha={bgAlphaPercent}";
}
