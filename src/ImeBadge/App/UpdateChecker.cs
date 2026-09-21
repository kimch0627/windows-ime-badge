using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>exe 에 새겨진 버전. CI 가 태그(v1.2.3)에서 -p:Version 으로 넣고, 로컬 빌드는 0.0.0-dev.</summary>
static class AppVersion
{
    public static string Display => VersionInfo.Display(Application.ProductVersion);
    public static bool IsDevBuild => Display.StartsWith("0.0.0", StringComparison.Ordinal);
}

sealed record UpdateInfo(string Tag, string Url);

/// <summary>
/// GitHub Releases 의 "latest"(정식 릴리스 중 최신) 를 조회해 새 버전이 있는지 본다.
/// 롤링 사전 릴리스(latest / dev-*)는 prerelease 라 API 가 돌려주지 않는다.
/// </summary>
static class UpdateChecker
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    static readonly Lazy<HttpClient> Http = new(() =>
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.ProductName}/{AppVersion.Display}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    });

    public static bool IsDue(Settings s) =>
        s.CheckForUpdates && !AppVersion.IsDevBuild &&
        (s.LastUpdateCheckUtc is null || DateTime.UtcNow - s.LastUpdateCheckUtc.Value >= Interval);

    /// <summary>최신 정식 릴리스. 릴리스가 없거나(404) 응답이 이상하면 null.</summary>
    public static async Task<UpdateInfo?> FetchLatestAsync(CancellationToken ct)
    {
        using var resp = await Http.Value.GetAsync(AppInfo.LatestReleaseApi, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) { Log.Write($"update check: HTTP {(int)resp.StatusCode}"); return null; }
        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var rel = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitHubRelease);
        if (rel?.TagName is null || rel.Draft || rel.Prerelease) return null;
        return new UpdateInfo(rel.TagName, rel.HtmlUrl ?? AppInfo.ReleasesUrl);
    }
}

sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
sealed partial class GitHubJsonContext : JsonSerializerContext { }
