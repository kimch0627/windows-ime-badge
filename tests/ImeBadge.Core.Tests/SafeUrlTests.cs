using Xunit;

namespace ImeBadge.Tests;

public sealed class SafeUrlTests
{
    [Theory]
    [InlineData("https://github.com/kimch0627/windows-ime-badge/releases/tag/v1.0.0", true)]
    [InlineData("https://GITHUB.com/x", true)]
    [InlineData("https://api.github.com/repos/x", true)]
    [InlineData("http://github.com/x", false)]            // 평문
    [InlineData("https://github.com.evil.example/x", false)] // 호스트 위장
    [InlineData("https://evilgithub.com/x", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("C:\\Windows\\System32\\cmd.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGitHubHttps(string? url, bool expected) => Assert.Equal(expected, SafeUrl.IsGitHubHttps(url));

    [Fact]
    public void GitHubOr_FallsBack()
    {
        Assert.Equal("https://github.com/a", SafeUrl.GitHubOr("https://github.com/a", "fallback"));
        Assert.Equal("fallback", SafeUrl.GitHubOr("ftp://github.com/a", "fallback"));
    }
}
