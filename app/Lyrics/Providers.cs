using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Lyrics;

/// <summary>一个歌词源的命中结果：LRC 文本（可为增强 LRC）+ 可选译文 LRC 文本。</summary>
public sealed class ProviderResult
{
public required string LrcText { get; init;}
public string? TransLrcText { get; init;}
}

/// <summary>歌词源接口。顺序由 LyricsService 固定：酷狗 → QQ → 网易云 → 酷我 → LRCLIB → lyrics.ovh → Musixmatch。</summary>
public interface ILyricsProvider
{
string Name { get;}
Task<ProviderResult?> FetchAsync(string title, string artist, long durationMs,
IReadOnlyList<string> combos, CancellationToken ct);
}

/// <summary>手动选词的候选（一个源里的一条可取词结果）。</summary>
public sealed class LyricCandidate
{
public required string ProviderName { get; init;}
public required string Title { get; init;}
public required string Artist { get; init;}
public long DurationMs { get; init;}
/// <summary>逐字（true）/ 逐行（false）。酷狗 KRC 为真逐字；LRCLIB 视文本是否含词级标签。</summary>
public bool WordLevel { get; init;}
/// <summary>源内取词所需的 id/key（各源自定格式）。</summary>
public required string Key { get; init;}
/// <summary>LRCLIB 直接把同步词文本放在这里，取词免再发请求。</summary>
public string? Payload { get; init;}
public string DurationText => DurationMs > 0? $"{DurationMs / 60000}:{(DurationMs / 1000) % 60:00}": "--:--";
}

/// <summary>一个源的候选搜索结果：候选列表 + 失败原因（null = 没失败，可能只是无结果）。</summary>
public sealed class CandidateSearchResult
{
public List<LyricCandidate> Candidates { get;} = new();
public string? Error { get; set;}
}

/// <summary>
/// 支持手动选词的源（v0.2）。lyrics.ovh 无候选概念、Musixmatch 免 key 通道不可用，均不实现。
/// 实现要求：搜不到返回空列表；接口/网络级失败才填 Error；单个实现内部异常由 LyricsService 兜底。
/// </summary>
public interface ICandidateProvider
{
Task<CandidateSearchResult> SearchCandidatesAsync(string title, string artist, CancellationToken ct);
Task<ProviderResult?> FetchCandidateAsync(LyricCandidate candidate, CancellationToken ct);
}

/// <summary>公共 HTTP 与匹配工具。每个请求独立超时（默认 4 秒），异常一律吞掉返回 null。</summary>
public static class HttpHelp
{
public static readonly HttpClient Client = Create();

private static HttpClient Create()
{
var c = new HttpClient(new HttpClientHandler
{
AutomaticDecompression = DecompressionMethods.All
});
c.DefaultRequestHeaders.UserAgent.ParseAdd(
"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
return c;
}

public static async Task<string?> GetStringAsync(string url, CancellationToken ct,
int timeoutSec = 4, string? referer = null, string? userAgent = null)
{
try
{
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
using var req = new HttpRequestMessage(HttpMethod.Get, url);
if (referer!= null) req.Headers.Referrer = new Uri(referer);
if (userAgent!= null) req.Headers.UserAgent.ParseAdd(userAgent);
using var resp = await Client.SendAsync(req, cts.Token).ConfigureAwait(false);
if (!resp.IsSuccessStatusCode) return null;
return await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
}
catch { return null;}
}

public static string Enc(string s) => Uri.EscapeDataString(s);

// ---------- 匹配 ----------

/// <summary>归一化：简体化、小写、去空白与常见标点，供标题/歌手比对。</summary>
public static string Norm(string s)
{
if (string.IsNullOrWhiteSpace(s)) return "";
s = Simplifier.ToSimplified(s).ToLowerInvariant();
var sb = new StringBuilder(s.Length);
foreach (char c in s)
{
if (char.IsWhiteSpace(c)) continue;
if ("，。,.!?;:()（）[]-—…、~♪'\"/\\&+·".IndexOf(c) >= 0) continue;
sb.Append(c);
}
return sb.ToString();
}

public static bool TitleMatches(string candidate, string target)
{
string a = Norm(candidate), b = Norm(target);
if (a.Length == 0 || b.Length == 0) return false;
if (a == b) return true;
// 一方包含另一方（如带 (Live) 后缀），且较短一方不能太短以免误配
return Math.Min(a.Length, b.Length) >= 2 && (a.Contains(b) || b.Contains(a));
}

public static bool ArtistMatches(string candidate, string target)
{
if (string.IsNullOrWhiteSpace(target)) return true;
string cand = Norm(candidate);
if (cand.Length == 0) return false;
foreach (string part in Regex.Split(target, @"[,、/&]|feat\.?|ft\.?", RegexOptions.IgnoreCase))
{
string p = Norm(part);
if (p.Length >= 1 && (cand.Contains(p) || p.Contains(cand))) return true;
}
return false;
}

public static bool DurationClose(long aMs, long bMs, long toleranceMs = 8000)
{
if (aMs <= 0 || bMs <= 0) return true;
return Math.Abs(aMs - bMs) <= toleranceMs;
}

public static JsonElement? ParseJson(string? text)
{
if (string.IsNullOrWhiteSpace(text)) return null;
try { return JsonDocument.Parse(text).RootElement.Clone();}
catch { return null;}
}

public static string Str(JsonElement e, string name) =>
e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
? v.ValueKind == JsonValueKind.String? v.GetString()?? "": v.ToString()
: "";

public static long Num(JsonElement e, string name) =>
e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n)? n
: long.TryParse(v.ToString(), out long m)? m: 0
: 0;
}

