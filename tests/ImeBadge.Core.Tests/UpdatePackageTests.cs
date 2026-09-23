using System.Collections.Generic;
using System.Runtime.InteropServices;
using Xunit;

namespace ImeBadge.Tests;

public sealed class UpdatePackageTests
{
    const string Base = "https://github.com/kimch0627/windows-ime-badge/releases/download/v1.4.0/";

    static ReleaseAsset A(string name, long size = 20_000_000) => new(name, Base + name, size);

    /// <summary>build.yml 이 정식 릴리스에 올리는 파일 그대로(아키텍처마다 세 개 + 체크섬). arm64 를 먼저 두어 순서에 기대지 않음을 확인한다.</summary>
    static List<ReleaseAsset> Release() => new()
    {
        A("ImeBadge-Setup-1.4.0-arm64.exe"),
        A("ImeBadge-Setup-1.4.0-x64.exe"),
        A("ImeBadge-win-arm64.exe", 2_000_000),
        A("ImeBadge-win-arm64-selfcontained.exe"),
        A("ImeBadge-win-x64.exe", 2_000_000),
        A("ImeBadge-win-x64-selfcontained.exe"),
        A("SHA256SUMS.txt", 400),
    };

    [Theory]
    [InlineData(UpdateFlavor.Installer, "x64", "ImeBadge-Setup-1.4.0-x64.exe")]
    [InlineData(UpdateFlavor.SelfContainedExe, "x64", "ImeBadge-win-x64-selfcontained.exe")]
    [InlineData(UpdateFlavor.FrameworkExe, "x64", "ImeBadge-win-x64.exe")]
    [InlineData(UpdateFlavor.Installer, "arm64", "ImeBadge-Setup-1.4.0-arm64.exe")]
    [InlineData(UpdateFlavor.SelfContainedExe, "arm64", "ImeBadge-win-arm64-selfcontained.exe")]
    [InlineData(UpdateFlavor.FrameworkExe, "arm64", "ImeBadge-win-arm64.exe")]
    public void Pick_MatchesFlavorAndArch(UpdateFlavor flavor, string arch, string expected) =>
        Assert.Equal(expected, UpdatePackage.Pick(Release(), flavor, arch)?.Name);

    [Fact]
    public void Pick_PrefersVersionedInstaller()
    {
        var assets = new List<ReleaseAsset> { A("ImeBadge-Setup-x64.exe"), A("ImeBadge-Setup-1.4.0-x64.exe") };
        Assert.Equal("ImeBadge-Setup-1.4.0-x64.exe", UpdatePackage.Pick(assets, UpdateFlavor.Installer, "x64")?.Name);
    }

    [Fact]
    public void Pick_FallsBackToRollingInstaller() =>
        Assert.Equal("ImeBadge-Setup-x64.exe", UpdatePackage.Pick(new List<ReleaseAsset> { A("ImeBadge-Setup-x64.exe") }, UpdateFlavor.Installer, "x64")?.Name);

    [Fact]
    public void Pick_NeverCrossesArchitecture()
    {
        // arm64 프로세스에 x64 파일만 있는 릴리스(1.3.1 까지의 옛 릴리스 모양) → 아무것도 고르지 않는다
        var x64Only = new List<ReleaseAsset> { A("ImeBadge-Setup-1.4.0.exe"), A("ImeBadge-Setup-1.4.0-x64.exe"), A("ImeBadge-win-x64.exe"), A("ImeBadge-win-x64-selfcontained.exe") };
        Assert.Null(UpdatePackage.Pick(x64Only, UpdateFlavor.Installer, "arm64"));
        Assert.Null(UpdatePackage.Pick(x64Only, UpdateFlavor.SelfContainedExe, "arm64"));
        Assert.Null(UpdatePackage.Pick(x64Only, UpdateFlavor.FrameworkExe, "arm64"));
        // x64 프로세스는 arm64 파일이 앞에 있어도 x64 파일을 고른다
        var arm64First = new List<ReleaseAsset> { A("ImeBadge-Setup-arm64.exe"), A("ImeBadge-Setup-1.4.0-arm64.exe"), A("ImeBadge-Setup-1.4.0-x64.exe") };
        Assert.Equal("ImeBadge-Setup-1.4.0-x64.exe", UpdatePackage.Pick(arm64First, UpdateFlavor.Installer, "x64")?.Name);
    }

