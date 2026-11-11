using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpotifyLyricsWin.Core;

namespace SpotifyLyricsWin.Lyrics;

/// <summary>翻译服务 id（v0.8，与 Settings.TranslateService 的取值一一对应）。</summary>
public static class TranslateServiceIds
{
    public const string Auto = "auto";
    public const string MyMemory = "mymemory";
    public const string Google = "google";
    public const string Baidu = "baidu";
    public const string Youdao = "youdao";
    public const string Microsoft = "microsoft";

    public static bool IsValid(string? id) =>
        id is Auto or MyMemory or Google or Baidu or Youdao or Microsoft;
}

/// <summary>机翻运行配置（从 Settings 快照出来，免得 MachineTranslate 直接依赖设置对象）。</summary>
public sealed class TranslateConfig
{
    public string Service { get; init; } = TranslateServiceIds.Auto;
    public string MyMemoryEmail { get; init; } = "";
    public string BaiduAppId { get; init; } = "";
    public string BaiduKey { get; init; } = "";
    public string YoudaoAppId { get; init; } = "";
    public string YoudaoKey { get; init; } = "";
    public string MsKey { get; init; } = "";
    public string MsRegion { get; init; } = "";

    public static TranslateConfig FromSettings(Settings s) => new()
    {
        Service = TranslateServiceIds.IsValid(s.TranslateService)
            ? s.TranslateService : TranslateServiceIds.Auto,
        MyMemoryEmail = s.MyMemoryEmail?.Trim() ?? "",
        BaiduAppId = s.BaiduAppId?.Trim() ?? "",
        BaiduKey = s.BaiduKey?.Trim() ?? "",
        YoudaoAppId = s.YoudaoAppId?.Trim() ?? "",
        YoudaoKey = s.YoudaoKey?.Trim() ?? "",
        MsKey = s.MsTranslatorKey?.Trim() ?? "",
        MsRegion = s.MsTranslatorRegion?.Trim() ?? ""
    };

    public static readonly TranslateConfig Default = new();
}

/// <summary>
/// 翻译路由（v0.8，纯逻辑供无头测试）：用户选了单个服务就优先它；凭据不齐的带 key 服务
/// 视为不可用、直接按自动链走（并由调用方记日志）。免 key 的 MyMemory/Google 恒可用。
/// 自动链本身 = Google gtx → MyMemory（v0.7 现行顺序，不变）。
/// </summary>
public static class TranslateRouter
{
    /// <summary>返回首选服务的 id；返回 Auto 表示直接走自动链。</summary>
    public static string PickPrimary(TranslateConfig cfg)
    {
        switch (cfg.Service)
        {
            case TranslateServiceIds.MyMemory:
            case TranslateServiceIds.Google:
                return cfg.Service;
            case TranslateServiceIds.Baidu:
                return cfg.BaiduAppId.Length > 0 && cfg.BaiduKey.Length > 0
                    ? TranslateServiceIds.Baidu : TranslateServiceIds.Auto;
            case TranslateServiceIds.Youdao:
                return cfg.YoudaoAppId.Length > 0 && cfg.YoudaoKey.Length > 0
                    ? TranslateServiceIds.Youdao : TranslateServiceIds.Auto;
            case TranslateServiceIds.Microsoft:
                return cfg.MsKey.Length > 0
                    ? TranslateServiceIds.Microsoft : TranslateServiceIds.Auto;
            default:
                return TranslateServiceIds.Auto;
        }
    }

    /// <summary>带 key 服务被选中但凭据不齐（调用方据此写一条「已回落自动链」日志）。</summary>
    public static bool SelectedButUnusable(TranslateConfig cfg) =>
        cfg.Service is TranslateServiceIds.Baidu or TranslateServiceIds.Youdao
            or TranslateServiceIds.Microsoft
        && PickPrimary(cfg) == TranslateServiceIds.Auto;
}