// =====================================================================
// 1. 酷狗 KRC（真逐字）
// 搜索：mobilecdn.kugou.com/api/v3/search/song → hash
// 歌词候选：krcs.kugou.com/search（带 hash+duration）→ id/accesskey
// 下载：lyrics.kugou.com/download?fmt=krc → KrcDecoder 解密转增强 LRC
// 兜底：lyrics.kugou.com/search 直接拿候选
// =====================================================================
public sealed class KugouProvider: ILyricsProvider, ICandidateProvider
{
public string Name => "酷狗 KRC";

public async Task<ProviderResult?> FetchAsync(string title, string artist, long durationMs,
IReadOnlyList<string> combos, CancellationToken ct)
{
foreach (string kw in combos)
{
// 路线 A：mobilecdn 拿 hash
string? searchJson = await HttpHelp.GetStringAsync(
$"http://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={HttpHelp.Enc(kw)}&page=1&pagesize=10", ct);
var root = HttpHelp.ParseJson(searchJson);
if (root is JsonElement r && r.TryGetProperty("data", out var data) &&
data.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Array)
{
int tried = 0;
foreach (var item in info.EnumerateArray())
{
if (tried >= 3) break;
string song = HttpHelp.Str(item, "songname");
string singer = HttpHelp.Str(item, "singername");
string hash = HttpHelp.Str(item, "hash");
long durSec = HttpHelp.Num(item, "duration");
if (hash.Length == 0) continue;
if (!HttpHelp.TitleMatches(song, title) ||!HttpHelp.ArtistMatches(singer, artist)) continue;
if (!HttpHelp.DurationClose(durSec * 1000, durationMs)) continue;
tried++;
var hit = await DownloadByHashAsync(hash, singer, song,
durSec > 0? durSec * 1000: durationMs, ct);
if (hit!= null) return hit;
}
}

// 路线 B：lyrics.kugou.com/search 直接候选
string? directJson = await HttpHelp.GetStringAsync(
$"https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&keyword={HttpHelp.Enc(kw)}", ct);
var droot = HttpHelp.ParseJson(directJson);
if (droot is JsonElement dr && dr.TryGetProperty("candidates", out var cands) &&
cands.ValueKind == JsonValueKind.Array)
{
int tried = 0;
foreach (var cand in cands.EnumerateArray())
{
if (tried >= 3) break;
string song = HttpHelp.Str(cand, "song");
string singer = HttpHelp.Str(cand, "singer");
if (!HttpHelp.TitleMatches(song, title) ||!HttpHelp.ArtistMatches(singer, artist)) continue;
tried++;
string? lrc = await DownloadKrcAsync(
HttpHelp.Str(cand, "id"), HttpHelp.Str(cand, "accesskey"), ct);
if (lrc!= null) return new ProviderResult { LrcText = lrc};
}
}
}
return null;
}

private async Task<ProviderResult?> DownloadByHashAsync(string hash, string singer, string song,
long durationMs, CancellationToken ct)
{
string kw = $"{singer} - {song}";
string? json = await HttpHelp.GetStringAsync(
$"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword={HttpHelp.Enc(kw)}" +
$"&duration={durationMs}&hash={hash}", ct);
var root = HttpHelp.ParseJson(json);
if (root is JsonElement r && r.TryGetProperty("candidates", out var cands) &&
cands.ValueKind == JsonValueKind.Array)
{
foreach (var cand in cands.EnumerateArray())
{
string? lrc = await DownloadKrcAsync(
HttpHelp.Str(cand, "id"), HttpHelp.Str(cand, "accesskey"), ct);
if (lrc!= null) return new ProviderResult { LrcText = lrc};
break; // 第一个候选就是官方推荐，失败不再逐个试，避免拖慢
}
}
return null;
}

private async Task<string?> DownloadKrcAsync(string id, string accesskey, CancellationToken ct)
{
if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(accesskey)) return null;
string? json = await HttpHelp.GetStringAsync(
$"https://lyrics.kugou.com/download?ver=1&client=pc&fmt=krc&charset=utf8&accesskey={accesskey}&id={id}", ct);
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) return null;
string content = HttpHelp.Str(r, "content");
if (content.Length == 0) return null;
string? krc = KrcDecoder.Decode(content);
if (krc == null) return null;
string enhanced = KrcDecoder.ToEnhancedLrc(krc);
return enhanced.Length > 0? enhanced: null;
}

