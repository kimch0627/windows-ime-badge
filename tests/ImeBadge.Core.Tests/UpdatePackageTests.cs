using System.Collections.Generic;
using Xunit;

namespace ImeBadge.Tests;

public sealed class UpdatePackageTests
{
    const string Base = "https://github.com/kimch0627/windows-ime-badge/releases/download/v1.4.0/";

    static ReleaseAsset A(string name, long size = 20_000_000) => new(name, Base + name, size);

    /// <summary>build.yml 이 정식 릴리스에 올리는 파일 그대로.</summary>
    static List<ReleaseAsset> Release() => new()
    {
        A("ImeBadge-Setup-1.4.0.exe"),
        A("ImeBadge-win-x64.exe", 2_000_000),
        A("ImeBadge-win-x64-selfcontained.exe"),
        A("SHA256SUMS.txt", 400),
    };

    [Theory]
    [InlineData(UpdateFlavor.Installer, "ImeBadge-Setup-1.4.0.exe")]
    [InlineData(UpdateFlavor.SelfContainedExe, "ImeBadge-win-x64-selfcontained.exe")]
    [InlineData(UpdateFlavor.FrameworkExe, "ImeBadge-win-x64.exe")]
    public void Pick_MatchesFlavor(UpdateFlavor flavor, string expected) =>
        Assert.Equal(expected, UpdatePackage.Pick(Release(), flavor)?.Name);

    [Fact]
    public void Pick_PrefersVersionedInstaller()
    {
        var assets = new List<ReleaseAsset> { A("ImeBadge-Setup.exe"), A("ImeBadge-Setup-1.4.0.exe") };
        Assert.Equal("ImeBadge-Setup-1.4.0.exe", UpdatePackage.Pick(assets, UpdateFlavor.Installer)?.Name);
    }

    [Fact]
    public void Pick_FallsBackToRollingInstaller() =>
        Assert.Equal("ImeBadge-Setup.exe", UpdatePackage.Pick(new List<ReleaseAsset> { A("ImeBadge-Setup.exe") }, UpdateFlavor.Installer)?.Name);

    [Fact]
    public void Pick_SelfContainedIsNotConfusedWithFrameworkExe()
    {
        var only = new List<ReleaseAsset> { A("ImeBadge-win-x64-selfcontained.exe") };
        Assert.Null(UpdatePackage.Pick(only, UpdateFlavor.FrameworkExe));
    }

    [Fact]
    public void Pick_Nothing_WhenMissingOrUnsafe()
    {
        Assert.Null(UpdatePackage.Pick(null, UpdateFlavor.Installer));
        Assert.Null(UpdatePackage.Pick(new List<ReleaseAsset>(), UpdateFlavor.Installer));
        // GitHub https 가 아닌 주소, 상한을 넘는 크기는 고르지 않는다
        Assert.Null(UpdatePackage.Pick(new List<ReleaseAsset> { new("ImeBadge-Setup-1.4.0.exe", "http://evil.test/x.exe", 100) }, UpdateFlavor.Installer));
        Assert.Null(UpdatePackage.Pick(new List<ReleaseAsset> { A("ImeBadge-Setup-1.4.0.exe", UpdatePackage.MaxAssetBytes + 1) }, UpdateFlavor.Installer));
    }

    [Fact]
    public void Sums_FindsChecksumList() =>
        Assert.Equal("SHA256SUMS.txt", UpdatePackage.Sums(Release())?.Name);

    [Fact]
    public void Sums_Null_WhenAbsent() =>
        Assert.Null(UpdatePackage.Sums(new List<ReleaseAsset> { A("ImeBadge-Setup-1.4.0.exe") }));

    const string SumsText = """
        # comment
        1111111111111111111111111111111111111111111111111111111111111111  ImeBadge-Setup-1.4.0.exe
        2222222222222222222222222222222222222222222222222222222222222222 *ImeBadge-win-x64-selfcontained.exe
        nothex  ImeBadge-win-x64.exe

        """;

    [Fact]
    public void ParseSums_ReadsNamesAndHashes()
    {
        var map = UpdatePackage.ParseSums(SumsText);
        Assert.Equal(2, map.Count);
        Assert.Equal(new string('1', 64), map["ImeBadge-Setup-1.4.0.exe"]);
        Assert.Equal(new string('2', 64), map["ImeBadge-win-x64-selfcontained.exe"]);   // 이진 모드 '*' 는 이름에서 뗀다
        Assert.False(map.ContainsKey("ImeBadge-win-x64.exe"));                          // 해시가 16진수 64자리가 아니면 버린다
    }

    [Fact]
    public void ParseSums_Empty()
    {
        Assert.Empty(UpdatePackage.ParseSums(null));
        Assert.Empty(UpdatePackage.ParseSums("   "));
    }

    [Fact]
    public void ExpectedHash()
    {
        Assert.Equal(new string('1', 64), UpdatePackage.ExpectedHash(SumsText, "ImeBadge-Setup-1.4.0.exe"));
        Assert.Null(UpdatePackage.ExpectedHash(SumsText, "ImeBadge-win-x64.exe"));
        Assert.Null(UpdatePackage.ExpectedHash(SumsText, null));
    }

    [Theory]
    [InlineData("ABCD", "abcd", true)]
    [InlineData("abcd", "abcd", true)]
    [InlineData("abcd", "abce", false)]
    [InlineData(null, "abcd", false)]
    [InlineData("abcd", null, false)]
    [InlineData("", "", false)]
    public void HashMatches(string? expected, string? actual, bool ok) =>
        Assert.Equal(ok, UpdatePackage.HashMatches(expected, actual));

    [Theory]
    [InlineData("v1.4.0", "ImeBadge-Setup-1.4.0.exe", "v1.4.0-ImeBadge-Setup-1.4.0.exe")]
    [InlineData(null, "ImeBadge-win-x64.exe", "ImeBadge-win-x64.exe")]
    // 경로를 벗어나려는 이름·태그는 안전한 글자만 남는다
    [InlineData("../../etc", "..\\..\\evil.exe", "etc-evil.exe")]
    [InlineData("v1.0.0", "", "v1.0.0-download.exe")]
    public void StagedFileName(string? tag, string asset, string expected) =>
        Assert.Equal(expected, UpdatePackage.StagedFileName(tag, asset));
}
