using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpotifyLyricsWin.Core;

/// <summary>GitHub Release 的一个资产（只留更新流程用到的字段）。</summary>
public sealed class ReleaseAsset
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

/// <summary>GitHub Release（releases/latest 的返回）。</summary>
public sealed class ReleaseInfo
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("published_at")] public string? PublishedAt { get; set; }
    [JsonPropertyName("assets")] public List<ReleaseAsset> Assets { get; set; } = new();

    /// <summary>更新 zip 资产：优先按约定名 SpotifyLyricsWin-v*.zip 找，否则取第一个 .zip。</summary>
    public ReleaseAsset? ZipAsset =>
        Assets.FirstOrDefault(a => a.Name.StartsWith("SpotifyLyricsWin-v", StringComparison.OrdinalIgnoreCase)
                                   && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        ?? Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    /// <summary>tag 去掉前缀 v 后的版本号，如 v0.4.0 → 0.4.0。</summary>
    public string Version => UpdateService.NormalizeVersion(TagName);
}

public enum UpdateCheckKind { NoToken, UpToDate, UpdateAvailable, Failed }

public sealed record UpdateCheckResult(UpdateCheckKind Kind, ReleaseInfo? Release, string Message);

/// <summary>Updater 写回的结果（%AppData%/SpotifyLyricsWin/update-result.json）。</summary>
public sealed class UpdateResult
{
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("time")] public string Time { get; set; } = "";
}

/// <summary>
/// v0.4 自动更新：查 GitHub 私有仓 Release（只读令牌）→ 下载 zip 资产 → 交给独立 Updater 换版。
/// 仓库保持私有，令牌只存本机、只在请求 GitHub API 时带上。
/// 本类只用 BCL（无 WPF 依赖），纯逻辑部分（版本比较/JSON 解析）可无头测试。
/// </summary>
public static class UpdateService
{
    public const string Repo = "a1440930984/spotify-lyrics-windows";
    public const string LatestApi = $"https://api.github.com/repos/{Repo}/releases/latest";
    public static string AssetApi(long assetId) => $"https://api.github.com/repos/{Repo}/releases/assets/{assetId}";

    /// <summary>当前程序版本（取程序集 Version，csproj 里维护，如 0.4.0）。</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string NormalizeVersion(string? tagOrVersion)
    {
        string s = (tagOrVersion ?? "").Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
        int dash = s.IndexOf('-');
        if (dash >= 0) s = s.Substring(0, dash); // 去掉 -beta 之类后缀再比
        return s;
    }

