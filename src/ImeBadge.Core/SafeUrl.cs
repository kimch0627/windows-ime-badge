using System;

namespace ImeBadge;

/// <summary>
/// 외부에서 받은 URL 을 브라우저로 열기 전에 확인한다. 업데이트 알림은 GitHub API 응답의 html_url 을 여는데,
/// TLS 로 보호되더라도 "https 이고 github.com 인 주소만 연다"는 규칙을 한 겹 더 두면 응답이 뒤바뀌어도
/// 엉뚱한 프로그램(다른 스킴, 다른 호스트)이 실행되지 않는다.
/// </summary>
public static class SafeUrl
{
    /// <summary>https://github.com/... 또는 그 하위 호스트(api.github.com 등)만 허용.</summary>
    public static bool IsGitHubHttps(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (!string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;
        string host = u.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>허용되지 않는 주소면 <paramref name="fallback"/> 을 돌려준다.</summary>
    public static string GitHubOr(string? url, string fallback) => IsGitHubHttps(url) ? url! : fallback;
}
