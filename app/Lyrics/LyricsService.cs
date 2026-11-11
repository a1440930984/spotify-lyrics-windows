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
/// <summary>v0.3：打分选优路径的得分（顺序回退路径为 -1）与选中的候选标题，供诊断/测试。</summary>
public int Score { get; init;} = -1;
public string CandidateTitle { get; init;} = "";
}

/// <summary>手动选词：一个源的分组结果（候选 + 失败原因）。</summary>
public sealed class CandidateGroup
{
public required string ProviderName { get; init;}
public required CandidateSearchResult Result { get; init;}
}

/// <summary>
/// 取词编排。v0.3 策略：缓存 → 国内四源并行候选打分选优（≥50 分） →
/// 回退顺序首命中（顺序由用户定，雷打不动）：
/// 酷狗 KRC → QQ 音乐 → 网易云 → 酷我 → LRCLIB → lyrics.ovh → Musixmatch。
/// 每个源独立 try/catch；结果按「标题|歌手」简体化 key 缓存
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

/// <summary>主歌手：按 , 、 / &amp; ; 与 feat./ft./featuring 切分取第一位（蓝本 LyricFlow keywordVariants）。</summary>
public static string MainArtist(string artist)
{
if (string.IsNullOrWhiteSpace(artist)) return "";
foreach (string part in Regex.Split(artist, @"[,、/&;]|feat\.?|ft\.?|featuring", RegexOptions.IgnoreCase))
{
string p = part.Trim();
if (p.Length > 0) return p;
}
return artist.Trim();
}

/// <summary>
/// 关键词变体（v0.6 扩充，蓝本 LyricFlow Util.kt 的 keywordVariants：最多 8 组、干净组排前）。
/// 输入应已简体化（服务层入口统一做）。顺序即各源尝试顺序：
/// 干净歌名+主歌手 → 干净歌名 → 干净歌名+全歌手 → 原歌名+全歌手 → 原歌名 →
/// 去 feat 歌名+全歌手 → 原歌名+主歌手 → 去 feat 歌名。
/// </summary>
public static IReadOnlyList<string> BuildCombos(string title, string artist)
{
string clean = CleanTitle(title);
string main = MainArtist(artist);
string noFeat = StripFeat(title);
var list = new List<string>();
void Add(string s) { s = Regex.Replace(s.Trim(), @"\s+", " "); if (s.Length > 0 &&!list.Contains(s)) list.Add(s);}
Add($"{clean} {main}");
Add(clean);
Add($"{clean} {artist}");
Add($"{title} {artist}");
Add(title);
if (noFeat!= title) Add($"{noFeat} {artist}");
if (main.Length > 0 && main!= artist) Add($"{title} {main}");
if (noFeat!= title) Add(noFeat);
return list.Take(8).ToList();
}

/// <summary>去掉标题里的 feat./ft. 段（括号内或行内均可）。</summary>
public static string StripFeat(string title)
{
string t = Regex.Replace(title, @"\s*[\(\[（【][^)\]）】]*(feat|ft\.|featuring)[^)\]）】]*[\)\]）】]", " ", RegexOptions.IgnoreCase);
t = Regex.Replace(t, @"\s+(feat\.?|ft\.?|featuring)\s+.*$", "", RegexOptions.IgnoreCase);
return Regex.Replace(t, @"\s+", " ").Trim();
}

/// <summary>
/// 清洗歌名（v0.6 升级，蓝本 LyricFlow cleanTitle + normalize 的括号口径）：
/// ① 剥「- 版本词」后缀（Remastered/Live/Acoustic/伴奏/纯音乐 等）；
/// ② 剥任意位置的带内容括号段（(Remastered)、(feat. …)、【】、（）都算）；
/// ③ 折叠多余空白。v0.3 版只剥尾部空括号、带内容的剥不掉，是变体偏弱的主因。
/// </summary>
public static string CleanTitle(string title)
{
string t = title.Trim();
t = Regex.Replace(t, @"\s*[-—–]\s*(original|remaster(ed)?|live|instrumental|acoustic|single|radio edit|explicit|伴奏|纯音乐|现场版?).*$", "", RegexOptions.IgnoreCase);
t = Regex.Replace(t, @"\s*[\(\[（【][^)\]）】]*[\)\]）】]", " ");
return Regex.Replace(t, @"\s+", " ").Trim();
}

