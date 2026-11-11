using System.Text;
using System.Text.Json;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Lyrics;

/// <summary>
/// 机器翻译（v0.7 新增）：歌词源无自带译文、网易云补译也没有时，把原文逐行机翻成
/// 简体中文当兜底译文（由设置开关 AutoTranslate 控制，默认开）。
///
/// 接口（2026-11-11 在开发机实测，日文/韩文各测通）：
/// - 主：Google gtx 免费接口 translate.googleapis.com/translate_a/t?client=gtx，
///   sl=auto 自动检测、tl=zh-CN；注意该接口对部分网络出口会限流（实测同机连发被 429），
///   所以必须留兜底、且请求要克制（整首合并成少数几次、请求间留间隔）；
/// - 兜底：MyMemory api.mymemory.translated.net/get，langpair=autodetect|zh-CN，
///   实测日/韩都通、多行 \n 结构能保住；免费匿名额度小，只作兜底。
/// 批量策略：一首歌按行拼成 ≤700 字的若干批（一批一次请求），译文按 \n 拆回逐行；
/// 行数对不齐时该批改逐行单翻；某批彻底失败只留空行（不整首放弃），全批皆空才返回 null。
/// 机翻没有时间戳——调用方按原文行序/行时间贴回，这正是「按行序对齐」的数据基础。
/// </summary>
public static class MachineTranslate
{
    private const int ChunkCharLimit = 700;
    private const int ChunkRequestGapMs = 300;

    /// <summary>
    /// 是否需要走机翻（纯逻辑，供无头测试）：歌词没有可用译文、且不是中文歌
    /// （中文歌判定与译文跳过同口径：汉字 &gt;45% 且无假名——原文已是中文就不翻）。
    /// </summary>
    public static bool NeedsTranslation(Core.Lyrics lyrics)
    {
        if (LyricsService.HasUsableTranslation(lyrics)) return false;
        string joined = string.Join(" ", lyrics.Lines.Select(l => l.Text));
        return !TextClassify.IsChineseSong(joined);
    }

    /// <summary>
    /// 逐行机翻。输入原文行（与歌词行同序），输出同长列表（翻不出的行是空串）；
    /// 一行都没翻出来返回 null。空行原样留空、不发请求。
    /// </summary>
    public static async Task<List<string>?> TranslateLinesAsync(
        IReadOnlyList<string> lines, CancellationToken ct)
    {
        var result = new string[lines.Count];
        Array.Fill(result, "");
        var todo = new List<int>();
        for (int i = 0; i < lines.Count; i++)
            if (!string.IsNullOrWhiteSpace(lines[i])) todo.Add(i);
        if (todo.Count == 0) return null;

        // 分批：连续行拼到接近上限就断批，批内用 \n 连接
        var chunks = new List<List<int>>();
        var cur = new List<int>();
        int curLen = 0;
        foreach (int idx in todo)
        {
            int add = lines[idx].Length + 1;
            if (cur.Count > 0 && curLen + add > ChunkCharLimit)
            {
                chunks.Add(cur);
                cur = new List<int>();
                curLen = 0;
            }
            cur.Add(idx);
            curLen += add;
        }
        if (cur.Count > 0) chunks.Add(cur);

        bool any = false;
        bool firstRequest = true;
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            if (!firstRequest) await Task.Delay(ChunkRequestGapMs, ct);
            firstRequest = false;

            string joined = string.Join("\n", chunk.Select(i => lines[i]));
            string? translated = await TranslateChunkAsync(joined, ct);
            List<string>? parts = translated == null ? null
                : translated.Replace("\r\n", "\n").Split('\n').Select(s => s.Trim()).ToList();
            if (parts != null && parts.Count == chunk.Count)
            {
                for (int k = 0; k < chunk.Count; k++)
                {
                    result[chunk[k]] = parts[k];
                    if (parts[k].Length > 0) any = true;
                }
                continue;
            }
            // 行数对不齐（接口把换行吃掉/合并了）：该批改逐行单翻，慢但准
            AppLog.WriteLimited("mt-realign",
                $"[机翻] 一批 {chunk.Count} 行译文行数对不齐（{(parts == null ? "无结果" : parts.Count.ToString())}），改逐行翻译", 30_000);
            foreach (int idx in chunk)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(150, ct);
                string? one = await TranslateChunkAsync(lines[idx], ct);
                if (!string.IsNullOrWhiteSpace(one))
                {
                    result[idx] = one.Trim();
                    any = true;
                }
            }
        }
        if (!any) return null;
        return result.ToList();
    }

    /// <summary>翻一段文本：先 Google gtx，失败/为空再 MyMemory；都失败返回 null。</summary>
    private static async Task<string?> TranslateChunkAsync(string text, CancellationToken ct)
    {
        string? viaGoogle = await TryGoogleGtxAsync(text, ct);
        if (!string.IsNullOrWhiteSpace(viaGoogle)) return viaGoogle;
        string? viaMm = await TryMyMemoryAsync(text, ct);
        if (!string.IsNullOrWhiteSpace(viaMm)) return viaMm;
        return null;
    }

    /// <summary>Google gtx：响应是 [[译文片段, 源语言], ...] 形式，片段拼起来即全文。</summary>
    public static async Task<string?> TryGoogleGtxAsync(string text, CancellationToken ct)
    {
        string url = "https://translate.googleapis.com/translate_a/t?client=gtx&sl=auto&tl=zh-CN&dt=t&q="
            + HttpHelp.Enc(text);
        string? resp = await HttpHelp.GetStringAsync(url, ct, timeoutSec: 10);
        if (string.IsNullOrWhiteSpace(resp)) return null;
        return ParseGoogleGtx(resp);
    }

    /// <summary>解析 gtx 响应（纯函数，供无头测试）：拼接所有片段的译文。</summary>
    public static string? ParseGoogleGtx(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return null;
            var sb = new StringBuilder();
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0
                    && item[0].ValueKind == JsonValueKind.String)
                    sb.Append(item[0].GetString());
            }
            string s = sb.ToString();
            return s.Length > 0 ? s : null;
        }
        catch { return null; }
    }

    /// <summary>MyMemory：responseData.translatedText，responseStatus 须为 200。</summary>
    public static async Task<string?> TryMyMemoryAsync(string text, CancellationToken ct)
    {
        string url = "https://api.mymemory.translated.net/get?q=" + HttpHelp.Enc(text)
            + "&langpair=autodetect|zh-CN";
        string? resp = await HttpHelp.GetStringAsync(url, ct, timeoutSec: 10);
        if (string.IsNullOrWhiteSpace(resp)) return null;
        return ParseMyMemory(resp);
    }

    /// <summary>解析 MyMemory 响应（纯函数，供无头测试）。</summary>
    public static string? ParseMyMemory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string status = root.TryGetProperty("responseStatus", out var st)
                ? (st.ValueKind == JsonValueKind.String ? st.GetString() ?? "" : st.GetRawText())
                : "";
            if (status != "200") return null;
            if (!root.TryGetProperty("responseData", out var data)) return null;
            if (!data.TryGetProperty("translatedText", out var tt)
                || tt.ValueKind != JsonValueKind.String) return null;
            string s = tt.GetString() ?? "";
            // 额度/参数错误时 MyMemory 有时把错误文案塞进 translatedText 且状态仍是 200 附近，防一手
            if (s.Contains("QUERY LENGTH LIMIT") || s.Contains("INVALID") && s.Length < 40) return null;
            return s.Length > 0 ? s : null;
        }
        catch { return null; }
    }
}
