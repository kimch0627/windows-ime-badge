using System;
using System.Linq;
using Xunit;

namespace ImeBadge.Tests;

/// <summary>테마 목록이 앞뒤가 맞는지, 그리고 어느 테마·어느 견본 색에서도 배지 글자가 읽히는지.</summary>
public sealed class DesignThemesTests
{
    public static TheoryData<string> ThemeIds()
    {
        var data = new TheoryData<string>();
        foreach (var t in DesignThemes.All) data.Add(t.Id);
        return data;
    }

    [Fact]
    public void Classic_IsFirst_AndMatchesOldDefaults()
    {
        Assert.Same(DesignThemes.Classic, DesignThemes.All[0]);
        Assert.Equal(Settings.DefaultHangulColor, DesignThemes.Classic.HangulColor);
        Assert.Equal(Settings.DefaultEnglishColor, DesignThemes.Classic.EnglishColor);
        Assert.Equal(BadgeFinish.Flat, DesignThemes.Classic.Finish);
        Assert.Equal(DesignThemes.ClassicId, new Settings().Theme);
    }

    [Fact]
    public void Ids_AreUniqueLowercase()
    {
        var ids = DesignThemes.All.Select(t => t.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.Equal(id.ToLowerInvariant(), id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rose")]   // 시안에만 있었던 테마
    public void Get_Unknown_ReturnsClassic(string? id) => Assert.Same(DesignThemes.Classic, DesignThemes.Get(id));

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Theme_HasElevenSwatches_IncludingItsDefaults(string id)
    {
        var t = DesignThemes.Get(id);
        Assert.Equal(11, t.Swatches.Count);
        Assert.All(t.Swatches, c => Assert.True(ColorHex.TryParse(c, out _), c));
        Assert.Contains(t.HangulColor, t.Swatches);    // 기본색이 견본에 없으면 "사용자 지정" 으로 보인다
        Assert.Contains(t.EnglishColor, t.Swatches);
    }

    /// <summary>테마가 고르는 글자색이 모든 견본에서 WCAG AA(4.5:1)를 넘는다. 클래식은 예전 흰/검 규칙이라 검사하지 않는다.</summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void SoftTheme_TextIsReadableOnEverySwatch(string id)
    {
        var t = DesignThemes.Get(id);
        if (t.Finish != BadgeFinish.Soft) return;
        foreach (var hex in t.Swatches)
        {
            ColorHex.TryParse(hex, out int fill);
            double ratio = ColorHex.ContrastRatio(fill, ColorHex.SoftTextOn(fill));
            Assert.True(ratio >= 4.5, $"{id} {hex}: {ratio:0.00}");
        }
    }

    /// <summary>
    /// 한/영 기본색은 색상(hue)만이 아니라 밝기도 달라야 한다. 휘도 대비 1.5 이상이면 흑백으로 보거나 색약이어도 두 배지가 구별된다.
    /// (벚꽃 핑크·라벤더, 캔디 코랄·하늘색은 한때 1.08·1.12 로 밝기가 거의 같았다.)
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Defaults_HangulAndEnglish_DifferInLightness(string id)
    {
        var t = DesignThemes.Get(id);
        ColorHex.TryParse(t.HangulColor, out int ko);
        ColorHex.TryParse(t.EnglishColor, out int en);
        double ratio = ColorHex.ContrastRatio(ko, en);
        Assert.True(ratio >= 1.5, $"{id}: {ratio:0.00}");
    }

    /// <summary>테두리: 모든 견본에서 테두리가 배지색과 구별된다(Flat 1.6, Soft 1.4 이상).</summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void EdgeOn_StandsOutOnEverySwatch(string id)
    {
        var t = DesignThemes.Get(id);
        bool soft = t.Finish == BadgeFinish.Soft;
        foreach (var hex in t.Swatches)
        {
            ColorHex.TryParse(hex, out int fill);
            double ratio = ColorHex.ContrastRatio(fill, ColorHex.EdgeOn(fill, soft));
            Assert.True(ratio >= (soft ? 1.4 : 1.6), $"{id} {hex}: {ratio:0.00}");
        }
    }

    [Fact]
    public void EdgeOn_DarkensMidTones_LightensNearBlack()
    {
        ColorHex.TryParse("#0067C0", out int blue);
        int edge = ColorHex.EdgeOn(blue, soft: false);
        Assert.True(ColorHex.RelativeLuminance(edge) < ColorHex.RelativeLuminance(blue));
        Assert.True((edge & 0xFF) > ((edge >> 16) & 0xFF));   // 파랑 색조가 남는다

        ColorHex.TryParse("#3C3C3C", out int gray);
        Assert.True(ColorHex.RelativeLuminance(ColorHex.EdgeOn(gray, soft: false)) > ColorHex.RelativeLuminance(gray));
    }

    /// <summary>설정 창의 확인 버튼(강조색 위 글자)도 읽혀야 한다.</summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void AccentButtonText_IsReadable(string id)
    {
        var t = DesignThemes.Get(id);
        foreach (var p in new[] { t.Light, t.Dark })
            Assert.True(ColorHex.ContrastRatio(p.Accent, p.OnAccent) >= 4.5, $"{id} dark={p.Dark}");
    }

    [Fact]
    public void SoftTextOn_KeepsWhiteWhenReadable_OtherwiseDarkTint()
    {
        Assert.Equal(unchecked((int)0xFFFFFFFF), ColorHex.SoftTextOn(unchecked((int)0xFF3A4E8C)));   // 네이비 → 흰 글자
        ColorHex.TryParse("#F29CBF", out int pink);
        int text = ColorHex.SoftTextOn(pink);
        Assert.NotEqual(unchecked((int)0xFF000000), text);                                         // 검정이 아니라
        Assert.True(((text >> 16) & 0xFF) > (text & 0xFF) - 40);                                    // 핑크 색조가 남은 진한 색
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("CAT", "cat")]
    [InlineData("dragon", "")]
    public void Characters_Normalize(string? input, string expected) => Assert.Equal(expected, BadgeCharacters.Normalize(input));
}

/// <summary>배지 글자 규칙: 한/꺆, a/A, Caps Lock 이면 밑줄. 설정을 끄면 예전처럼 한/A.</summary>
public sealed class BadgeTextTests
{
    [Theory]
    [InlineData(true, false, "한", false)]
    [InlineData(true, true, "꺆", true)]
    [InlineData(false, false, "a", false)]
    [InlineData(false, true, "A", true)]
    public void ShowCapsLock_On(bool korean, bool caps, string text, bool bar) =>
        Assert.Equal((text, bar), BadgeText.For(korean, caps, showCapsLock: true));

    [Theory]
    [InlineData(true, false, "한")]
    [InlineData(true, true, "한")]
    [InlineData(false, false, "A")]
    [InlineData(false, true, "A")]
    public void ShowCapsLock_Off_KeepsOldLetters(bool korean, bool caps, string text) =>
        Assert.Equal((text, false), BadgeText.For(korean, caps, showCapsLock: false));
}
