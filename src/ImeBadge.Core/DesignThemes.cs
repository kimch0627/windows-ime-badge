using System;
using System.Collections.Generic;

namespace ImeBadge;

/// <summary>
/// 배지의 마감(질감). 클래식은 흰/검 글자와 검은 그림자(Flat), 새 테마는 부드럽게(Soft) 그린다.
/// Soft: 흰 글자가 잘 안 읽히는 밝은 색이면 글자를 검정 대신 "배지 색을 진하게 만든 색"으로, 그림자도 배지 색조로.
/// 테두리(같은 색조로 어둡게 한 선)·안쪽 위 림·글자 가운데 정렬은 마감과 상관없이 모든 테마에 같다.
/// </summary>
public enum BadgeFinish { Flat, Soft }

/// <summary>
/// 설정 창·메뉴의 색 한 벌(디자인 토큰). ARGB 정수(System.Drawing 없이 쓰려고). 앱 쪽 Theme.Palette 로 옮겨 쓴다.
/// Window: 창 바탕 · Card: 카드 표면 · Input: 입력칸 · Elevated: 보조 버튼 표면 · Border: 테두리 · Track: 슬라이더 트랙 ·
/// Text/SubtleText: 글자 · Accent 계열: 강조색과 그 위의 글자색.
/// </summary>
public sealed record ThemePalette(
    bool Dark, int Window, int Card, int Input, int Elevated, int Border, int Track,
    int Text, int SubtleText, int Link, int Hover,
    int Accent, int AccentHover, int AccentPressed, int OnAccent);

/// <summary>
/// 디자인 테마 하나. 배지 기본색 두 개, 마감, 설정 창의 밝게/어둡게 팔레트, 색 견본 11개를 함께 가진다.
/// 비유하면 벽지·조명·소품을 한 세트로 묶은 인테리어 패키지다. 집 구조(기능)는 테마와 상관없이 같다.
/// </summary>
/// <param name="Id">설정 파일에 저장되는 이름("classic"). 바꾸면 옛 설정이 클래식으로 돌아가므로 바꾸지 않는다.</param>
/// <param name="Gloss">위쪽 그라데이션의 세기. 0 이면 그라데이션 없음. 배지 위쪽을 이만큼 흰색 쪽으로 섞는다.
/// 반짝임은 모든 테마에 있는 안쪽 위 림이 맡고, 이 값은 젤리 같은 부드러움만 더한다.</param>
/// <param name="CornerRadius">설정 창 카드 모서리 반경(px, 96 DPI 기준).</param>
public sealed record DesignTheme(
    string Id, string HangulColor, string EnglishColor, BadgeFinish Finish, float Gloss, int CornerRadius,
    ThemePalette Light, ThemePalette Dark, IReadOnlyList<string> Swatches);

/// <summary>디자인 테마 목록. 첫 번째(클래식)가 기본값이며 색은 예전 그대로다.</summary>
public static class DesignThemes
{
    public const string ClassicId = "classic";

    static int C(string hex) => ColorHex.TryParse(hex, out int v) ? v : throw new ArgumentException(hex);

    /// <summary>강조색 위에 마우스를 올렸을·눌렀을 때의 색. 밝은 창에서는 흰색 쪽으로, 어두운 창에서는 검은색 쪽으로 조금씩 옮긴다.</summary>
    static ThemePalette P(bool dark, string window, string card, string input, string elevated, string border, string track,
        string text, string subtle, string hover, string accent, string onAccent)
    {
        int a = C(accent), toward = dark ? C("#000000") : C("#FFFFFF");
        return new(dark, C(window), C(card), C(input), C(elevated), C(border), C(track), C(text), C(subtle),
            Link: a, Hover: C(hover), Accent: a, AccentHover: ColorHex.Mix(a, toward, 0.1), AccentPressed: ColorHex.Mix(a, toward, 0.2), OnAccent: C(onAccent));
    }

