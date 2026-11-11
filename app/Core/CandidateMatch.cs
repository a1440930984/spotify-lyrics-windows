namespace SpotifyLyricsWin.Core;

/// <summary>
/// 手动选词「当前使用」判定（v0.8，纯逻辑供无头测试）：
/// 候选与当前已应用歌词同源（ProviderName 全等）且标题简体化后全等（不分大小写）→ 当前使用。
/// 当前来源/标题任一为空（还没取到词）→ 一律 false。
/// </summary>
public static class CandidateMatch
{
    public static bool IsCurrent(string candidateProvider, string candidateTitle,
        string? currentProvider, string? currentTitle)
    {
        if (string.IsNullOrWhiteSpace(currentProvider) || string.IsNullOrWhiteSpace(currentTitle))
            return false;
        if (!string.Equals(candidateProvider, currentProvider, StringComparison.Ordinal))
            return false;
        string a = Simplifier.ToSimplified(candidateTitle ?? "").Trim();
        string b = Simplifier.ToSimplified(currentTitle).Trim();
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
