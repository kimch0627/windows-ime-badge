using System.Collections.Generic;
using Xunit;

namespace ImeBadge.Tests;

/// <summary>VW-01·02: 커서 옆 말풍선의 글자 크기와 색이 어느 테마·밝게/어둡게에서도 또렷한지.</summary>
public sealed class CalloutStyleTests
{
    const int White = unchecked((int)0xFFFFFFFF);

    public static TheoryData<string, bool, CalloutKind> Cases()
    {
        var data = new TheoryData<string, bool, CalloutKind>();
        foreach (var t in DesignThemes.All)
            foreach (bool dark in new[] { false, true })
                foreach (var kind in new[] { CalloutKind.Info, CalloutKind.Warning })
                    data.Add(t.Id, dark, kind);
        return data;
    }

    static CalloutColors ColorsOf(string id, bool dark, CalloutKind kind)
    {
        var t = DesignThemes.Get(id);
        return CalloutStyle.For(kind, dark ? t.Dark : t.Light);
    }

    [Theory]
    [InlineData(1f, 13f)]
    [InlineData(1.25f, 16f)]   // 16.25 → 16
    [InlineData(1.5f, 20f)]    // 19.5 → 20 (반올림은 0 에서 먼 쪽)
    [InlineData(1.75f, 23f)]   // 22.75 → 23
    [InlineData(2f, 26f)]
    public void FontPx_IsWholePixels(float scale, float px) => Assert.Equal(px, CalloutStyle.FontPx(CalloutStyle.DetailSize, scale));

    [Theory]
    [InlineData(1f, 1)]
    [InlineData(1.25f, 1)]
    [InlineData(1.5f, 2)]
    [InlineData(2f, 2)]
    public void BorderWidth_IsWholePixels(float scale, int width) => Assert.Equal(width, CalloutStyle.BorderWidth(scale));

    [Fact]
    public void Text_IsNotSmallerThan13px() => Assert.True(CalloutStyle.DetailSize >= 13f && CalloutStyle.TitleSize >= 13f);

    /// <summary>테두리가 놓일 문서 바탕(밝게 = 흰 문서, 어둡게 = 어두운 편집기)과 3:1 이상. 흰 문서 위 흰 말풍선이 묻히지 않는다.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Border_StandsOutFromTheDocument(string id, bool dark, CalloutKind kind)
    {
        var c = ColorsOf(id, dark, kind);
        int doc = dark ? CalloutStyle.DarkDocument : CalloutStyle.LightDocument;
        double ratio = ColorHex.ContrastRatio(c.Border, doc);
        Assert.True(ratio >= CalloutStyle.BorderContrast, $"{id} dark={dark} {kind}: {ratio:0.00}");
    }

    /// <summary>제목·설명 글자가 말풍선 바탕과 7:1(WCAG AAA) 이상.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Text_IsHighContrast(string id, bool dark, CalloutKind kind)
    {
        var c = ColorsOf(id, dark, kind);
        double title = ColorHex.ContrastRatio(c.Title, c.Fill), detail = ColorHex.ContrastRatio(c.Detail, c.Fill);
        Assert.True(title >= CalloutStyle.DetailContrast, $"{id} dark={dark} {kind} title {title:0.00}");
        Assert.True(detail >= CalloutStyle.DetailContrast, $"{id} dark={dark} {kind} detail {detail:0.00}");
    }

    /// <summary>클래식 정보 말풍선의 설명은 밝게·어둡게 모두 10:1 이상(예전 #5F5F5F 는 흰 바탕에서 6.4).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassicInfoDetail_IsAtLeast10To1(bool dark)
    {
        var c = ColorsOf(DesignThemes.ClassicId, dark, CalloutKind.Info);
        Assert.True(ColorHex.ContrastRatio(c.Detail, c.Fill) >= 10);
    }

    /// <summary>정보 말풍선 바탕은 순백이 아니다(흰 문서와 구별). 색 띠는 테마 강조색.</summary>
    [Fact]
    public void ClassicInfo_IsTintedAndStripedWithAccent()
    {
        var p = DesignThemes.Classic.Light;
        var c = CalloutStyle.For(CalloutKind.Info, p);
        Assert.NotEqual(White, c.Fill);
        Assert.Equal(p.Accent | unchecked((int)0xFF000000), c.Stripe);
    }

    [Theory]
    [InlineData(1f, 4)]
    [InlineData(1.25f, 5)]
    [InlineData(1.5f, 6)]
    public void StripeWidth_ScalesInWholePixels(float scale, int width) => Assert.Equal(width, CalloutStyle.StripeWidth(scale));

    /// <summary>VW-04: 말풍선은 배지 크기와 Windows 텍스트 크기 중 큰 쪽을 따른다(곱하지 않음). 100% 아래로 줄지 않고 225% 를 넘지 않는다.</summary>
    [Theory]
    [InlineData(1f, 100, 100, 1f)]
    [InlineData(1f, 150, 100, 1.5f)]     // 배지를 키우면 말풍선도
    [InlineData(1f, 100, 150, 1.5f)]     // 글자를 키우면 말풍선도
    [InlineData(1f, 150, 150, 1.5f)]     // 둘 다 150% 여도 2.25 배가 되지 않는다
    [InlineData(1f, 50, 100, 1f)]        // 작은 배지에서도 글자는 줄지 않는다
    [InlineData(1f, 300, 100, 2.25f)]    // 아주 큰 배지에서도 225% 까지
    [InlineData(1.5f, 130, 100, 1.95f)]  // 모니터 배율과는 곱한다
    public void Scale_FollowsBadgeSizeOrTextSize(float dpi, int size, int text, float expected) =>
        Assert.Equal(expected, CalloutStyle.Scale(dpi, size, text), 3);
}