private static string CacheFileFor(string key, string suffix)
{
string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
return Path.Combine(Settings.CacheDir, hash + suffix);
}

public async Task<FetchOutcome?> FetchAsync(TrackInfo track, bool skipCache, CancellationToken ct) =>
(await FetchReportAsync(track, skipCache, ct).ConfigureAwait(false)).Outcome;

/// <summary>取词失败分类（v0.6，借鉴 V2.4 的 networkProblem vs 没收录；解析型为本端细分）。</summary>
public enum FetchFailureKind
{
/// <summary>命中（Outcome 非空）或未失败。</summary>
None,
/// <summary>各源都有响应但没有可用歌词：真没收录，不重试、立即收手。</summary>
NoResult,
/// <summary>网络型失败（请求异常/超时为主）：临时故障，值得退避重试。</summary>
Network,
/// <summary>有源返回了内容但解析不出可用行：不重试（重试也是同样的内容）。</summary>
Parse
}

/// <summary>一次取词的完整报告：结果 + 失败分类 + 人话说明（供 Program 决定重试与写日志）。</summary>
public sealed record FetchReport(FetchOutcome? Outcome, FetchFailureKind Failure, string Detail);

public async Task<FetchReport> FetchReportAsync(TrackInfo track, bool skipCache, CancellationToken ct)
{
string key = track.Key;
if (!skipCache)
{
var cached = TryReadCache(key, track);
if (cached!= null) return new FetchReport(cached, FetchFailureKind.None, "本地缓存");
}

int netFailuresBefore = HttpHelp.NetworkFailureCount;

// v0.3 策略：先「各源并行搜候选 → 打分选优」（≥50 分才采用）；
// 无人达标时回退到下方的顺序首命中（酷狗优先），LRCLIB 等海外源只在回退链里出现。
var (scored, anyResponse) = await FetchByScoringAsync(track, ct).ConfigureAwait(false);
if (scored!= null) return new FetchReport(scored, FetchFailureKind.None, "打分选优");

var (seq, sawUnparsable) = await FetchSequentialAsync(track, ct).ConfigureAwait(false);
if (seq!= null) return new FetchReport(seq, FetchFailureKind.None, "顺序回退");

// 失败分类：
// - 有源返回了内容但解析不出行 → 解析型；
// - 打分阶段至少一个源正常响应过（目录真被查过，就是没有）→ 没收录型
//   （个别源的网络抖动不改变结论，无头实测踩过：一首不存在的歌被 2 次
//   边缘超时误判成 Network 会白白触发整轮重试）；
// - 一个源都没响应成功且期间有网络层失败 → 网络型（值得退避重试）。
int netDelta = HttpHelp.NetworkFailureCount - netFailuresBefore;
if (sawUnparsable)
return new FetchReport(null, FetchFailureKind.Parse, "有源返回内容但解析不出可用歌词行");
if (anyResponse)
return new FetchReport(null, FetchFailureKind.NoResult, "各源均无匹配结果（没收录）");
if (netDelta > 0)
return new FetchReport(null, FetchFailureKind.Network, $"取词期间出现 {netDelta} 次网络层失败且无任何源响应成功（超时/连不上）");
return new FetchReport(null, FetchFailureKind.NoResult, "各源均无匹配结果（没收录）");
}