// ---------- 手动选词（v0.2） ----------

public async Task<CandidateSearchResult> SearchCandidatesAsync(string title, string artist,
CancellationToken ct)
{
var result = new CandidateSearchResult();
var seen = new HashSet<string>();
bool anyResponse = false;
bool Add(string song, string singer, long durMs, string id, string accesskey)
{
if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(accesskey)) return false;
string key = $"{id}|{accesskey}";
if (!seen.Add(key)) return false;
result.Candidates.Add(new LyricCandidate
{
ProviderName = Name, Title = song, Artist = singer,
DurationMs = durMs, WordLevel = true, Key = key
});
return true;
}

var combos = LyricsService.BuildCombos(title, artist);
// 路线 B：lyrics.kugou.com/search 直接给 id/accesskey 候选
foreach (string kw in combos.Take(2))
{
if (result.Candidates.Count >= 10) break;
string? json = await HttpHelp.GetStringAsync(
$"https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&keyword={HttpHelp.Enc(kw)}", ct);
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) continue;
anyResponse = true;
if (r.TryGetProperty("candidates", out var cands) && cands.ValueKind == JsonValueKind.Array)
{
foreach (var cand in cands.EnumerateArray())
{
long durRaw = HttpHelp.Num(cand, "duration");
// duration 单位不稳定：小于 1000 的按秒处理，其余按毫秒
long durMs = durRaw <= 0? 0: durRaw < 1000? durRaw * 1000: durRaw;
Add(HttpHelp.Str(cand, "song"), HttpHelp.Str(cand, "singer"), durMs,
HttpHelp.Str(cand, "id"), HttpHelp.Str(cand, "accesskey"));
}
}
}

// 路线 A 补跑（v0.3 改）：mobilecdn 拿真歌曲 hash → krcs 换 id/accesskey。
// 路线 B 的候选是「歌词文件」索引，混着半截 KRC 与 DJ 版（时长=片段长），
// 打分会被时长差误杀；路线 A 按歌曲哈希核验，给出的是全曲 KRC，必须并进来一起打分。
// 上限：最多补 3 个哈希核验候选，控住耗时（打分阶段总预算 6 秒）。
int addedFromA = 0;
foreach (string kw in combos.Take(2))
{
if (addedFromA >= 3) break;
string? json = await HttpHelp.GetStringAsync(
$"http://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={HttpHelp.Enc(kw)}&page=1&pagesize=8", ct);
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) continue;
anyResponse = true;
if (!r.TryGetProperty("data", out var data) || !data.TryGetProperty("info", out var info) ||
info.ValueKind != JsonValueKind.Array) continue;
int resolved = 0;
foreach (var item in info.EnumerateArray())
{
if (resolved >= 3 || addedFromA >= 3) break;
string hash = HttpHelp.Str(item, "hash");
if (hash.Length == 0) continue;
resolved++;
string song = HttpHelp.Str(item, "songname");
string singer = HttpHelp.Str(item, "singername");
long durSec = HttpHelp.Num(item, "duration");
string kw2 = $"{singer} - {song}";
string? cjson = await HttpHelp.GetStringAsync(
$"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword={HttpHelp.Enc(kw2)}" +
$"&duration={durSec * 1000}&hash={hash}", ct);
var croot = HttpHelp.ParseJson(cjson);
if (croot is JsonElement cr && cr.TryGetProperty("candidates", out var cc) &&
cc.ValueKind == JsonValueKind.Array)
{
foreach (var cand in cc.EnumerateArray())
{
long durRaw = HttpHelp.Num(cand, "duration");
// 与路线 B 同口径：小于 1000 按秒、其余按毫秒；缺失时用歌曲时长兜底
long durMs = durRaw <= 0 ? durSec * 1000 : durRaw < 1000 ? durRaw * 1000 : durRaw;
if (Add(HttpHelp.Str(cand, "song") is { Length: > 0 } s ? s : song,
HttpHelp.Str(cand, "singer") is { Length: > 0 } s2 ? s2 : singer,
durMs, HttpHelp.Str(cand, "id"), HttpHelp.Str(cand, "accesskey")))
addedFromA++;
break;
}
}
}
}
if (!anyResponse) result.Error = "接口无响应（网络问题）";
return result;
}

public async Task<ProviderResult?> FetchCandidateAsync(LyricCandidate candidate, CancellationToken ct)
{
var parts = candidate.Key.Split('|');
if (parts.Length!= 2) return null;
string? lrc = await DownloadKrcAsync(parts[0], parts[1], ct);
return lrc!= null? new ProviderResult { LrcText = lrc}: null;
}
}

