using Xunit;

namespace ImeBadge.Tests;

public sealed class VersionInfoTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3", false)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2", "1.2.0", false)]
    [InlineData("0.0.0-dev", "0.0.0", true)]
    [InlineData("1.2.3-dev.abc1234", "1.2.3", true)]
    [InlineData("1.2.3+sha.abcdef", "1.2.3", false)]
    [InlineData("1.2.3-rc.1+build", "1.2.3", true)]
    public void TryParse_Variants(string text, string expected, bool pre)
    {
        Assert.True(VersionInfo.TryParse(text, out var v, out bool isPre));
        Assert.Equal(expected, v.ToString(3));
        Assert.Equal(pre, isPre);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("dev-main")]
    public void TryParse_Invalid(string? text) => Assert.False(VersionInfo.TryParse(text, out _, out _));

    [Theory]
    [InlineData("1.0.0", "v1.0.1", true)]
    [InlineData("1.0.0", "v1.1.0", true)]
    [InlineData("1.0.0", "v2.0.0", true)]
    [InlineData("1.0.1", "v1.0.0", false)]
    [InlineData("1.0.0", "v1.0.0", false)]
    [InlineData("1.0.0-dev", "v1.0.0", true)]     // 사전 릴리스 → 같은 숫자의 정식은 새것
    [InlineData("1.0.0", "v1.0.0-rc.1", false)]
    [InlineData("1.0.0", "latest", false)]         // 롤링 태그는 무시
    [InlineData("garbage", "v9.9.9", false)]
    public void IsNewer(string current, string latest, bool expected) =>
        Assert.Equal(expected, VersionInfo.IsNewer(current, latest));

    [Theory]
    [InlineData("1.2.3+abcdef", "1.2.3")]
    [InlineData("0.0.0-dev", "0.0.0-dev")]
    [InlineData(null, "0.0.0")]
    public void Display(string? info, string expected) => Assert.Equal(expected, VersionInfo.Display(info));
}
