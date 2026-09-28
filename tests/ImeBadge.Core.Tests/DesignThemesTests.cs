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

    [Fact]
    public void Classic_OtherColor_IsTheOldOrange() => Assert.Equal("#FF8C00", DesignThemes.Classic.OtherColor);

    /// <summary>"?" 배지 글자도 읽혀야 한다. 클래식은 예전 흰/검 규칙이라 검사하지 않는다.</summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void SoftTheme_OtherColor_IsReadableAndInSwatches(string id)
    {
        var t = DesignThemes.Get(id);
        if (t.Finish != BadgeFinish.Soft) return;
        Assert.Contains(t.OtherColor, t.Swatches);
        ColorHex.TryParse(t.OtherColor, out int fill);
        double ratio = ColorHex.ContrastRatio(fill, ColorHex.SoftTextOn(fill));
        Assert.True(ratio >= 4.5, $"{id} {t.OtherColor}: {ratio:0.00}");
    }

    /// <summary>"?" 배지가 한글·영문 배지와 헷갈리지 않게: 색상(hue)이 40° 이상 떨어지거나 밝기 대비가 2:1 이상.</summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void OtherColor_IsDistinctFromHangulAndEnglish(string id)
    {
        var t = DesignThemes.Get(id);
        ColorHex.TryParse(t.OtherColor, out int other);
        foreach (var hex in new[] { t.HangulColor, t.EnglishColor })
        {
            ColorHex.TryParse(hex, out int c);
            bool hueApart = Saturation(c) > 0.1 && HueDistance(other, c) >= 40;
            bool lumApart = ColorHex.ContrastRatio(other, c) >= 2.0;
            Assert.True(hueApart || lumApart, $"{id}: {t.OtherColor} vs {hex}");
        }
    }

    static (double r, double g, double b) Rgb(int argb) =>
        (((argb >> 16) & 0xFF) / 255.0, ((argb >> 8) & 0xFF) / 255.0, (argb & 0xFF) / 255.0);

    static double Saturation(int argb)
    {
        var (r, g, b) = Rgb(argb);
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max == 0 ? 0 : (max - min) / max;
    }

    static double Hue(int argb)
    {
        var (r, g, b) = Rgb(argb);
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d == 0) return 0;
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    static double HueDistance(int a, int b)
    {
        double d = Math.Abs(Hue(a) - Hue(b)) % 360;
        return Math.Min(d, 360 - d);
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

    /// <summary>진한 색조 글자는 4.5 에 딱 맞추지 않고 여유(6:1)를 둔다. 흰 글자를 쓰는 색(4.5 이상)은 그대로, 6 이 불가능하면 검정.</summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void SoftTheme_DarkInk_HasMarginOverAA(string id)
    {
        var t = DesignThemes.Get(id);
        if (t.Finish != BadgeFinish.Soft) return;
        foreach (var hex in t.Swatches)
        {
            ColorHex.TryParse(hex, out int fill);
            int ink = ColorHex.SoftTextOn(fill);
            if (ink == unchecked((int)0xFFFFFFFF)) continue;
            // 중간 톤(#5C7CE0 등)은 검정으로도 6 에 못 미친다. 그때는 검정(낼 수 있는 가장 큰 대비).
            double best = Math.Min(ColorHex.SoftInkContrast, ColorHex.ContrastRatio(fill, unchecked((int)0xFF000000)));
            double ratio = ColorHex.ContrastRatio(fill, ink);
            Assert.True(ratio >= best - 1e-9, $"{id} {hex}: {ratio:0.00}");
        }
    }

    static int InkOn(DesignTheme t, int fill) =>
        t.Finish == BadgeFinish.Soft ? ColorHex.SoftTextOn(fill)
            : ColorHex.PrefersWhiteText(fill) ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFF000000);

    /// <summary>
    /// 불투명도를 낮춰도(30~100%) 글자 둘레(헤일로)가 흰 문서·검은 편집기 어느 쪽 위에서든 글자와 4.5:1 이상.
    /// 예전(고정 짙기)에는 캔디 한글 55% 가 어두운 배경에서 3.3:1, 30% 에서 2.7:1 이었다.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Halo_KeepsTextReadable_AtAnyOpacity_OnWhiteAndBlack(string id)
    {
        const int White = unchecked((int)0xFFFFFFFF), Black = unchecked((int)0xFF000000);
        var t = DesignThemes.Get(id);
        foreach (var hex in new[] { t.HangulColor, t.EnglishColor, t.OtherColor })
        {
            ColorHex.TryParse(hex, out int fill);
            int ink = InkOn(t, fill);
            for (int op = 30; op <= 100; op += 5)
            {
                int alpha = 255 * op / 100;
                int halo = ColorHex.HaloAlpha(fill, ink, alpha);
                Assert.InRange(halo, alpha, 255);
                foreach (int bg in new[] { White, Black })
                {
                    double ratio = ColorHex.ContrastRatio(ink, ColorHex.Over(fill, halo, bg));
                    Assert.True(ratio >= ColorHex.HaloContrast, $"{id} {hex} {op}% on {ColorHex.ToHex(bg)}: {ratio:0.00}");
                }
            }
        }
    }

    [Fact]
    public void HaloAlpha_StaysLow_WhenContrastIsAlreadyEnough()
    {
        // 불투명(255)이면 그대로, 흰 바탕 위 검은 글자처럼 넉넉하면 배지 알파에서 크게 올리지 않는다.
        Assert.Equal(255, ColorHex.HaloAlpha(unchecked((int)0xFFFFFFFF), unchecked((int)0xFF000000), 255));
        Assert.True(ColorHex.HaloAlpha(unchecked((int)0xFF3C3C3C), unchecked((int)0xFFFFFFFF), 200) < 230);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("CAT", "cat")]
    [InlineData("dragon", "")]
    public void Characters_Normalize(string? input, string expected) => Assert.Equal(expected, BadgeCharacters.Normalize(input));
}