// =====================================================================
// 2. QQ 音乐：client_search_cp 搜索 + fcg_query_lyric_new 取词（base64 LRC + trans 译文）
// =====================================================================
public sealed class QqProvider: ILyricsProvider, ICandidateProvider
{
public string Name => "QQ 音乐";

public async Task<ProviderResult?> FetchAsync(string title, string artist, long durationMs,
IReadOnlyList<string> combos, CancellationToken ct)
{
foreach (string kw in combos)
{
string? json = await HttpHelp.GetStringAsync(
$"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p=1&n=10&w={HttpHelp.Enc(kw)}&format=json",
ct, referer: "https://y.qq.com/");
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) continue;
if (!r.TryGetProperty("data", out var data) ||!data.TryGetProperty("song", out var songObj) ||
!songObj.TryGetProperty("list", out var list) || list.ValueKind!= JsonValueKind.Array) continue;

int tried = 0;
foreach (var item in list.EnumerateArray())
{
if (tried >= 3) break;
string name = HttpHelp.Str(item, "songname");
string mid = HttpHelp.Str(item, "songmid");
long interval = HttpHelp.Num(item, "interval");
string singers = "";
if (item.TryGetProperty("singer", out var sg) && sg.ValueKind == JsonValueKind.Array)
singers = string.Join(",", sg.EnumerateArray().Select(s => HttpHelp.Str(s, "name")));
if (mid.Length == 0) continue;
if (!HttpHelp.TitleMatches(name, title) ||!HttpHelp.ArtistMatches(singers, artist)) continue;
if (!HttpHelp.DurationClose(interval * 1000, durationMs)) continue;
tried++;
var hit = await FetchLyricAsync(mid, ct);
if (hit!= null) return hit;
}
}
return null;
}

private async Task<ProviderResult?> FetchLyricAsync(string songmid, CancellationToken ct)
{
string? json = await HttpHelp.GetStringAsync(
$"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={songmid}&format=json&nobase64=0&g_tk=5381",
ct, referer: "https://y.qq.com/portal/player.html");
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) return null;
string lrc = DecodeB64(HttpHelp.Str(r, "lyric"));
if (!lrc.Contains('[')) return null;
string trans = DecodeB64(HttpHelp.Str(r, "trans"));
return new ProviderResult
{
LrcText = lrc,
TransLrcText = trans.Contains('[')? trans: null
};
}

private static string DecodeB64(string b64)
{
if (string.IsNullOrWhiteSpace(b64)) return "";
try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64.Trim()));}
catch { return "";}
}

// ---------- 手动选词（v0.2） ----------

public async Task<CandidateSearchResult> SearchCandidatesAsync(string title, string artist,
CancellationToken ct)
{
var result = new CandidateSearchResult();
var seen = new HashSet<string>();
bool anyResponse = false;
foreach (string kw in LyricsService.BuildCombos(title, artist).Take(2))
{
if (result.Candidates.Count >= 10) break;
string? json = await HttpHelp.GetStringAsync(
$"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p=1&n=10&w={HttpHelp.Enc(kw)}&format=json",
ct, referer: "https://y.qq.com/");
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) continue;
anyResponse = true;
if (!r.TryGetProperty("data", out var data) ||!data.TryGetProperty("song", out var songObj) ||
!songObj.TryGetProperty("list", out var list) || list.ValueKind!= JsonValueKind.Array) continue;
foreach (var item in list.EnumerateArray())
{
string mid = HttpHelp.Str(item, "songmid");
if (mid.Length == 0 ||!seen.Add(mid)) continue;
string singers = "";
if (item.TryGetProperty("singer", out var sg) && sg.ValueKind == JsonValueKind.Array)
singers = string.Join(",", sg.EnumerateArray().Select(s => HttpHelp.Str(s, "name")));
result.Candidates.Add(new LyricCandidate
{
ProviderName = Name, Title = HttpHelp.Str(item, "songname"), Artist = singers,
DurationMs = HttpHelp.Num(item, "interval") * 1000, WordLevel = false, Key = mid
});
}
}
if (!anyResponse) result.Error = "接口无响应（网络问题）";
return result;
}

public Task<ProviderResult?> FetchCandidateAsync(LyricCandidate candidate, CancellationToken ct) =>
FetchLyricAsync(candidate.Key, ct);
}

// =====================================================================
// 3. 网易云：cloudsearch 搜索 + /api/song/lyric（lrc + tlyric）
// =====================================================================
public sealed class NetEaseProvider: ILyricsProvider, ICandidateProvider
{
public string Name => "网易云";

public async Task<ProviderResult?> FetchAsync(string title, string artist, long durationMs,
IReadOnlyList<string> combos, CancellationToken ct)
{
foreach (string kw in combos)
{
string? json = await HttpHelp.GetStringAsync(
$"https://interface.music.163.com/api/cloudsearch/pc?type=1&s={HttpHelp.Enc(kw)}&limit=10",
ct, referer: "https://music.163.com/");
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) continue;
if (!r.TryGetProperty("result", out var result) ||!result.TryGetProperty("songs", out var songs) ||
songs.ValueKind!= JsonValueKind.Array) continue;

int tried = 0;
foreach (var s in songs.EnumerateArray())
{
if (tried >= 3) break;
string name = HttpHelp.Str(s, "name");
long id = HttpHelp.Num(s, "id");
long dt = HttpHelp.Num(s, "dt");
string singers = "";
if (s.TryGetProperty("ar", out var ar) && ar.ValueKind == JsonValueKind.Array)
singers = string.Join(",", ar.EnumerateArray().Select(a => HttpHelp.Str(a, "name")));
if (id == 0) continue;
if (!HttpHelp.TitleMatches(name, title) ||!HttpHelp.ArtistMatches(singers, artist)) continue;
if (!HttpHelp.DurationClose(dt, durationMs)) continue;
tried++;
var hit = await FetchLyricByIdAsync(id, ct);
if (hit!= null) return hit;
}
}
return null;
}

