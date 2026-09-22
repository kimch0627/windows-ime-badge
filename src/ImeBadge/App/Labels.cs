using System;

namespace ImeBadge;

/// <summary>
/// 트레이 메뉴와 설정 창이 함께 쓰는 문구·프리셋. 한곳에 두어 두 화면의 표현이 어긋나지 않게 한다.
/// 문구는 현재 UI 언어(<see cref="Strings"/>)를 따르므로 배열을 저장해 두지 말고 쓸 때마다 읽는다.
/// </summary>
static class Labels
{
    public static (string label, BadgeStyle value)[] Styles => new[]
    {
        (Strings.Get("style.box"), BadgeStyle.Box),
        (Strings.Get("style.pill"), BadgeStyle.Pill),
        (Strings.Get("style.dot"), BadgeStyle.Dot),
        (Strings.Get("style.underline"), BadgeStyle.Underline),
        (Strings.Get("style.dotFlash"), BadgeStyle.DotFlash),
    };

    public static (string label, BadgePlacement value)[] Placements => new[]
    {
        (Strings.Get("place.aboveRight"), BadgePlacement.AboveRight),
        (Strings.Get("place.belowRight"), BadgePlacement.BelowRight),
        (Strings.Get("place.aboveLeft"), BadgePlacement.AboveLeft),
        (Strings.Get("place.belowLeft"), BadgePlacement.BelowLeft),
    };

    /// <summary>트레이 메뉴의 크기 프리셋. 설정 창에서는 50~300% 어느 값이든 고를 수 있다.</summary>
    public static (string label, int pct)[] SizePresets => new[]
    {
        (Strings.Get("size.small"), 80), (Strings.Get("size.normal"), 100), (Strings.Get("size.large"), 130), (Strings.Get("size.xlarge"), 160),
    };

    /// <summary>트레이 메뉴의 불투명도 프리셋.</summary>
    public static (string label, int pct)[] OpacityPresets => new[]
    {
        (Strings.Get("opacity.100"), 100), (Strings.Get("opacity.85"), 85), (Strings.Get("opacity.70"), 70), (Strings.Get("opacity.50"), 50),
    };

    /// <summary>설정 창의 언어 선택지.</summary>
    public static (string label, UiLanguage value)[] Languages => new[]
    {
        (Strings.Get("language.auto"), UiLanguage.Auto),
        (Strings.Get("language.ko"), UiLanguage.Korean),
        (Strings.Get("language.en"), UiLanguage.English),
    };

    public static string Of(BadgeStyle s) => Array.Find(Styles, x => x.value == s).label ?? s.ToString();
    public static string Of(BadgePlacement p) => Array.Find(Placements, x => x.value == p).label ?? p.ToString();
}