    /// <summary>클래식: Windows 11 설정 앱과 같은 색. 예전 Theme.Palette 값을 그대로 옮겼다(바꾸면 기존 사용자의 모습이 바뀐다).</summary>
    public static readonly DesignTheme Classic = new(ClassicId, Settings.DefaultHangulColor, Settings.DefaultEnglishColor, BadgeFinish.Flat, 0f, 8,
        Light: new(false, C("#F3F3F3"), C("#FFFFFF"), C("#FBFBFB"), C("#FBFBFB"), C("#E0E0E0"), C("#8A8A8A"),
            C("#1B1B1B"), C("#5F5F5F"), C("#005FB8"), C("#ECECEC"), C("#0067C0"), C("#1975C5"), C("#3283CA"), C("#FFFFFF")),
        Dark: new(true, C("#202020"), C("#2B2B2B"), C("#1F1F1F"), C("#373737"), C("#3F3F3F"), C("#9E9E9E"),
            C("#FFFFFF"), C("#B0B0B0"), C("#4CC2FF"), C("#3A3A3A"), C("#4CC2FF"), C("#47B1E8"), C("#42A1D2"), C("#000000")),
        Swatches: new[] { "#0067C0", "#0099BC", "#00B294", "#10893E", "#8E8CD8", "#744DA9", "#E3008C", "#E74856", "#CA5010", "#FFB900", "#3C3C3C" });

    /// <summary>벚꽃: 파스텔 핑크·라벤더, 은은한 광택. 라벤더는 핑크와 밝기가 달라 흑백·색약으로도 구별되게 조금 진하게(#B6A4F0 → #8E7CE0).</summary>
    public static readonly DesignTheme Blossom = new("blossom", "#F29CBF", "#8E7CE0", BadgeFinish.Soft, 0.12f, 12,
        Light: P(false, "#FFF6F9", "#FFFFFF", "#FFFBFD", "#FFFBFD", "#F2DCE5", "#B99AA7", "#2E1F26", "#7D6470", "#FCEBF2", "#C93A72", "#FFFFFF"),
        Dark: P(true, "#241B1F", "#2F2429", "#20181B", "#3B2E34", "#4B3A42", "#A58D98", "#FFF4F8", "#CDB3BE", "#40313A", "#F5A3C4", "#3B0F24"),
        Swatches: new[] { "#F29CBF", "#F7B2A1", "#FFD49A", "#C3E5A8", "#9EDDD2", "#A9C8F5", "#8E7CE0", "#DDA8EC", "#F4A0A8", "#D6BFAE", "#8D7F9C" });

    /// <summary>캔디: 코랄·블루, 젤리 같은 강한 광택. 블루는 코랄과 밝기가 달라 흑백·색약으로도 구별되게 진하게(#4DAEF0 → #1C74C9).</summary>
    public static readonly DesignTheme Candy = new("candy", "#FF6B81", "#1C74C9", BadgeFinish.Soft, 0.21f, 12,
        Light: P(false, "#FFF8F4", "#FFFFFF", "#FFFCFA", "#FFFCFA", "#FFE0D5", "#C4A39A", "#2A1E24", "#7A6069", "#FFEDE6", "#D4304D", "#FFFFFF"),
        Dark: P(true, "#1E1A22", "#2A2430", "#1B171E", "#36303C", "#463E4D", "#A197A8", "#FFF6F2", "#C8B8C2", "#3A3240", "#FF8A9C", "#3A0A14"),
        Swatches: new[] { "#FF6B81", "#FF9A57", "#FFD23F", "#7ED957", "#2EC4B6", "#1C74C9", "#6C8CFF", "#B36BFF", "#FF78C4", "#FF5A5F", "#3D3D5C" });

    /// <summary>민트초코: 상큼한 민트 + 달콤한 초콜릿.</summary>
    public static readonly DesignTheme Mint = new("mint", "#7FD8C4", "#6B4A3A", BadgeFinish.Soft, 0.1f, 12,
        Light: P(false, "#F3FBF8", "#FFFFFF", "#F9FDFC", "#F9FDFC", "#D5EDE6", "#8FB5AA", "#1F2A27", "#5C6F69", "#E6F6F1", "#2E7D6B", "#FFFFFF"),
        Dark: P(true, "#1C1917", "#26211E", "#1A1715", "#322B27", "#40372F", "#9C8F86", "#F5FBF9", "#BDB3AB", "#352D28", "#86E0CB", "#0F2E27"),
        Swatches: new[] { "#7FD8C4", "#A8E6CF", "#5BBFA9", "#9AD1E8", "#C7E9B0", "#FFD3B6", "#F6C6D0", "#D9C2F0", "#C9A27E", "#8B5E3C", "#6B4A3A" });