/// <summary>배지 글자 규칙: 한/꺆, a/A, Caps Lock 이면 밑줄, Shift 를 누르고 있으면 반대 대소문자와 ▲. 설정을 끄면 예전처럼 한/A.</summary>
public sealed class BadgeTextTests
{
    [Theory]
    [InlineData(true, false, "한", BadgeMark.None)]
    [InlineData(true, true, "꺆", BadgeMark.CapsBar)]
    [InlineData(false, false, "a", BadgeMark.None)]
    [InlineData(false, true, "A", BadgeMark.CapsBar)]
    public void ShowCapsLock_On(bool korean, bool caps, string text, BadgeMark mark) =>
        Assert.Equal((text, mark), BadgeText.For(korean, caps, showCapsLock: true));

    [Theory]
    [InlineData(true, false, "한")]
    [InlineData(true, true, "한")]
    [InlineData(false, false, "A")]
    [InlineData(false, true, "A")]
    public void ShowCapsLock_Off_KeepsOldLetters(bool korean, bool caps, string text) =>
        Assert.Equal((text, BadgeMark.None), BadgeText.For(korean, caps, showCapsLock: false));

    /// <summary>Shift 는 지금 입력될 글자를 보여 준다: Caps Lock 이 꺼져 있으면 대문자, 켜져 있으면 소문자. 표시는 늘 ▲.</summary>
    [Theory]
    [InlineData(true, false, "꺆")]
    [InlineData(true, true, "한")]
    [InlineData(false, false, "A")]
    [InlineData(false, true, "a")]
    public void ShiftHeld_FlipsCase_WithShiftMark(bool korean, bool caps, string text) =>
        Assert.Equal((text, BadgeMark.Shift), BadgeText.For(korean, caps, showCapsLock: true, shift: true));

    [Theory]
    [InlineData(true, false, "한")]
    [InlineData(true, true, "한")]
    [InlineData(false, false, "A")]
    [InlineData(false, true, "A")]
    public void ShiftHeld_IgnoredWhenCapsLockDisplayOff(bool korean, bool caps, string text) =>
        Assert.Equal((text, BadgeMark.None), BadgeText.For(korean, caps, showCapsLock: false, shift: true));
}