/// <summary>
/// v0.3 打分选优：并行查各源候选（总阶段上限约 6 秒，单源 4 秒超时不变），
/// 逐个打分取最高；最高分 ≥50 才取词采用。超时/无候选/取词失败都返回 null 走回退。
/// v0.6 起 LRCLIB 也进打分池（Beyond《長城》实案：国内四源搜索前 10 条里根本没有
/// Beyond 版、候选最高才 30 分，唯一能命中的 LRCLIB 候选有 100 分却只在顺序回退链
/// 末端等着，整链走完要 9 秒且 LRCLIB 当时不可达就全盘落空——见 v0.6 汇报 B 案）。
/// 各源并行查，LRCLIB 慢也只受同一 6 秒阶段上限约束，不拖慢国内源。
/// </summary>
private async Task<(FetchOutcome? Outcome, bool AnyResponse)> FetchByScoringAsync(TrackInfo track, CancellationToken ct)
{
IReadOnlyList<CandidateGroup> groups;
using (var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
{
phaseCts.CancelAfter(TimeSpan.FromSeconds(6));
try
{
groups = await SearchCandidatesAsync(track.Title, track.Artist, phaseCts.Token,
includeOverseas: true).ConfigureAwait(false);
}
catch (OperationCanceledException) when (!ct.IsCancellationRequested)
{
return (null, false); // 只是本阶段 6 秒到点，走回退链
}
}
// 任一源无错误地响应过 = 目录确实被查过（失败分类用，见 FetchReportAsync）
bool anyResponse = groups.Any(g => g.Result.Error == null);
LyricCandidate? best = null;
int bestScore = int.MinValue;
foreach (var g in groups)
{
foreach (var c in g.Result.Candidates)
{
if (CandidateScorer.TitleEliminated(c, track.Title)) continue; // v0.6 标题淘汰线
int s = CandidateScorer.Score(c, track.Title, track.Artist, track.DurationMs);
if (s > bestScore) { bestScore = s; best = c;}
}
}
if (best == null || bestScore < 50) return (null, anyResponse);
var outcome = await FetchCandidateAsync(best, track, ct).ConfigureAwait(false);
if (outcome == null) return (null, anyResponse);
return (new FetchOutcome
{
Lyrics = outcome.Lyrics, SourceName = outcome.SourceName,
Score = bestScore, CandidateTitle = best.Title
}, anyResponse);
}

/// <summary>v0.2 的顺序首命中逻辑（酷狗 → QQ → 网易云 → 酷我 → LRCLIB → ovh → Musixmatch），v0.3 降为回退。
/// 返回值第二项：本轮是否有源返回了内容但解析不出可用行（失败分类用）。</summary>
private async Task<(FetchOutcome? Outcome, bool SawUnparsable)> FetchSequentialAsync(TrackInfo track, CancellationToken ct)
{
// 搜索关键词统一用简体（v0.3）：SMTC 给的标题/歌手可能是繁体
string title = Simplifier.ToSimplified(track.Title);
string artist = Simplifier.ToSimplified(track.Artist);
string key = track.Key;
bool sawUnparsable = false;
var combos = BuildCombos(title, artist);
foreach (var provider in _providers)
{
ct.ThrowIfCancellationRequested();
ProviderResult? hit = null;
try
{
hit = await provider.FetchAsync(title, artist,
track.DurationMs, combos, ct).ConfigureAwait(false);
}
catch (OperationCanceledException) { throw;}
catch { /* 单源异常不得中断整条链 */}
if (hit == null || string.IsNullOrWhiteSpace(hit.LrcText)) continue;

var lyrics = ParseWithTranslations(hit, track);
if (lyrics.Lines.Count == 0) { sawUnparsable = true; continue; }
WriteCache(key, hit);
return (new FetchOutcome { Lyrics = lyrics, SourceName = provider.Name }, sawUnparsable);
}
return (null, sawUnparsable);
}

private static Core.Lyrics ParseWithTranslations(ProviderResult hit, TrackInfo track)
{
var main = LrcParser.Parse(hit.LrcText, track.Title, track.Artist);
if (!string.IsNullOrWhiteSpace(hit.TransLrcText))
{
// v0.6：中文歌跳过译文（借鉴清单 3-3，阈值取 LyricFlow 实证的汉字 >45%）：
// 网易云对中文歌给的 tlyric 常是原文重复/繁体版，贴上去只是同义重复行。
string joined = string.Join(" ", main.Lines.Select(l => l.Text));
if (!TextClassify.IsChineseSong(joined))
{
var trans = LrcParser.Parse(hit.TransLrcText);
var aligned = LrcParser.AlignTranslations(main, trans);
main = new Core.Lyrics(main.Lines, main.Title, main.Artist, aligned);
}
}
return main;
}

/// <summary>歌词是否已带可用译文（任一行非空）。</summary>
public static bool HasUsableTranslation(Core.Lyrics lyrics) =>
lyrics.Translations.Any(t => !string.IsNullOrWhiteSpace(t));

/// <summary>
/// v0.6 后台补译（借鉴清单 #5 的第一级）：主源无译文时向网易云单独补 tlyric，
/// 用新对齐算法（容差合并）贴到已有歌词上；成功则写 .trans.lrc 缓存并返回新歌词对象。
/// 中文歌直接跳过（不发请求）。失败/没有返回 null，调用方保持原歌词不动。
/// </summary>
public async Task<Core.Lyrics?> FetchTranslationBackupAsync(TrackInfo track, Core.Lyrics current,
CancellationToken ct)
{
string joined = string.Join(" ", current.Lines.Select(l => l.Text));
if (TextClassify.IsChineseSong(joined)) return null;
var netease = _providers.OfType<NetEaseProvider>().FirstOrDefault();
if (netease == null) return null;
string title = Simplifier.ToSimplified(track.Title);
string artist = Simplifier.ToSimplified(track.Artist);
string? transText;
try { transText = await netease.FetchTranslationOnlyAsync(title, artist, track.DurationMs, ct).ConfigureAwait(false);}
catch (OperationCanceledException) { throw;}
catch { return null;}
if (string.IsNullOrWhiteSpace(transText)) return null;
var trans = LrcParser.Parse(transText);
if (trans.Lines.Count == 0) return null;
var aligned = LrcParser.AlignTranslations(current, trans);
if (!aligned.Any(t => !string.IsNullOrWhiteSpace(t))) return null;
WriteCacheTrans(track.Key, transText);
AppLog.Write($"[译文] 「{track.Title}」 已后台补取网易云 tlyric 并对齐（{aligned.Count(t => !string.IsNullOrWhiteSpace(t))}/{current.Lines.Count} 行）");
return new Core.Lyrics(current.Lines, current.Title, current.Artist, aligned);
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

/// <summary>单独写译文缓存（后台补译成功后调用，与正文缓存同 key 配对）。</summary>
public static void WriteCacheTrans(string key, string transLrcText)
{
try
{
Directory.CreateDirectory(Settings.CacheDir);
File.WriteAllText(CacheFileFor(key, ".trans.lrc"), transLrcText);
}
catch { /* 缓存写失败不影响显示 */}
}

// ---------- 手动选词（v0.2） ----------

/// <summary>
/// 并行查各源候选（实现 ICandidateProvider 的源：酷狗/QQ/网易云/酷我/LRCLIB）。
/// 单源失败只记在该组 Error 里，不中断其他源（照安卓版行为）。
/// includeOverseas=false 时不查 LRCLIB（仅国内四源）；v0.6 起自动打分阶段也传 true
/// （《長城》实案，见 FetchByScoringAsync 说明），手动选词同为 true（五源全查）。
/// 关键词在入口统一简体化（v0.3）：传入繁体标题/歌手也会以简体搜各源。
/// </summary>
public async Task<IReadOnlyList<CandidateGroup>> SearchCandidatesAsync(
string title, string artist, CancellationToken ct, bool includeOverseas = true)
{
title = Simplifier.ToSimplified(title ?? "");
artist = Simplifier.ToSimplified(artist ?? "");
var tasks = new List<Task<CandidateGroup>>();
foreach (var p in _providers)
{
if (p is not ICandidateProvider cp) continue;
if (!includeOverseas && p.Name == "LRCLIB") continue;
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