    /// <summary>미드나잇: 밤하늘 네이비 + 골드.</summary>
    public static readonly DesignTheme Midnight = new("midnight", "#F2C75C", "#3A4E8C", BadgeFinish.Soft, 0.09f, 12,
        Light: P(false, "#F4F5FA", "#FFFFFF", "#FAFBFD", "#FAFBFD", "#DDE1EE", "#8C93AD", "#161B2E", "#5A6078", "#EBEEF7", "#2E3F75", "#FFFFFF"),
        Dark: P(true, "#0F1424", "#182038", "#0C101D", "#212A45", "#2C3654", "#8F98B8", "#F3F4FA", "#AEB5CE", "#232C48", "#F2C75C", "#2A1F00"),
        Swatches: new[] { "#F2C75C", "#E8A65C", "#E07A7A", "#C58BE0", "#8FA3F0", "#5C7CE0", "#3A4E8C", "#4FB3BF", "#7FC8A9", "#C0C4D6", "#1F2640" });

    /// <summary>설정 창·트레이 메뉴에 보이는 순서.</summary>
    public static readonly IReadOnlyList<DesignTheme> All = new[] { Classic, Blossom, Candy, Mint, Midnight };

    /// <summary>id 로 찾는다. 모르는 id(손으로 고친 파일, 새 버전에서 만든 테마를 구버전에서 읽음)는 클래식.</summary>
    public static DesignTheme Get(string? id)
    {
        foreach (var t in All)
            if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) return t;
        return Classic;
    }

    public static bool IsKnown(string? id)
    {
        foreach (var t in All)
            if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

/// <summary>
/// 캐릭터 배지 모양. 설정 파일에는 문자열로 저장한다(<see cref="Settings.Character"/>).
/// <see cref="BadgeStyle"/> 에 값을 더하지 않은 이유: 구버전은 모르는 enum 이름을 읽으면 설정 파일 전체를 버린다.
/// 문자열 필드는 구버전이 그냥 무시하므로, 되돌려도 나머지 설정은 남고 배지는 둥근 모양으로 보인다.
/// </summary>
public static class BadgeCharacters
{
    public const string None = "", Cat = "cat", Dog = "dog", Heart = "heart", Cloud = "cloud", Star = "star";

    /// <summary>설정 창·트레이 메뉴에 보이는 순서.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Cat, Dog, Heart, Cloud, Star };

    /// <summary>알려진 캐릭터면 그 이름(소문자), 아니면 <see cref="None"/>.</summary>
    public static string Normalize(string? id)
    {
        foreach (var c in All)
            if (string.Equals(c, id, StringComparison.OrdinalIgnoreCase)) return c;
        return None;
    }
}

/// <summary>
/// 배지에 쓸 글자. Caps Lock 표시가 켜져 있으면(기본) 글자로 대소문자를 구별한다:
/// 한글은 평소 "한", Caps Lock 이 켜지면 쌍자음 "꺆"(평소와 다르다는 것이 한눈에 보이게), 영문은 소문자 "a" / 대문자 "A".
/// Caps Lock 이 켜져 있으면 글자 아래에 짧은 밑줄도 긋는다(언어 공통). 트레이 아이콘은 이 규칙을 쓰지 않고 항상 "한"/"A" 다.
/// 설정을 끄면 예전처럼 항상 "한"/"A", 밑줄 없음.
/// </summary>
public static class BadgeText
{
    public const string Hangul = "한", HangulCaps = "꺆", EnglishLower = "a", EnglishUpper = "A";

    /// <param name="korean">한글 입력 상태면 true, 영문이면 false.</param>
    /// <param name="capsLock">Caps Lock 이 켜져 있는가.</param>
    /// <param name="showCapsLock">설정 "Caps Lock 표시".</param>
    public static (string Text, bool CapsBar) For(bool korean, bool capsLock, bool showCapsLock)
    {
        if (!showCapsLock) return (korean ? Hangul : EnglishUpper, false);
        if (korean) return (capsLock ? HangulCaps : Hangul, capsLock);
        return (capsLock ? EnglishUpper : EnglishLower, capsLock);
    }
}