private async Task<ProviderResult?> FetchLyricByIdAsync(long id, CancellationToken ct)
{
string? ljson = await HttpHelp.GetStringAsync(
$"https://music.163.com/api/song/lyric?id={id}&lv=1&kv=1&tv=1",
ct, referer: "https://music.163.com/");
var lroot = HttpHelp.ParseJson(ljson);
if (lroot is not JsonElement lr) return null;
string lrc = lr.TryGetProperty("lrc", out var lo)? HttpHelp.Str(lo, "lyric"): "";
if (!lrc.Contains('[')) return null;
string tlyric = lr.TryGetProperty("tlyric", out var to)? HttpHelp.Str(to, "lyric"): "";
return new ProviderResult
{
LrcText = lrc,
TransLrcText = tlyric.Contains('[')? tlyric: null
};
}

// ---------- 手动选词（v0.2） ----------

public async Task<CandidateSearchResult> SearchCandidatesAsync(string title, string artist,
CancellationToken ct)
{
var result = new CandidateSearchResult();
var seen = new HashSet<long>();
bool anyResponse = false;
foreach (string kw in LyricsService.BuildCombos(title, artist).Take(2))
{
if (result.Candidates.Count >= 10) break;
string? json = await HttpHelp.GetStringAsync(
$"https://interface.music.163.com/api/cloudsearch/pc?type=1&s={HttpHelp.Enc(kw)}&limit=10",
ct, referer: "https://music.163.com/");
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) continue;
anyResponse = true;
if (!r.TryGetProperty("result", out var res) ||!res.TryGetProperty("songs", out var songs) ||
songs.ValueKind!= JsonValueKind.Array) continue;
foreach (var s in songs.EnumerateArray())
{
long id = HttpHelp.Num(s, "id");
if (id == 0 ||!seen.Add(id)) continue;
string singers = "";
if (s.TryGetProperty("ar", out var ar) && ar.ValueKind == JsonValueKind.Array)
singers = string.Join(",", ar.EnumerateArray().Select(a => HttpHelp.Str(a, "name")));
result.Candidates.Add(new LyricCandidate
{
ProviderName = Name, Title = HttpHelp.Str(s, "name"), Artist = singers,
DurationMs = HttpHelp.Num(s, "dt"), WordLevel = false, Key = id.ToString()
});
}
}
if (!anyResponse) result.Error = "接口无响应（网络问题）";
return result;
}

public Task<ProviderResult?> FetchCandidateAsync(LyricCandidate candidate, CancellationToken ct) =>
long.TryParse(candidate.Key, out long id)
? FetchLyricByIdAsync(id, ct)
: Task.FromResult<ProviderResult?>(null);
}