    [Fact]
    public void Pick_LegacyUnsuffixedInstallerIsIgnored()
    {
        // 접미사 없는 옛 이름은 아키텍처를 알 수 없으므로 고르지 않는다(옛 릴리스로는 어차피 내려가지 않는다)
        var legacy = new List<ReleaseAsset> { A("ImeBadge-Setup-1.4.0.exe"), A("ImeBadge-Setup.exe") };
        Assert.Null(UpdatePackage.Pick(legacy, UpdateFlavor.Installer, "x64"));
    }

    [Fact]
    public void Pick_SelfContainedIsNotConfusedWithFrameworkExe()
    {
        var only = new List<ReleaseAsset> { A("ImeBadge-win-x64-selfcontained.exe") };
        Assert.Null(UpdatePackage.Pick(only, UpdateFlavor.FrameworkExe, "x64"));
    }

    [Fact]
    public void Pick_Nothing_WhenMissingOrUnsafe()
    {
        Assert.Null(UpdatePackage.Pick(null, UpdateFlavor.Installer, "x64"));
        Assert.Null(UpdatePackage.Pick(new List<ReleaseAsset>(), UpdateFlavor.Installer, "x64"));
        // 아키텍처를 모르면(릴리스를 만들지 않는 CPU) 아무것도 고르지 않는다
        Assert.Null(UpdatePackage.Pick(Release(), UpdateFlavor.Installer, null));
        Assert.Null(UpdatePackage.Pick(Release(), UpdateFlavor.Installer, ""));
        // GitHub https 가 아닌 주소, 상한을 넘는 크기는 고르지 않는다
        Assert.Null(UpdatePackage.Pick(new List<ReleaseAsset> { new("ImeBadge-Setup-1.4.0-x64.exe", "http://evil.test/x.exe", 100) }, UpdateFlavor.Installer, "x64"));
        Assert.Null(UpdatePackage.Pick(new List<ReleaseAsset> { A("ImeBadge-Setup-1.4.0-x64.exe", UpdatePackage.MaxAssetBytes + 1) }, UpdateFlavor.Installer, "x64"));
    }

    [Theory]
    [InlineData(Architecture.X64, "x64")]
    [InlineData(Architecture.Arm64, "arm64")]
    [InlineData(Architecture.X86, null)]
    [InlineData(Architecture.Arm, null)]
    public void ArchToken_MatchesReleaseFileNames(Architecture arch, string? expected) =>
        Assert.Equal(expected, UpdatePackage.ArchToken(arch));

    [Fact]
    public void Sums_FindsChecksumList() =>
        Assert.Equal("SHA256SUMS.txt", UpdatePackage.Sums(Release())?.Name);

    [Fact]
    public void Sums_Null_WhenAbsent() =>
        Assert.Null(UpdatePackage.Sums(new List<ReleaseAsset> { A("ImeBadge-Setup-1.4.0-x64.exe") }));

    const string SumsText = """
        # comment
        1111111111111111111111111111111111111111111111111111111111111111  ImeBadge-Setup-1.4.0-x64.exe
        2222222222222222222222222222222222222222222222222222222222222222 *ImeBadge-win-x64-selfcontained.exe
        nothex  ImeBadge-win-x64.exe

        """;

    [Fact]
    public void ParseSums_ReadsNamesAndHashes()
    {
        var map = UpdatePackage.ParseSums(SumsText);
        Assert.Equal(2, map.Count);
        Assert.Equal(new string('1', 64), map["ImeBadge-Setup-1.4.0-x64.exe"]);
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
        Assert.Equal(new string('1', 64), UpdatePackage.ExpectedHash(SumsText, "ImeBadge-Setup-1.4.0-x64.exe"));
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
    [InlineData("v1.4.0", "ImeBadge-Setup-1.4.0-x64.exe", "v1.4.0-ImeBadge-Setup-1.4.0-x64.exe")]
    [InlineData(null, "ImeBadge-win-x64.exe", "ImeBadge-win-x64.exe")]
    // 경로를 벗어나려는 이름·태그는 안전한 글자만 남는다
    [InlineData("../../etc", "..\\..\\evil.exe", "etc-evil.exe")]
    [InlineData("v1.0.0", "", "v1.0.0-download.exe")]
    public void StagedFileName(string? tag, string asset, string expected) =>
        Assert.Equal(expected, UpdatePackage.StagedFileName(tag, asset));
}