/// <summary>
/// MyMemory 请求构造（v0.8 独立出来供测试）：带 de 邮箱参数时日额度 5000→50000 字符
/// （官方 usage limits 页口径），留空则匿名。邮箱只随翻译请求发给 MyMemory，不做他用。
/// </summary>
public static class MyMemoryApi
{
    public static string BuildUrl(string text, string? email)
    {
        string url = "https://api.mymemory.translated.net/get?q=" + HttpHelp.Enc(text)
            + "&langpair=autodetect|zh-CN";
        if (!string.IsNullOrWhiteSpace(email)) url += "&de=" + HttpHelp.Enc(email.Trim());
        return url;
    }
}

/// <summary>
/// 百度翻译开放平台·通用翻译 API（v0.8，用户自备 APP ID + 密钥）：
/// GET https://fanyi-api.baidu.com/api/trans/vip/translate，
/// sign = MD5(appid + q + salt + 密钥） 的 32 位小写（q 为 UTF-8 原文、不先编码）。
/// 官方文档示例向量：appid=2015063000000001、q=apple、salt=1435660288、密钥=12345678
/// → sign=f89f9594663708c1605f3d736d01d2d4（无头测试断言的就是它）。
/// q 支持 \n 多行：响应 trans_result 按行拆成多条，dst 以 \n 拼回即可还原行序。
/// </summary>
public static class BaiduTranslateApi
{
    public static string BuildSign(string appid, string q, string salt, string key)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(appid + q + salt + key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string BuildUrl(string appid, string key, string q, string salt)
    {
        string sign = BuildSign(appid, q, salt, key);
        return "https://fanyi-api.baidu.com/api/trans/vip/translate?q=" + HttpHelp.Enc(q)
            + "&from=auto&to=zh&appid=" + HttpHelp.Enc(appid)
            + "&salt=" + HttpHelp.Enc(salt) + "&sign=" + sign;
    }

    /// <summary>解析响应（纯函数）：有 error_code 或无 trans_result → null；否则 dst 逐条以 \n 连接。</summary>
    public static string? ParseResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error_code", out _)) return null;
            if (!root.TryGetProperty("trans_result", out var arr)
                || arr.ValueKind != JsonValueKind.Array) return null;
            var parts = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("dst", out var dst) && dst.ValueKind == JsonValueKind.String)
                    parts.Add(dst.GetString() ?? "");
            }
            if (parts.Count == 0) return null;
            string s = string.Join("\n", parts);
            return s.Length > 0 ? s : null;
        }
        catch { return null; }
    }

    public static async Task<string?> TranslateAsync(
        string appid, string key, string text, CancellationToken ct)
    {
        if (appid.Length == 0 || key.Length == 0) return null;
        string salt = Random.Shared.Next(100000, 999999999).ToString();
        string url = BuildUrl(appid, key, text, salt);
        string? resp = await HttpHelp.GetStringAsync(url, ct, timeoutSec: 10);
        return ParseResponse(resp);
    }
}

/// <summary>
/// 有道智云·文本翻译 API v3（v0.8，用户自备应用 ID + 应用密钥）：
/// POST https://openapi.youdao.com/api（form），signType=v3，
/// sign = SHA256（应用ID + input + salt + curtime + 应用密钥） 小写 hex；
/// input 截断（官方规则）：q 长度 ≤20 用原文，&gt;20 用 前10字 + 长度 + 后10字（长度按字符数）。
/// 官方文档未给带期望哈希的完整示例向量——无头测试用可复算的自洽向量
/// （固定 appKey/salt/curtime/q，期望值由独立 sha256 计算得出）断言拼接顺序与截断规则。
/// 响应：errorCode="0" 时 translation 数组为译文（按段对应 q 的换行段）。
/// </summary>
public static class YoudaoTranslateApi
{
    /// <summary>官方 input 截断规则。</summary>
    public static string TruncateInput(string q)
    {
        if (q.Length <= 20) return q;
        return q.Substring(0, 10) + q.Length + q.Substring(q.Length - 10);
    }

