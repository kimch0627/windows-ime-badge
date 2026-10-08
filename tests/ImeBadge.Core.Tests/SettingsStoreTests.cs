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
            TrayShowsState = false,
            Animate = false,
            ShowCapsLock = false,
            ShowShiftHold = false,
            ShowShiftImmediately = true,
            ShowInsert = false,
            Language = UiLanguage.English,
            Hotkey = "Ctrl+Shift+F9",
            CaretSonar = true,
            CaretSonarHotkey = "Ctrl+Shift+F8",
            CaretSonarOnSwitch = false,
            FocusStealWarning = true,
            ShowSelection = true,
            PasswordWarning = true,
            KeepImeMode = true,
            LastUpdateCheckUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            SkippedUpdateTag = "v9.9.9",
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
        Assert.False(back.TrayShowsState);
        Assert.False(back.Animate);
        Assert.False(back.ShowCapsLock);
        Assert.False(back.ShowShiftHold);
        Assert.True(back.ShowShiftImmediately);
        Assert.False(back.ShowInsert);
        Assert.Equal(UiLanguage.English, back.Language);
        Assert.Equal("Ctrl+Shift+F9", back.Hotkey);
        Assert.True(back.CaretSonar);
        Assert.Equal("Ctrl+Shift+F8", back.CaretSonarHotkey);
        Assert.False(back.CaretSonarOnSwitch);
        Assert.True(back.FocusStealWarning);
        Assert.True(back.ShowSelection);
        Assert.True(back.PasswordWarning);
        Assert.True(back.KeepImeMode);
        Assert.Equal(s.LastUpdateCheckUtc, back.LastUpdateCheckUtc);
        Assert.Equal("v9.9.9", back.SkippedUpdateTag);
        Assert.False(File.Exists(P("settings.json.tmp")));   // 임시 파일은 남지 않는다
    }

    [Fact]
    public void Save_WritesEnumsAsNames()
    {
        var store = new SettingsStore(P("settings.json"));
        store.Save(new Settings { Style = BadgeStyle.Underline, Language = UiLanguage.Korean });
        string json = File.ReadAllText(P("settings.json"));
        Assert.Contains("\"Underline\"", json);
        Assert.Contains("\"Korean\"", json);
    }

    [Fact]
    public void Normalize_ResetsUndefinedLanguage()
    {
        var s = new Settings { Language = (UiLanguage)99 };
        s.Normalize();
        Assert.Equal(UiLanguage.Auto, s.Language);
    }

    [Theory]
    [InlineData(BadgePlacement.Above, "AboveRight", "above")]
    [InlineData(BadgePlacement.Below, "BelowRight", "below")]
    public void Save_CenterPlacement_WritesValueOldVersionsCanRead(BadgePlacement place, string stored, string center)
    {
        // 1.11.0 까지는 모르는 enum 이름이 있으면 설정 파일 전체를 버린다. "Placement" 에는 그 버전들이 아는 값만 쓴다.
        var store = new SettingsStore(P("settings.json"));
        Assert.True(store.Save(new Settings { Placement = place, SizePercent = 130 }));
        string json = File.ReadAllText(P("settings.json"));
        Assert.Contains($"\"Placement\": \"{stored}\"", json);
        Assert.Contains($"\"PlacementCenter\": \"{center}\"", json);

        var back = store.Load();
        Assert.Equal(place, back.Placement);
        Assert.Equal(130, back.SizePercent);
    }

    [Fact]
    public void Save_CornerPlacement_OmitsPlacementCenter()
    {
        var store = new SettingsStore(P("settings.json"));
        var s = new Settings { Placement = BadgePlacement.Above };
        s.Placement = BadgePlacement.BelowLeft;
        Assert.True(store.Save(s));
        Assert.DoesNotContain("PlacementCenter", File.ReadAllText(P("settings.json")));
        Assert.Equal(BadgePlacement.BelowLeft, store.Load().Placement);
    }

    [Fact]
    public void Load_UnknownEnumNames_ResetOnlyThoseSettings()
    {
        // 나중 버전이 더한 값이나 손으로 고친 오타. 그 항목만 기본값이 되고 나머지 설정은 그대로 남는다.
        File.WriteAllText(P("settings.json"), """{ "Style": "Hexagon", "Placement": "Sideways", "Language": "Klingon", "SizePercent": 150, "ExcludedProcesses": ["mstsc"] }""");
        var s = new SettingsStore(P("settings.json")).Load();
        Assert.Equal(BadgeStyle.Pill, s.Style);
        Assert.Equal(BadgePlacement.AboveRight, s.Placement);
        Assert.Equal(UiLanguage.Auto, s.Language);
        Assert.Equal(150, s.SizePercent);
        Assert.Equal(new[] { "mstsc" }, s.ExcludedProcesses);
    }

    [Fact]
    public void Load_UnknownPlacementCenter_UsesStoredPlacement()
    {
        File.WriteAllText(P("settings.json"), """{ "Placement": "BelowLeft", "PlacementCenter": "middle" }""");
        var s = new SettingsStore(P("settings.json")).Load();
        Assert.Equal(BadgePlacement.BelowLeft, s.Placement);
        Assert.Null(s.PlacementCenter);
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
        Assert.True(s.ShowCapsLock);
        Assert.True(s.ShowShiftHold);
        Assert.False(s.ShowShiftImmediately);            // 1.13.0 이하 설정 파일에는 없다 → 기본 꺼짐(0.3초 기다림)
        Assert.True(s.ShowInsert);                       // 1.13.0 이하 설정 파일에는 없다 → 기본 켜짐
        Assert.False(s.CaretSonar);                      // 실험 기능은 모두 기본 꺼짐
        Assert.Equal("Ctrl+Alt+J", s.CaretSonarHotkey);
        Assert.True(s.CaretSonarOnSwitch);
        Assert.False(s.FocusStealWarning);
        Assert.False(s.ShowSelection);
        Assert.False(s.PasswordWarning);
        Assert.False(s.KeepImeMode);
        Assert.Equal(UiLanguage.Auto, s.Language);
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
    public void Load_OldFileWithoutThemeOrCharacter_KeepsClassicLook()
    {
        // 1.4.0 까지의 파일: 테마·캐릭터 항목이 없다. 기존 사용자는 지금까지와 똑같이 보여야 한다.
        File.WriteAllText(P("settings.json"), """{ "Style": "Box", "HangulColor": "#E74856" }""");
        var s = new SettingsStore(P("settings.json")).Load();
        Assert.Equal(DesignThemes.ClassicId, s.Theme);
        Assert.Equal(BadgeCharacters.None, s.Character);
        Assert.Equal(BadgeStyle.Box, s.Style);
        Assert.Equal("#E74856", s.HangulColor);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsThemeAndCharacter()
    {
        var store = new SettingsStore(P("settings.json"));
        Assert.True(store.Save(new Settings { Theme = "blossom", Character = BadgeCharacters.Cat }));
        var back = store.Load();
        Assert.Equal("blossom", back.Theme);
        Assert.Equal(BadgeCharacters.Cat, back.Character);
        Assert.Contains("\"Theme\": \"blossom\"", File.ReadAllText(P("settings.json")));
    }

    [Fact]
    public void Load_UnknownThemeAndCharacter_FallBackWithoutLosingOtherSettings()
    {
        // 나중 버전이 만든 테마·캐릭터를 이 버전이 읽는 경우. 문자열 항목이라 파일 전체가 버려지지 않는다.
        File.WriteAllText(P("settings.json"), """{ "Theme": "galaxy", "Character": "dragon", "SizePercent": 150 }""");
        var s = new SettingsStore(P("settings.json")).Load();
        Assert.Equal(DesignThemes.ClassicId, s.Theme);
        Assert.Equal(BadgeCharacters.None, s.Character);
        Assert.Equal(150, s.SizePercent);
    }

    [Fact]
    public void Normalize_CanonicalizesCase()
    {
        var s = new Settings { Theme = "BLOSSOM", Character = "Heart" };
        s.Normalize();
        Assert.Equal("blossom", s.Theme);
        Assert.Equal(BadgeCharacters.Heart, s.Character);
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

        b.Theme = "mint"; b.Character = BadgeCharacters.Star;
        a.CopyFrom(b);
        Assert.Equal("mint", a.Theme);
        Assert.Equal(BadgeCharacters.Star, a.Character);
        Assert.Equal(2, a.ExcludedProcesses.Count);
        Assert.Equal(50, a.SizePercent);

        b.Visibility = BadgeVisibility.OnChange;
        b.ShowInsert = false;
        b.ShowShiftImmediately = true;
        b.CaretSonar = true;
        b.CaretSonarHotkey = "Ctrl+Alt+K";
        b.CaretSonarOnSwitch = false;
        b.FocusStealWarning = true;
        b.ShowSelection = true;
        b.PasswordWarning = true;
        b.KeepImeMode = true;
        a.CopyFrom(b);
        Assert.Equal(BadgeVisibility.OnChange, a.Visibility);
        Assert.False(a.ShowInsert);
        Assert.True(a.ShowShiftImmediately);
        Assert.True(a.CaretSonar);
        Assert.Equal("Ctrl+Alt+K", a.CaretSonarHotkey);
        Assert.False(a.CaretSonarOnSwitch);
        Assert.True(a.FocusStealWarning);
        Assert.True(a.ShowSelection);
        Assert.True(a.PasswordWarning);
        Assert.True(a.KeepImeMode);
    }

    [Fact]
    public void Load_OldFileWithoutVisibility_IsAlways_AndUnknownFallsBack()
    {
        File.WriteAllText(P("old.json"), """{ "Style": "Dot" }""");
        Assert.Equal(BadgeVisibility.Always, new SettingsStore(P("old.json")).Load().Visibility);

        File.WriteAllText(P("new.json"), """{ "Style": "Dot", "Visibility": "sometimes", "SizePercent": 130 }""");
        var s = new SettingsStore(P("new.json")).Load();
        Assert.Equal(BadgeVisibility.Always, s.Visibility);
        Assert.Equal(130, s.SizePercent);   // 모르는 값 하나 때문에 다른 설정을 잃지 않는다
    }

    [Fact]
    public void Load_OldFileWithRemovedSettings_KeepsOtherSettings()
    {
        // 없어진 설정("화면을 분석해 커서를 따라가기" TrackCursorByImage, "커서를 못 찾는 앱" CornerBadgeProcesses)이
        // 남은 예전 설정 파일도 나머지는 그대로 읽힌다.
        File.WriteAllText(P("old.json"), """{ "SizePercent": 130, "TrackCursorByImage": true, "CornerBadgeProcesses": ["Xshell*", "SecureCRT"], "ExcludedProcesses": ["mstsc"] }""");
        var s = new SettingsStore(P("old.json")).Load();
        Assert.Equal(130, s.SizePercent);
        Assert.Equal(new[] { "mstsc" }, s.ExcludedProcesses);
    }

    [Fact]
    public void Export_ThenImport_RoundTrips_WithoutMachineOnlyValues()
    {
        var s = new Settings
        {
            Theme = "candy",
            Visibility = BadgeVisibility.DimWhileTyping,
            SizePercent = 150,
            ExcludedProcesses = new List<string> { "mstsc" },
            LastUpdateCheckUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            SkippedUpdateTag = "v9.9.9",
        };
        SettingsStore.Export(s, P("export.json"));
        var back = SettingsStore.Import(P("export.json"));
        Assert.Equal("candy", back.Theme);
        Assert.Equal(BadgeVisibility.DimWhileTyping, back.Visibility);
        Assert.Equal(150, back.SizePercent);
        Assert.Equal(new[] { "mstsc" }, back.ExcludedProcesses);
        Assert.Null(back.LastUpdateCheckUtc);   // 이 PC 의 업데이트 기록은 옮기지 않는다
        Assert.Null(back.SkippedUpdateTag);
        Assert.Equal("v9.9.9", s.SkippedUpdateTag);   // 원본은 그대로
    }

    [Fact]
    public void Import_NormalizesOutOfRange()
    {
        File.WriteAllText(P("wild.json"), """{ "SizePercent": 9999, "Theme": "nope", "Placement": "Sideways" }""");
        var s = SettingsStore.Import(P("wild.json"));
        Assert.Equal(300, s.SizePercent);
        Assert.Equal(DesignThemes.ClassicId, s.Theme);
        Assert.Equal(BadgePlacement.AboveRight, s.Placement);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    public void Import_RejectsNonSettingsFiles(string content)
    {
        File.WriteAllText(P("bad.json"), content);
        Assert.Throws<InvalidDataException>(() => SettingsStore.Import(P("bad.json")));
    }

    [Fact]
    public void Import_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => SettingsStore.Import(P("nope.json")));
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

    [Fact]
    public void RelativeLuminance_BlackAndWhite()
    {
        Assert.Equal(0.0, ColorHex.RelativeLuminance(unchecked((int)0xFF000000)), 3);
        Assert.Equal(1.0, ColorHex.RelativeLuminance(unchecked((int)0xFFFFFFFF)), 3);
    }

    [Fact]
    public void ContrastRatio_WhiteOnDefaultHangulColor_MeetsAA()
    {
        Assert.True(ColorHex.TryParse(Settings.DefaultHangulColor, out int blue));
        Assert.True(ColorHex.ContrastRatio(blue, unchecked((int)0xFFFFFFFF)) >= 4.5);
        Assert.Equal(21.0, ColorHex.ContrastRatio(0, unchecked((int)0xFFFFFFFF)), 1);
    }

    [Theory]
    [InlineData("#0078D7", true)]    // 파랑(예전 기본값): 흰 글자
    [InlineData("#0067C0", true)]    // 파랑(기본값): 흰 글자
    [InlineData("#E74856", true)]    // Windows 빨강 강조색: 흰 글자
    [InlineData("#00B294", true)]    // Windows 청록 강조색: 흰 글자
    [InlineData("#3C3C3C", true)]    // 진회색: 흰 글자
    [InlineData("#FFB900", false)]   // Windows 금색 강조색: 검은 글자
    [InlineData("#FFD34D", false)]   // 노랑: 검은 글자
    [InlineData("#FFFFFF", false)]
    [InlineData("#000000", true)]
    public void PrefersWhiteText_PicksReadableColor(string hex, bool white)
    {
        Assert.True(ColorHex.TryParse(hex, out int argb));
        Assert.Equal(white, ColorHex.PrefersWhiteText(argb));
    }
}
