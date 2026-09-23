using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ImeBadge.Tests;

/// <summary>두 언어 테이블이 같은 키를 가지고, 자리표시자({0}) 개수가 같고, 니모닉(&amp;)이 같은 메뉴·창 안에서 겹치지 않는지.</summary>
public sealed class StringsTests
{
    static readonly IReadOnlyDictionary<string, string> Ko = Strings.Table(UiLanguage.Korean);
    static readonly IReadOnlyDictionary<string, string> En = Strings.Table(UiLanguage.English);

    [Fact]
    public void BothLanguages_HaveSameKeys()
    {
        var onlyKo = Ko.Keys.Except(En.Keys).ToArray();
        var onlyEn = En.Keys.Except(Ko.Keys).ToArray();
        Assert.True(onlyKo.Length == 0, "영어 테이블에 없는 키: " + string.Join(", ", onlyKo));
        Assert.True(onlyEn.Length == 0, "한국어 테이블에 없는 키: " + string.Join(", ", onlyEn));
    }

    [Fact]
    public void Placeholders_MatchAcrossLanguages()
    {
        foreach (var (key, ko) in Ko)
        {
            var a = Placeholders(ko);
            var b = Placeholders(En[key]);
            Assert.True(a.SetEquals(b), $"{key}: ko {{{string.Join(",", a)}}} vs en {{{string.Join(",", b)}}}");
        }
    }

    static HashSet<string> Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)\}").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void NoEmptyStrings()
    {
        foreach (var (key, v) in Ko) Assert.False(string.IsNullOrWhiteSpace(v), "ko " + key);
        foreach (var (key, v) in En) Assert.False(string.IsNullOrWhiteSpace(v), "en " + key);
    }

    /// <summary>트레이 메뉴 최상위 항목의 니모닉이 겹치면 Alt+글자로 엉뚱한 항목이 열린다.</summary>
    [Theory]
    [InlineData(UiLanguage.Korean)]
    [InlineData(UiLanguage.English)]
    public void TrayMenu_MnemonicsAreUnique(UiLanguage lang)
    {
        AssertUniqueMnemonics(Strings.Table(lang), "menu.pause", "menu.settings", "menu.theme", "menu.style", "menu.placement", "menu.size",
            "menu.opacity", "menu.autostart", "menu.checkUpdates", "menu.about", "menu.exit");
    }

    /// <summary>설정 창은 한 창에 모든 항목이 있으므로 창 전체에서 니모닉이 겹치면 안 된다.</summary>
    [Theory]
    [InlineData(UiLanguage.Korean)]
    [InlineData(UiLanguage.English)]
    public void SettingsWindow_MnemonicsAreUnique(UiLanguage lang)
    {
        AssertUniqueMnemonics(Strings.Table(lang), "settings.reset", "look.theme", "look.style", "look.placement", "look.size", "look.opacity",
            "look.hangulColor", "look.englishColor", "look.animate", "look.capsLock", "behavior.autostart", "behavior.fullscreen",
            "behavior.trayState", "behavior.hotkey", "behavior.updates", "behavior.poll", "behavior.language", "exclude.add", "exclude.remove");
    }

    static void AssertUniqueMnemonics(IReadOnlyDictionary<string, string> table, params string[] keys)
    {
        var seen = new Dictionary<char, string>();
        foreach (var key in keys)
        {
            var text = table[key];
            int i = text.IndexOf('&');
            Assert.True(i >= 0 && i + 1 < text.Length, $"{key}: 니모닉(&) 없음");
            char c = char.ToUpperInvariant(text[i + 1]);
            Assert.False(seen.TryGetValue(c, out var other), $"{key} 와 {other} 의 니모닉 '{c}' 가 겹침");
            seen[c] = key;
        }
    }

    [Fact]
    public void Setting_SwitchesLanguage()
    {
        var before = Strings.Setting;
        try
        {
            Strings.Setting = UiLanguage.English;
            Assert.False(Strings.IsKorean);
            Assert.Equal("en", Strings.Code);
            Assert.Equal("&Pause", Strings.Get("menu.pause"));
            Assert.Equal("Now: X", Strings.Format("menu.current", "X"));

            Strings.Setting = UiLanguage.Korean;
            Assert.True(Strings.IsKorean);
            Assert.Equal("일시 중지(&P)", Strings.Get("menu.pause"));
        }
        finally { Strings.Setting = before; }
    }

    [Fact]
    public void Get_UnknownKey_ReturnsKey() => Assert.Equal("no.such.key", Strings.Get("no.such.key"));
}