    public static string BuildSign(string appKey, string q, string salt, string curtime, string secret)
    {
        string input = TruncateInput(q);
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(appKey + input + salt + curtime + secret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>POST 表单字段（纯构造，供测试断言字段齐全与签名一致）。</summary>
    public static Dictionary<string, string> BuildForm(
        string appKey, string secret, string q, string salt, string curtime) => new()
        {
            ["q"] = q,
            ["from"] = "auto",
            ["to"] = "zh-CHS",
            ["appKey"] = appKey,
            ["salt"] = salt,
            ["sign"] = BuildSign(appKey, q, salt, curtime, secret),
            ["signType"] = "v3",
            ["curtime"] = curtime
        };

    /// <summary>解析响应（纯函数）：errorCode 非 "0" → null；translation 多段时以 \n 还原行序。</summary>
    public static string? ParseResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("errorCode", out var ec)
                || ec.ValueKind != JsonValueKind.String || ec.GetString() != "0") return null;
            if (!root.TryGetProperty("translation", out var arr)
                || arr.ValueKind != JsonValueKind.Array) return null;
            var parts = new List<string>();
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) parts.Add(item.GetString() ?? "");
            if (parts.Count == 0) return null;
            string s = parts.Count == 1 ? parts[0] : string.Join("\n", parts);
            return s.Length > 0 ? s : null;
        }
        catch { return null; }
    }

    public static async Task<string?> TranslateAsync(
        string appKey, string secret, string text, CancellationToken ct)
    {
        if (appKey.Length == 0 || secret.Length == 0) return null;
        string salt = Guid.NewGuid().ToString("N");
        string curtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var form = BuildForm(appKey, secret, text, salt, curtime);
        string? resp = await HttpHelp.PostFormAsync("https://openapi.youdao.com/api", form, ct, timeoutSec: 10);
        return ParseResponse(resp);
    }
}

/// <summary>
/// 微软 Azure 翻译（v0.8，用户自备密钥 + 区域）：POST
/// https://{区域}.api.cognitive.microsofttranslator.com/translate?api-version=3.0&amp;to=zh-Hans
/// （区域留空走全球端点 api.cognitive.microsofttranslator.com），
/// 头 Ocp-Apim-Subscription-Key（+ 区域端点时 Ocp-Apim-Subscription-Region），
/// 体 [{"Text":"…"}]，源语言自动检测。响应 [{"translations":[{"text":"…"}]}]；
/// 失败响应形如 {"error":{"code":…,"message":…}} → 解析为 null。
/// </summary>
public static class MicrosoftTranslateApi
{
    /// <summary>请求三件套（纯构造，供无头测试断言 URL/头/体）。</summary>
    public static (string Url, Dictionary<string, string> Headers, string Body) BuildRequest(
        string key, string region, string text)
    {
        string host = string.IsNullOrWhiteSpace(region)
            ? "api.cognitive.microsofttranslator.com"
            : $"{region.Trim()}.api.cognitive.microsofttranslator.com";
        string url = $"https://{host}/translate?api-version=3.0&to=zh-Hans";
        var headers = new Dictionary<string, string>
        {
            ["Ocp-Apim-Subscription-Key"] = key
        };
        if (!string.IsNullOrWhiteSpace(region))
            headers["Ocp-Apim-Subscription-Region"] = region.Trim();
        string body = JsonSerializer.Serialize(new[] { new Dictionary<string, string> { ["Text"] = text } });
        return (url, headers, body);
    }

    /// <summary>解析响应（纯函数）：取首个元素的首个译文；error/结构不符 → null。</summary>
    public static string? ParseResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
            var first = root[0];
            if (!first.TryGetProperty("translations", out var arr)
                || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return null;
            var t0 = arr[0];
            if (!t0.TryGetProperty("text", out var tx) || tx.ValueKind != JsonValueKind.String)
                return null;
            string s = tx.GetString() ?? "";
            return s.Length > 0 ? s : null;
        }
        catch { return null; }
    }

    public static async Task<string?> TranslateAsync(
        string key, string region, string text, CancellationToken ct)
    {
        if (key.Length == 0) return null;
        var (url, headers, body) = BuildRequest(key, region, text);
        string? resp = await HttpHelp.PostJsonAsync(url, body, headers, ct, timeoutSec: 10);
        return ParseResponse(resp);
    }
}