// =====================================================================
// 4. 酷我：search.kuwo.cn/r.s 搜索 + m.kuwo.cn/newh5 歌词（逐行时间）
// =====================================================================
public sealed class KuwoProvider: ILyricsProvider, ICandidateProvider
{
public string Name => "酷我";

private static readonly Regex MetaPrefix = new(
@"^(作词|作曲|编曲|制作人|和声|合声|录音|混音|母带|监制|出品|OP|SP)\s*[:：]");

public async Task<ProviderResult?> FetchAsync(string title, string artist, long durationMs,
IReadOnlyList<string> combos, CancellationToken ct)
{
foreach (string kw in combos)
{
string? raw = await HttpHelp.GetStringAsync(
$"http://search.kuwo.cn/r.s?all={HttpHelp.Enc(kw)}&ft=music&itemset=web_2013&client=kt&pn=0&rn=10&rformat=json&encoding=utf8", ct);
if (raw == null) continue;
// 酷我返回的是单引号伪 JSON，统一换成双引号再解析（业内通行做法）
var root = HttpHelp.ParseJson(raw.Replace('\'', '"'));
if (root is not JsonElement r) continue;
if (!r.TryGetProperty("abslist", out var list) || list.ValueKind!= JsonValueKind.Array) continue;

var candidates = new List<(string rid, string name, string singer, long dur)>();
foreach (var item in list.EnumerateArray())
{
string name = WebUtility.HtmlDecode(HttpHelp.Str(item, "NAME")).Replace("&nbsp;", " ").Trim();
string singer = WebUtility.HtmlDecode(HttpHelp.Str(item, "ARTIST")).Trim();
string rid = HttpHelp.Str(item, "MUSICRID").Replace("MUSIC_", "");
long dur = HttpHelp.Num(item, "DURATION");
if (rid.Length == 0) continue;
if (!HttpHelp.TitleMatches(name, title) ||!HttpHelp.ArtistMatches(singer, artist)) continue;
if (!HttpHelp.DurationClose(dur * 1000, durationMs)) continue;
candidates.Add((rid, name, singer, dur));
}
// 精确同名优先、带「伴奏」的排最后
candidates.Sort((a, b) =>
{
int sa = Score(a.name), sb = Score(b.name);
return sb.CompareTo(sa);
});
int Score(string n) =>
(HttpHelp.Norm(n) == HttpHelp.Norm(title)? 2: 0) + (n.Contains("伴奏")? -3: 0);

int tried = 0;
foreach (var (rid, _, _, _) in candidates)
{
if (tried >= 2) break;
tried++;
var hit = await FetchLyricByRidAsync(rid, ct);
if (hit!= null) return hit;
}
}
return null;
}

private async Task<ProviderResult?> FetchLyricByRidAsync(string rid, CancellationToken ct)
{
string? ljson = await HttpHelp.GetStringAsync(
$"https://m.kuwo.cn/newh5/singles/songinfoandlrc?musicId={rid}", ct);
var lroot = HttpHelp.ParseJson(ljson);
if (lroot is not JsonElement lr) return null;
if (!lr.TryGetProperty("data", out var data) ||!data.TryGetProperty("lrclist", out var lrcList) ||
lrcList.ValueKind!= JsonValueKind.Array) return null;
var sb = new StringBuilder();
foreach (var line in lrcList.EnumerateArray())
{
string text = HttpHelp.Str(line, "lineLyric").Trim();
if (text.Length == 0 || MetaPrefix.IsMatch(text)) continue;
if (!double.TryParse(HttpHelp.Str(line, "time"),
System.Globalization.NumberStyles.Float,
System.Globalization.CultureInfo.InvariantCulture, out double sec)) continue;
sb.Append(LrcParser.StampTag((long)(sec * 1000))).Append(text).Append('\n');
}
return sb.Length > 0? new ProviderResult { LrcText = sb.ToString()}: null;
}

// ---------- 手动选词（v0.2） ----------

public async Task<CandidateSearchResult> SearchCandidatesAsync(string title, string artist,
CancellationToken ct)
{
var result = new CandidateSearchResult();
var seen = new HashSet<string>();
bool anyResponse = false;
foreach (string kw in LyricsService.BuildCombos(title, artist).Take(2))
{
if (result.Candidates.Count >= 10) break;
string? raw = await HttpHelp.GetStringAsync(
$"http://search.kuwo.cn/r.s?all={HttpHelp.Enc(kw)}&ft=music&itemset=web_2013&client=kt&pn=0&rn=10&rformat=json&encoding=utf8", ct);
if (raw == null) continue;
anyResponse = true;
var root = HttpHelp.ParseJson(raw.Replace('\'', '"'));
if (root is not JsonElement r) continue;
if (!r.TryGetProperty("abslist", out var list) || list.ValueKind!= JsonValueKind.Array) continue;
foreach (var item in list.EnumerateArray())
{
string rid = HttpHelp.Str(item, "MUSICRID").Replace("MUSIC_", "");
if (rid.Length == 0 ||!seen.Add(rid)) continue;
string name = WebUtility.HtmlDecode(HttpHelp.Str(item, "NAME")).Replace("&nbsp;", " ").Trim();
string singer = WebUtility.HtmlDecode(HttpHelp.Str(item, "ARTIST")).Trim();
result.Candidates.Add(new LyricCandidate
{
ProviderName = Name, Title = name, Artist = singer,
DurationMs = HttpHelp.Num(item, "DURATION") * 1000, WordLevel = false, Key = rid
});
}
}
if (!anyResponse) result.Error = "接口无响应（网络问题）";
return result;
}

public Task<ProviderResult?> FetchCandidateAsync(LyricCandidate candidate, CancellationToken ct) =>
FetchLyricByRidAsync(candidate.Key, ct);
}

