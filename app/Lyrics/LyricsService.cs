using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Lyrics;

/// <summary>取词结果：解析好的歌词 + 命中的源名。</summary>
public sealed class FetchOutcome
{
public required Core.Lyrics Lyrics { get; init;}
public required string SourceName { get; init;}
}

/// <summary>手动选词：一个源的分组结果（候选 + 失败原因）。</summary>
public sealed class CandidateGroup
{
public required string ProviderName { get; init;}
public required CandidateSearchResult Result { get; init;}
}

/// <summary>
/// 取词编排（顺序由用户定，雷打不动）：
/// 酷狗 KRC → QQ 音乐 → 网易云 → 酷我 → LRCLIB → lyrics.ovh → Musixmatch。
/// 命中即停；每个源独立 try/catch；结果按「标题|歌手」简体化 key 缓存
/// 到 %AppData%/SpotifyLyricsWin/cache/（存增强 LRC 文本 + 可选译文）。
/// </summary>
public sealed class LyricsService
{
private readonly IReadOnlyList<ILyricsProvider> _providers;

public LyricsService(Settings settings)
{
_providers = new ILyricsProvider[]
{
new KugouProvider(),
new QqProvider(),
new NetEaseProvider(),
new KuwoProvider(),
new LrclibProvider(),
new LyricsOvhProvider(),
new MusixmatchProvider(() => settings.MusixmatchApiKey)
};
}

/// <summary>
/// 关键词组合（参考安卓版搜词策略）：「歌名 歌手」「歌名」「去后缀歌名 歌手」「去后缀歌名」。
/// </summary>
public static IReadOnlyList<string> BuildCombos(string title, string artist)
{
string clean = CleanTitle(title);
var list = new List<string>();
void Add(string s) { s = s.Trim(); if (s.Length > 0 &&!list.Contains(s)) list.Add(s);}
Add($"{title} {artist}");
Add(title);
if (clean!= title)
{
Add($"{clean} {artist}");
Add(clean);
}
return list;
}

/// <summary>去掉歌名里的括号后缀与 " - xxx" 后缀，用于多组合尝试。</summary>
public static string CleanTitle(string title)
{
string t = title.Trim();
t = Regex.Replace(t, @"\s*[\(\[（]*[\)\]）】]\s*$", "");
int dash = t.IndexOf(" - ", StringComparison.Ordinal);
if (dash > 0) t = t.Substring(0, dash);
return t.Trim();
}

private static string CacheFileFor(string key, string suffix)
{
string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
return Path.Combine(Settings.CacheDir, hash + suffix);
}

public async Task<FetchOutcome?> FetchAsync(TrackInfo track, bool skipCache, CancellationToken ct)
{
string key = track.Key;
if (!skipCache)
{
var cached = TryReadCache(key, track);
if (cached!= null) return cached;
}

var combos = BuildCombos(track.Title, track.Artist);
foreach (var provider in _providers)
{
ct.ThrowIfCancellationRequested();
ProviderResult? hit = null;
try
{
hit = await provider.FetchAsync(track.Title, track.Artist,
track.DurationMs, combos, ct).ConfigureAwait(false);
}
catch (OperationCanceledException) { throw;}
catch { /* 单源异常不得中断整条链 */}
if (hit == null || string.IsNullOrWhiteSpace(hit.LrcText)) continue;

var lyrics = ParseWithTranslations(hit, track);
if (lyrics.Lines.Count == 0) continue;
WriteCache(key, hit);
return new FetchOutcome { Lyrics = lyrics, SourceName = provider.Name};
}
return null;
}

private static Core.Lyrics ParseWithTranslations(ProviderResult hit, TrackInfo track)
{
var main = LrcParser.Parse(hit.LrcText, track.Title, track.Artist);
if (!string.IsNullOrWhiteSpace(hit.TransLrcText))
{
var trans = LrcParser.Parse(hit.TransLrcText);
var aligned = LrcParser.AlignTranslations(main, trans);
// 中文歌的「译文」多半是原文重复，没有实际译文价值时保持原样（源给了就对齐展示）
main = new Core.Lyrics(main.Lines, main.Title, main.Artist, aligned);
}
return main;
}

private FetchOutcome? TryReadCache(string key, TrackInfo track)
{
try
{
string file = CacheFileFor(key, ".lrc");
if (!File.Exists(file)) return null;
string text = File.ReadAllText(file);
if (string.IsNullOrWhiteSpace(text)) return null;
string transFile = CacheFileFor(key, ".trans.lrc");
var hit = new ProviderResult
{
LrcText = text,
TransLrcText = File.Exists(transFile)? File.ReadAllText(transFile): null
};
var lyrics = ParseWithTranslations(hit, track);
if (lyrics.Lines.Count == 0) return null;
return new FetchOutcome { Lyrics = lyrics, SourceName = "本地缓存"};
}
catch { return null;}
}

private static void WriteCache(string key, ProviderResult hit)
{
try
{
Directory.CreateDirectory(Settings.CacheDir);
File.WriteAllText(CacheFileFor(key, ".lrc"), hit.LrcText);
if (!string.IsNullOrWhiteSpace(hit.TransLrcText))
File.WriteAllText(CacheFileFor(key, ".trans.lrc"), hit.TransLrcText);
}
catch { /* 缓存写失败不影响显示 */}
}

// ---------- 手动选词（v0.2） ----------

/// <summary>
/// 并行查各源候选（实现 ICandidateProvider 的源：酷狗/QQ/网易云/酷我/LRCLIB）。
/// 单源失败只记在该组 Error 里，不中断其他源（照安卓版行为）。
/// </summary>
public async Task<IReadOnlyList<CandidateGroup>> SearchCandidatesAsync(
string title, string artist, CancellationToken ct)
{
var tasks = new List<Task<CandidateGroup>>();
foreach (var p in _providers)
{
if (p is not ICandidateProvider cp) continue;
var provider = p;
tasks.Add(Task.Run(async () =>
{
try
{
var r = await cp.SearchCandidatesAsync(title, artist, ct).ConfigureAwait(false);
return new CandidateGroup { ProviderName = provider.Name, Result = r};
}
catch (OperationCanceledException) { throw;}
catch (Exception ex)
{
return new CandidateGroup
{
ProviderName = provider.Name,
Result = new CandidateSearchResult { Error = ex.Message}
};
}
}, ct));
}
return await Task.WhenAll(tasks).ConfigureAwait(false);
}

/// <summary>
/// 按候选取词 → 解析 → 按当前曲目 key 覆盖缓存 → 返回可直接上屏的结果。
/// </summary>
public async Task<FetchOutcome?> FetchCandidateAsync(LyricCandidate candidate, TrackInfo track,
CancellationToken ct)
{
foreach (var p in _providers)
{
if (p.Name!= candidate.ProviderName || p is not ICandidateProvider cp) continue;
ProviderResult? hit;
try { hit = await cp.FetchCandidateAsync(candidate, ct).ConfigureAwait(false);}
catch (OperationCanceledException) { throw;}
catch { return null;}
if (hit == null || string.IsNullOrWhiteSpace(hit.LrcText)) return null;
var lyrics = ParseWithTranslations(hit, track);
if (lyrics.Lines.Count == 0) return null;
WriteCache(track.Key, hit);
return new FetchOutcome { Lyrics = lyrics, SourceName = p.Name};
}
return null;
}
}
