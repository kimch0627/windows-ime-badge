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

    /// <summary>
    /// 설정 창은 페이지마다 보이는 항목이 다르고, Alt+글자는 지금 보이는 페이지의 항목만 움직인다(숨은 페이지는 니모닉을 받지 않는다).
    /// 그래서 한 페이지 안에서만 겹치지 않으면 된다. 아래 [확인]/[취소] 는 니모닉 없이 Enter/Esc 로 누른다.
    /// </summary>
    [Theory]
    [InlineData(UiLanguage.Korean)]
    [InlineData(UiLanguage.English)]
    public void SettingsPages_MnemonicsAreUnique(UiLanguage lang)
    {
        var t = Strings.Table(lang);
        // 모양
        AssertUniqueMnemonics(t, "look.theme", "look.style", "look.placement", "look.size", "look.opacity", "look.hangulColor", "look.englishColor");
        // 표시
        AssertUniqueMnemonics(t, "look.visibility", "look.animate", "look.capsLock", "look.shiftHold", "look.shiftNow", "look.insert",
            "behavior.fullscreen", "behavior.trayState");
        // 일반
        AssertUniqueMnemonics(t, "behavior.autostart", "behavior.hotkey", "behavior.updates", "behavior.language", "behavior.poll",
            "settings.import", "settings.export", "settings.reset");
        // 제외 앱
        AssertUniqueMnemonics(t, "exclude.add");
        // 실험 기능
        AssertUniqueMnemonics(t, "exp.sonar", "exp.sonar.preview", "exp.sonarHotkey", "exp.sonarOnSwitch", "exp.fieldMemory",
            "exp.fieldMemoryClear.button", "exp.focusSteal", "exp.selection", "exp.password");
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