// =====================================================================
// 5. LRCLIB：/api/get（带 duration 精确匹配）失败再 /api/search
// =====================================================================
public sealed class LrclibProvider: ILyricsProvider, ICandidateProvider
{
public string Name => "LRCLIB";
private const string Ua = "SpotifyLyricsWin/0.3 (personal lyrics overlay)";

public async Task<ProviderResult?> FetchAsync(string title, string artist, long durationMs,
IReadOnlyList<string> combos, CancellationToken ct)
{
string url = $"https://lrclib.net/api/get?track_name={HttpHelp.Enc(title)}&artist_name={HttpHelp.Enc(artist)}" +
(durationMs > 0? $"&duration={durationMs / 1000}": "");
string? json = await HttpHelp.GetStringAsync(url, ct, userAgent: Ua);
var root = HttpHelp.ParseJson(json);
if (root is JsonElement r)
{
string synced = HttpHelp.Str(r, "syncedLyrics");
if (synced.Contains('[')) return new ProviderResult { LrcText = synced};
}
string? sjson = await HttpHelp.GetStringAsync(
$"https://lrclib.net/api/search?track_name={HttpHelp.Enc(title)}&artist_name={HttpHelp.Enc(artist)}",
ct, userAgent: Ua);
var sroot = HttpHelp.ParseJson(sjson);
if (sroot is JsonElement sr && sr.ValueKind == JsonValueKind.Array)
{
string? best = null; long bestDiff = long.MaxValue;
foreach (var item in sr.EnumerateArray())
{
string synced = HttpHelp.Str(item, "syncedLyrics");
if (!synced.Contains('[')) continue;
double dur = item.TryGetProperty("duration", out var dv) && dv.ValueKind == JsonValueKind.Number
? dv.GetDouble(): 0;
long diff = durationMs > 0 && dur > 0? Math.Abs((long)(dur * 1000) - durationMs): 0;
if (diff < bestDiff) { bestDiff = diff; best = synced;}
}
if (best!= null && bestDiff <= 8000)
return new ProviderResult { LrcText = best};
}
return null;
}

// ---------- 手动选词（v0.2） ----------

public async Task<CandidateSearchResult> SearchCandidatesAsync(string title, string artist,
CancellationToken ct)
{
var result = new CandidateSearchResult();
var seen = new HashSet<string>();
bool anyResponse = false;
void Add(string t, string a, double durSec, string synced)
{
if (!synced.Contains('[')) return;
string dedupe = $"{HttpHelp.Norm(t)}|{HttpHelp.Norm(a)}|{(long)durSec}";
if (!seen.Add(dedupe)) return;
bool word = Regex.IsMatch(synced, @"<\d{1,3}:\d{1,2}");
result.Candidates.Add(new LyricCandidate
{
ProviderName = Name, Title = t, Artist = a,
DurationMs = (long)(durSec * 1000), WordLevel = word,
Key = dedupe, Payload = synced
});
}
// 先精确 get（带歌手），再 search 列表
string? gj = await HttpHelp.GetStringAsync(
$"https://lrclib.net/api/get?track_name={HttpHelp.Enc(title)}&artist_name={HttpHelp.Enc(artist)}",
ct, userAgent: Ua);
var gr = HttpHelp.ParseJson(gj);
if (gr is JsonElement g)
{
anyResponse = true;
double dur = g.TryGetProperty("duration", out var dv) && dv.ValueKind == JsonValueKind.Number
? dv.GetDouble(): 0;
Add(HttpHelp.Str(g, "trackName") is { Length: > 0 } tn? tn: title,
HttpHelp.Str(g, "artistName") is { Length: > 0 } an? an: artist,
dur, HttpHelp.Str(g, "syncedLyrics"));
}
string? sj = await HttpHelp.GetStringAsync(
$"https://lrclib.net/api/search?track_name={HttpHelp.Enc(title)}&artist_name={HttpHelp.Enc(artist)}",
ct, userAgent: Ua);
var sr = HttpHelp.ParseJson(sj);
if (sr is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
{
anyResponse = true;
foreach (var item in arr.EnumerateArray())
{
if (result.Candidates.Count >= 12) break;
double dur = item.TryGetProperty("duration", out var dv) && dv.ValueKind == JsonValueKind.Number
? dv.GetDouble(): 0;
Add(HttpHelp.Str(item, "trackName"), HttpHelp.Str(item, "artistName"),
dur, HttpHelp.Str(item, "syncedLyrics"));
}
}
if (!anyResponse) result.Error = "接口无响应（网络问题）";
return result;
}

public Task<ProviderResult?> FetchCandidateAsync(LyricCandidate candidate, CancellationToken ct) =>
Task.FromResult(candidate.Payload is { Length: > 0 } p
? new ProviderResult { LrcText = p}: null);
}

// =====================================================================
// 6. lyrics.ovh：纯文本，按时长均分兜底
// =====================================================================
public sealed class LyricsOvhProvider: ILyricsProvider
{
public string Name => "lyrics.ovh";

public async Task<ProviderResult?> FetchAsync(string title, string artist, long durationMs,
IReadOnlyList<string> combos, CancellationToken ct)
{
if (string.IsNullOrWhiteSpace(artist)) return null;
string? json = await HttpHelp.GetStringAsync(
$"https://api.lyrics.ovh/v1/{HttpHelp.Enc(artist)}/{HttpHelp.Enc(title)}", ct);
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) return null;
string plain = HttpHelp.Str(r, "lyrics");
if (string.IsNullOrWhiteSpace(plain)) return null;
// 纯文本 → 按时长均匀铺成 LRC（LrcParser.fromPlain 的等价序列化）
var lyrics = LrcParser.FromPlain(plain, durationMs, title, artist);
if (lyrics.Lines.Count == 0) return null;
var sb = new StringBuilder();
foreach (var line in lyrics.Lines)
sb.Append(LrcParser.StampTag(line.TimeMs)).Append(line.Text).Append('\n');
return new ProviderResult { LrcText = sb.ToString()};
}
}

