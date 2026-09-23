using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
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

/// <summary>릴리스 하나의 요약: 태그, 사람이 볼 페이지 주소, 붙어 있는 파일 목록.</summary>
sealed record UpdateInfo(string Tag, string Url, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>내려받기 진행 상황. Total 이 0 이면 서버가 크기를 알려 주지 않은 경우다.</summary>
readonly record struct DownloadProgress(long Done, long Total)
{
    public int Percent => Total > 0 ? (int)Math.Clamp(Done * 100 / Total, 0, 100) : 0;
}

/// <summary>
/// GitHub Releases 의 "latest"(정식 릴리스 중 최신) 를 조회해 새 버전이 있는지 본다.
/// 롤링 사전 릴리스(latest / dev-*)는 prerelease 라 API 가 돌려주지 않는다.
/// 자동 업그레이드에 필요한 파일 내려받기(<see cref="DownloadAsync"/>)도 여기서 한다.
/// </summary>
static class UpdateChecker
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    /// <summary>파일 하나를 받는 데 허용하는 최대 시간. 아주 느린 회선에서도 끝나도록 넉넉히 둔다.</summary>
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    static HttpClient New(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.ProductName}/{AppVersion.Display}");
        return c;
    }

    static readonly Lazy<HttpClient> Api = new(() =>
    {
        var c = New(TimeSpan.FromSeconds(10));
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    });

    /// <summary>파일 내려받기용. API 조회보다 오래 걸리므로 제한 시간이 다르다.</summary>
    static readonly Lazy<HttpClient> Files = new(() => New(DownloadTimeout));

    public static bool IsDue(Settings s) =>
        s.CheckForUpdates && !AppVersion.IsDevBuild &&
        (s.LastUpdateCheckUtc is null || DateTime.UtcNow - s.LastUpdateCheckUtc.Value >= Interval);

    /// <summary>최신 정식 릴리스. 릴리스가 없거나(404) 응답이 이상하면 null.</summary>
    public static async Task<UpdateInfo?> FetchLatestAsync(CancellationToken ct)
    {
        using var resp = await Api.Value.GetAsync(AppInfo.LatestReleaseApi, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) { Log.Write($"update check: HTTP {(int)resp.StatusCode}"); return null; }
        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var rel = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitHubRelease);
        if (rel?.TagName is null || rel.Draft || rel.Prerelease) return null;

        var assets = new List<ReleaseAsset>();
        foreach (var a in rel.Assets ?? Array.Empty<GitHubAsset>())
            if (a?.Name is { Length: > 0 } name && a.DownloadUrl is { Length: > 0 } url)
                assets.Add(new ReleaseAsset(name, url, a.Size));

        // 응답의 링크는 https://github.com/... 일 때만 그대로 열고, 아니면 릴리스 목록 페이지로 대신한다.
        return new UpdateInfo(rel.TagName, SafeUrl.GitHubOr(rel.HtmlUrl, AppInfo.ReleasesUrl), assets);
    }

    /// <summary>SHA256SUMS.txt 처럼 작은 텍스트 파일을 받는다. 상한을 넘으면 예외.</summary>
    public static async Task<string> FetchTextAsync(string url, int maxBytes, CancellationToken ct)
    {
        if (!SafeUrl.IsGitHubHttps(url)) throw new InvalidOperationException($"refusing non-GitHub url: {url}");
        using var resp = await Files.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long? declared = resp.Content.Headers.ContentLength;
        if (declared > maxBytes) throw new InvalidOperationException($"{url}: {declared} bytes exceeds limit {maxBytes}");
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[maxBytes + 1];
        int read = 0, n;
        while (read <= maxBytes && (n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct).ConfigureAwait(false)) > 0)
            read += n;
        if (read > maxBytes) throw new InvalidOperationException($"{url}: response exceeds limit {maxBytes}");
        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>
    /// 릴리스 파일을 <paramref name="destPath"/> 로 받으면서 SHA-256 을 같이 계산해 16진수로 돌려준다.
    /// 상한(<see cref="UpdatePackage.MaxAssetBytes"/>)을 넘으면 받다가 그만두고 예외를 던진다.
    /// </summary>
    public static async Task<string> DownloadAsync(ReleaseAsset asset, string destPath, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (!SafeUrl.IsGitHubHttps(asset.Url)) throw new InvalidOperationException($"refusing non-GitHub url: {asset.Url}");
        using var resp = await Files.Value.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? asset.Size;
        if (total > UpdatePackage.MaxAssetBytes) throw new InvalidOperationException($"{asset.Name}: {total} bytes exceeds limit");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        // 받다가 끊기면 반쪽짜리 파일이 남는다. 다 받은 뒤 제자리로 옮겨 "완성된 파일만 존재"하게 한다.
        string tmp = destPath + ".part";
        try
        {
            await using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                var buffer = new byte[64 * 1024];
                long done = 0;
                int n;
                // 진행 보고는 UI 스레드로 넘어가므로 100ms 에 한 번으로 묶는다(64KB 마다 보내면 수백 번이 된다).
                long reported = Environment.TickCount64;
                progress?.Report(new DownloadProgress(0, total));
                while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    done += n;
                    if (done > UpdatePackage.MaxAssetBytes) throw new InvalidOperationException($"{asset.Name}: exceeds size limit while downloading");
                    hash.AppendData(buffer, 0, n);
                    await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    if (done == total || Environment.TickCount64 - reported >= 100)
                    {
                        reported = Environment.TickCount64;
                        progress?.Report(new DownloadProgress(done, total));
                    }
                }
            }
            File.Move(tmp, destPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
}

sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
sealed partial class GitHubJsonContext : JsonSerializerContext { }
