using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace ImeBadge.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "imebadge-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    string P(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Load_WithNoFile_ReturnsDefaults()
    {
        var store = new SettingsStore(P("sub/settings.json"));
        var s = store.Load();
        Assert.Equal(BadgeStyle.Pill, s.Style);
        Assert.Equal(100, s.SizePercent);
        Assert.True(s.CheckForUpdates);
        Assert.Empty(s.ExcludedProcesses);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var store = new SettingsStore(P("settings.json"));
        var s = new Settings
        {
            Style = BadgeStyle.DotFlash,
            Placement = BadgePlacement.BelowRight,
            SizePercent = 130,
            OpacityPercent = 70,
            HangulColor = "#FF0000",
            ExcludedProcesses = new List<string> { "mstsc", "Unreal*" },
            HideOnFullscreen = false,
            LastUpdateCheckUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        };
        Assert.True(store.Save(s));

        var back = store.Load();
        Assert.Equal(BadgeStyle.DotFlash, back.Style);
        Assert.Equal(BadgePlacement.BelowRight, back.Placement);
        Assert.Equal(130, back.SizePercent);
        Assert.Equal(70, back.OpacityPercent);
        Assert.Equal("#FF0000", back.HangulColor);
        Assert.Equal(new[] { "mstsc", "Unreal*" }, back.ExcludedProcesses);
        Assert.False(back.HideOnFullscreen);
        Assert.Equal(s.LastUpdateCheckUtc, back.LastUpdateCheckUtc);
        Assert.False(File.Exists(P("settings.json.tmp")));   // 임시 파일은 남지 않는다
    }

    [Fact]
    public void Save_WritesEnumsAsNames()
    {
        var store = new SettingsStore(P("settings.json"));
        store.Save(new Settings { Style = BadgeStyle.Underline });
        Assert.Contains("\"Underline\"", File.ReadAllText(P("settings.json")));
    }

    [Fact]
    public void Load_MigratesLegacyFile_AndKeepsLegacy()
    {
        // 0.x 버전이 exe 옆에 남긴 파일 형식 (새 항목은 없음)
        File.WriteAllText(P("imebadge.settings.json"), """{ "Style": "Box", "Placement": "BelowRight", "SizePercent": 160, "OpacityPercent": 85 }""");
        var store = new SettingsStore(P("new/settings.json"), P("imebadge.settings.json"));

        var s = store.Load();
        Assert.Equal(BadgeStyle.Box, s.Style);
        Assert.Equal(160, s.SizePercent);
        Assert.True(s.HideOnFullscreen);                 // 새 항목은 기본값
        Assert.True(File.Exists(P("new/settings.json"))); // 새 위치에 복사됨
        Assert.True(File.Exists(P("imebadge.settings.json")));
    }

    [Fact]
    public void Load_PrefersNewFileOverLegacy()
    {
        File.WriteAllText(P("legacy.json"), """{ "SizePercent": 160 }""");
        File.WriteAllText(P("settings.json"), """{ "SizePercent": 80 }""");
        var s = new SettingsStore(P("settings.json"), P("legacy.json")).Load();
        Assert.Equal(80, s.SizePercent);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        File.WriteAllText(P("settings.json"), "{ not json");
        var s = new SettingsStore(P("settings.json")).Load();
        Assert.Equal(BadgeStyle.Pill, s.Style);
    }

    [Fact]
    public void Load_ClampsOutOfRangeValues()
    {
        File.WriteAllText(P("settings.json"), """{ "SizePercent": 9999, "OpacityPercent": 1, "PollIntervalMs": 5, "HangulColor": "red", "ExcludedProcesses": ["", "  ", "mstsc"] }""");
        var s = new SettingsStore(P("settings.json")).Load();
        Assert.Equal(300, s.SizePercent);
        Assert.Equal(30, s.OpacityPercent);
        Assert.Equal(50, s.PollIntervalMs);
        Assert.Equal(Settings.DefaultHangulColor, s.HangulColor);
        Assert.Equal(new[] { "mstsc" }, s.ExcludedProcesses);
    }

    [Fact]
    public void Clone_And_CopyFrom_AreIndependent()
    {
        var a = new Settings { ExcludedProcesses = new List<string> { "x" } };
        var b = a.Clone();
        b.ExcludedProcesses.Add("y");
        b.SizePercent = 50;
        Assert.Single(a.ExcludedProcesses);
        Assert.Equal(100, a.SizePercent);

        a.CopyFrom(b);
        Assert.Equal(2, a.ExcludedProcesses.Count);
        Assert.Equal(50, a.SizePercent);
    }
}

public sealed class ColorHexTests
{
    [Theory]
    [InlineData("#0078D7", unchecked((int)0xFF0078D7))]
    [InlineData("0078d7", unchecked((int)0xFF0078D7))]
    [InlineData("#800078D7", unchecked((int)0x800078D7))]
    [InlineData("  #FFFFFF ", -1)]
    public void TryParse_Valid(string text, int expected)
    {
        Assert.True(ColorHex.TryParse(text, out int argb));
        Assert.Equal(expected, argb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void TryParse_Invalid(string? text) => Assert.False(ColorHex.TryParse(text, out _));

    [Fact]
    public void ToHex_DropsAlpha() => Assert.Equal("#0078D7", ColorHex.ToHex(unchecked((int)0xFF0078D7)));
}