// =====================================================================
// 7. Musixmatch：桌面端免 key 通道（token.get + macro.subtitles.get）尽力实现；
// 或设置里的 API Key 走官方接口。失败静默跳过，绝不拖死链路。
// 注意：本机实测免 key 通道返回乱序假词（未授权 token 的反爬结果），仅作最后兜底。
// =====================================================================
public sealed class MusixmatchProvider: ILyricsProvider
{
public string Name => "Musixmatch";
private readonly Func<string> _apiKey;
private string? _userToken;

public MusixmatchProvider(Func<string> apiKey) => _apiKey = apiKey;

public async Task<ProviderResult?> FetchAsync(string title, string artist, long durationMs,
IReadOnlyList<string> combos, CancellationToken ct)
{
try
{
string key = _apiKey()?.Trim()?? "";
if (key.Length > 0) return await FetchWithKeyAsync(key, title, artist, ct);
return await FetchDesktopAsync(title, artist, durationMs, ct);
}
catch { return null;}
}

private async Task<ProviderResult?> FetchDesktopAsync(string title, string artist,
long durationMs, CancellationToken ct)
{
if (_userToken == null)
{
string? tjson = await HttpHelp.GetStringAsync(
"https://apic-desktop.musixmatch.com/ws/1.1/token.get?app_id=web-desktop-app-v1.0", ct);
var troot = HttpHelp.ParseJson(tjson);
if (troot is JsonElement tr && tr.TryGetProperty("message", out var tm) &&
tm.TryGetProperty("body", out var tb))
_userToken = HttpHelp.Str(tb, "user_token");
if (string.IsNullOrEmpty(_userToken)) return null;
}
string url = "https://apic-desktop.musixmatch.com/ws/1.1/macro.subtitles.get?app_id=web-desktop-app-v1.0" +
$"&usertoken={_userToken}&q_track={HttpHelp.Enc(title)}&q_artist={HttpHelp.Enc(artist)}&format=json" +
(durationMs > 0? $"&q_duration={durationMs / 1000}": "");
string? json = await HttpHelp.GetStringAsync(url, ct);
var root = HttpHelp.ParseJson(json);
if (root is not JsonElement r) return null;
// message.body.macro_calls["track.subtitles.get"].message.body.subtitle_list[0].subtitle.subtitle_body
if (!r.TryGetProperty("message", out var msg) ||!msg.TryGetProperty("body", out var body) ||
!body.TryGetProperty("macro_calls", out var calls)) return null;
foreach (var prop in calls.EnumerateObject())
{
if (!prop.Name.Contains("subtitles")) continue;
var b = prop.Value;
if (b.TryGetProperty("message", out var m2) && m2.TryGetProperty("body", out var b2) &&
b2.TryGetProperty("subtitle_list", out var sl) && sl.ValueKind == JsonValueKind.Array)
{
foreach (var item in sl.EnumerateArray())
{
string sub = item.TryGetProperty("subtitle", out var so)? HttpHelp.Str(so, "subtitle_body"): "";
if (sub.Contains('[')) return new ProviderResult { LrcText = sub};
}
}
}
return null;
}

private async Task<ProviderResult?> FetchWithKeyAsync(string key, string title, string artist,
CancellationToken ct)
{
string? tjson = await HttpHelp.GetStringAsync(
$"https://api.musixmatch.com/ws/1.1/matcher.track.get?q_track={HttpHelp.Enc(title)}&q_artist={HttpHelp.Enc(artist)}&apikey={key}", ct);
var troot = HttpHelp.ParseJson(tjson);
if (troot is not JsonElement tr) return null;
long trackId = 0;
if (tr.TryGetProperty("message", out var tm) && tm.TryGetProperty("body", out var tb) &&
tb.TryGetProperty("track", out var tk))
trackId = HttpHelp.Num(tk, "track_id");
if (trackId == 0) return null;
string? sjson = await HttpHelp.GetStringAsync(
$"https://api.musixmatch.com/ws/1.1/track.subtitles.get?track_id={trackId}&subtitle_format=lrc&apikey={key}", ct);
var sroot = HttpHelp.ParseJson(sjson);
if (sroot is not JsonElement sr) return null;
if (sr.TryGetProperty("message", out var sm) && sm.TryGetProperty("body", out var sb2) &&
sb2.TryGetProperty("subtitle_list", out var sl) && sl.ValueKind == JsonValueKind.Array)
{
foreach (var item in sl.EnumerateArray())
{
string sub = item.TryGetProperty("subtitle", out var so)? HttpHelp.Str(so, "subtitle_body"): "";
if (sub.Contains('[')) return new ProviderResult { LrcText = sub};
}
}
return null;
}
}