    /// <summary>比较两个版本号（可带 v 前缀）：a&gt;b 返回正数，a&lt;b 返回负数，相等 0。</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = ParseParts(NormalizeVersion(a));
        var pb = ParseParts(NormalizeVersion(b));
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] ParseParts(string v) =>
        v.Split('.', StringSplitOptions.RemoveEmptyEntries)
         .Select(p => int.TryParse(p, out int n) ? n : 0)
         .ToArray();

    public static bool IsUpdateAvailable(string latestTag) =>
        CompareVersions(latestTag, CurrentVersion) > 0;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>解析 releases/latest 的 JSON（抽出来便于无头测试）。</summary>
    public static ReleaseInfo ParseRelease(string json) =>
        JsonSerializer.Deserialize<ReleaseInfo>(json, JsonOpts) ?? new ReleaseInfo();

    // ---------- 路径 ----------

    public static string UpdateWorkDir => Path.Combine(Path.GetTempPath(), "SpotifyLyricsWin-update");
    public static string StagingDir => Path.Combine(UpdateWorkDir, "staging");
    public static string DownloadedZipPath => Path.Combine(UpdateWorkDir, "update.zip");
    public static string StagedUpdaterPath => Path.Combine(UpdateWorkDir, "SpotifyLyricsUpdater.exe");
    public static string UpdaterLogPath => Path.Combine(UpdateWorkDir, "updater.log");
    public static string ResultPath => Path.Combine(Settings.AppDataDir, "update-result.json");

    // ---------- 网络 ----------

    private static HttpRequestMessage NewRequest(string url, string token, string accept)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.UserAgent.ParseAdd("SpotifyLyricsWin");
        req.Headers.Accept.ParseAdd(accept);
        return req;
    }

    /// <summary>查最新 Release。无令牌返回 NoToken；任何失败返回 Failed + 人话原因。</summary>
    public static async Task<UpdateCheckResult> CheckLatestAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new UpdateCheckResult(UpdateCheckKind.NoToken, null, "未填 GitHub 只读令牌，无法检查更新。");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var req = NewRequest(LatestApi, token.Trim(), "application/vnd.github+json");
            using var resp = await http.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                resp.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new UpdateCheckResult(UpdateCheckKind.Failed, null,
                    $"GitHub 拒绝了请求（{(int)resp.StatusCode}）。多半是令牌不对、已过期，或没勾这个仓库的只读权限。");
            if (!resp.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateCheckKind.Failed, null,
                    $"检查更新失败：GitHub 返回 {(int)resp.StatusCode}，请稍后重试。");
            var release = ParseRelease(body);
            if (release.ZipAsset == null)
                return new UpdateCheckResult(UpdateCheckKind.Failed, release,
                    $"最新 Release（{release.TagName}）里没有可下载的 zip 资产。");
            return IsUpdateAvailable(release.TagName)
                ? new UpdateCheckResult(UpdateCheckKind.UpdateAvailable, release,
                    $"发现新版 {release.TagName}（当前 v{CurrentVersion}）")
                : new UpdateCheckResult(UpdateCheckKind.UpToDate, release,
                    $"已是最新（当前 v{CurrentVersion}，最新 {release.TagName}）");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckKind.Failed, null, $"检查更新失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 下载 Release 的 zip 资产。私有仓必须走资产 API + Accept: application/octet-stream 带令牌
    ///（浏览器直链对私有仓无效）。下载后核对字节数与资产 size，不一致视为失败。
    /// </summary>
    public static async Task DownloadAssetAsync(string token, ReleaseAsset asset, string destPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var req = NewRequest(AssetApi(asset.Id), token.Trim(), "application/octet-stream");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"下载失败：GitHub 返回 {(int)resp.StatusCode}，请稍后重试。");
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var net = await resp.Content.ReadAsStreamAsync(ct))
        {
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await net.ReadAsync(buf, ct)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (asset.Size > 0) progress?.Report(done * 100.0 / asset.Size);
            }
        }
        long actual = new FileInfo(destPath).Length;
        if (asset.Size > 0 && actual != asset.Size)
            throw new InvalidOperationException($"下载不完整：应为 {asset.Size} 字节，实际 {actual} 字节，请重试。");
    }

    /// <summary>把下载好的 zip 解压到 staging（覆盖旧 staging）。</summary>
    public static void ExtractToStaging(string zipPath)
    {
        if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, recursive: true);
        Directory.CreateDirectory(StagingDir);
        ZipFile.ExtractToDirectory(zipPath, StagingDir, overwriteFiles: true);
    }

    /// <summary>把 Updater 从主程序目录复制到更新工作目录（脱钩启动必须从临时目录跑，不能占着安装目录）。</summary>
    public static string StageUpdater(string appDir)
    {
        string src = Path.Combine(appDir, "SpotifyLyricsUpdater.exe");
        if (!File.Exists(src))
            throw new FileNotFoundException("安装目录里找不到更新助手 SpotifyLyricsUpdater.exe，无法自动更新。", src);
        Directory.CreateDirectory(UpdateWorkDir);
        string dst = StagedUpdaterPath;
        File.Copy(src, dst, overwrite: true);
        // Updater 若是框架依赖版，还需要它的 dll/runtimeconfig/deps 一起过去
        foreach (string extra in new[] { "SpotifyLyricsUpdater.dll", "SpotifyLyricsUpdater.runtimeconfig.json", "SpotifyLyricsUpdater.deps.json" })
        {
            string p = Path.Combine(appDir, extra);
            if (File.Exists(p)) File.Copy(p, Path.Combine(UpdateWorkDir, extra), overwrite: true);
        }
        return dst;
    }

    // ---------- 更新结果（Updater 写、主程序下次启动读） ----------

    public static UpdateResult? ReadResult()
    {
        try
        {
            if (File.Exists(ResultPath))
                return JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(ResultPath), JsonOpts);
        }
        catch { /* 读不到就不报 */ }
        return null;
    }

    public static void DeleteResult()
    {
        try { if (File.Exists(ResultPath)) File.Delete(ResultPath); } catch { }
    }
}
