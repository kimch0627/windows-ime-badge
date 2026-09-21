using System;

namespace ImeBadge;

/// <summary>
/// 트레이 메뉴와 설정 창이 함께 쓰는 문구·프리셋. 한곳에 두어 두 화면의 표현이 어긋나지 않게 한다.
/// </summary>
static class Labels
{
    public static readonly (string label, BadgeStyle value)[] Styles =
    {
        ("사각 배지  [한]", BadgeStyle.Box),
        ("둥근 배지  (한)", BadgeStyle.Pill),
        ("점  ●", BadgeStyle.Dot),
        ("밑줄  ▬", BadgeStyle.Underline),
        ("점, 바뀔 때 1.5초 글자", BadgeStyle.DotFlash),
    };

    public static readonly (string label, BadgePlacement value)[] Placements =
    {
        ("커서 오른쪽 위", BadgePlacement.AboveRight),
        ("커서 오른쪽 아래", BadgePlacement.BelowRight),
        ("커서 왼쪽 위", BadgePlacement.AboveLeft),
        ("커서 왼쪽 아래", BadgePlacement.BelowLeft),
    };

    /// <summary>트레이 메뉴의 크기 프리셋. 설정 창에서는 50~300% 어느 값이든 고를 수 있다.</summary>
    public static readonly (string label, int pct)[] SizePresets =
    {
        ("작게 (80%)", 80), ("보통 (100%)", 100), ("크게 (130%)", 130), ("아주 크게 (160%)", 160),
    };

    /// <summary>트레이 메뉴의 불투명도 프리셋.</summary>
    public static readonly (string label, int pct)[] OpacityPresets =
    {
        ("불투명 (100%)", 100), ("살짝 비침 (85%)", 85), ("반투명 (70%)", 70), ("많이 비침 (50%)", 50),
    };

    public static string Of(BadgeStyle s) => Array.Find(Styles, x => x.value == s).label ?? s.ToString();
    public static string Of(BadgePlacement p) => Array.Find(Placements, x => x.value == p).label ?? p.ToString();
}
