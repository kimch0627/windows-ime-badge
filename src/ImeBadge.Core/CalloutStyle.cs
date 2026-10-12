using System;

namespace ImeBadge;

/// <summary>말풍선의 종류. 색과 기본 아이콘이 다르다(Windows 11 InfoBar 의 정보·주의 색).</summary>
public enum CalloutKind { Info, Warning }

/// <summary>말풍선 색(ARGB): 바탕, 테두리, 왼쪽 색 띠, 제목, 설명, 아이콘.</summary>
public readonly record struct CalloutColors(int Fill, int Border, int Stripe, int Title, int Detail, int Icon);

/// <summary>
/// 커서 옆 말풍선(실험 기능의 안내·경고)의 색과 글자 크기 규칙. 그리기(GDI+)는 앱 쪽 <c>OverlayRenderer</c> 가 하고, 여기서는
/// 테스트할 수 있는 값만 정한다(VW-01·02).
/// <list type="bullet">
/// <item>말풍선은 문서 위에 잠깐 뜬다. 밝게 모드면 대개 흰 문서, 어둡게 모드면 어두운 편집기 위라고 보고, 그 바탕과 테두리가
/// <see cref="BorderContrast"/> 이상 차이 나게 한다. 흰 문서 위 흰 말풍선이 그림자만 남기고 묻히던 것을 막는다.</item>
/// <item>정보 말풍선은 카드색에 강조색을 조금 섞어 순백과 구별하고, 왼쪽 색 띠(강조색)로 종류를 알린다. 주의는 InfoBar 의 주의 색 그대로.</item>
/// <item>설명 글자는 설정 창의 보조 글자색이 아니라 본문색에 가깝게(대비 <see cref="DetailContrast"/> 이상). 잠깐 보이는 알림의 작은 글자는 옅으면 흐리게 읽힌다.</item>
/// </list>
/// </summary>
public static class CalloutStyle
{
    const int White = unchecked((int)0xFFFFFFFF), Black = unchecked((int)0xFF000000);

    /// <summary>밝게 모드에서 말풍선이 놓인다고 보는 바탕(흰 문서).</summary>
    public const int LightDocument = White;

    /// <summary>어둡게 모드에서 말풍선이 놓인다고 보는 바탕(어두운 편집기).</summary>
    public const int DarkDocument = ColorHex.DarkEditorBackground;

    /// <summary>테두리와 문서 바탕의 최소 대비(WCAG 비텍스트 대비 기준과 같은 3:1).</summary>
    public const double BorderContrast = 3.0;

    /// <summary>설명 글자와 말풍선 바탕의 최소 대비(WCAG AAA 본문 기준 7:1).</summary>
    public const double DetailContrast = 7.0;

    /// <summary>설명 글자를 옅게 할 때 지키려는 대비. 이만큼 안 되면 옅게 하지 않고 제목색을 그대로 쓴다.</summary>
    const double DetailTarget = 10.0;

    /// <summary>제목·설명 글자 크기(배율 1 기준 px). 맑은 고딕 Regular 12px 는 획이 1px 에 못 미쳐 번져 보여서 13px 로.</summary>
    public const float TitleSize = 13f, DetailSize = 13f;

    /// <summary>
    /// 배율을 곱한 글자 크기를 정수 px 로. 소수 px(125% 의 16.25px)는 힌팅(격자 맞춤)이 줄마다 달라져 획 굵기가 고르지 않다.
    /// </summary>
    public static float FontPx(float size, float scale) => MathF.Max(1f, MathF.Round(size * scale, MidpointRounding.AwayFromZero));

    /// <summary>왼쪽 색 띠 폭(px). 배율 1 에서 4px, 정수.</summary>
    public static int StripeWidth(float scale) => Math.Max(2, (int)MathF.Round(4 * scale, MidpointRounding.AwayFromZero));

    /// <summary>테두리 굵기(px). 정수로 맞춰야 125% 에서 1.25px 펜이 2px 짜리 반투명 선으로 번지지 않는다.</summary>
    public static int BorderWidth(float scale) => Math.Max(1, (int)MathF.Round(scale, MidpointRounding.AwayFromZero));

    /// <summary>Windows 접근성 "텍스트 크기"의 범위(%). 설정 앱의 슬라이더가 100~225 다.</summary>
    public const int MaxTextScale = 225;

    /// <summary>
    /// 말풍선 배율(VW-04): 모니터 배율 × (배지 크기와 Windows 텍스트 크기 중 큰 쪽). 배지를 키운 사용자·글자를 키운 사용자 모두에게 말풍선이
    /// 함께 커진다. 둘을 곱하지 않는 것은 150% × 150% 처럼 겹쳐 지나치게 커지지 않게, 100% 아래로 줄이지 않는 것은 배지를 작게 쓰는 사용자에게도
    /// 안내 글자는 읽혀야 해서다. 위로는 텍스트 크기의 최대(225%)까지.
    /// </summary>
    public static float Scale(float dpi, int sizePercent, int textScalePercent) =>
        dpi * Math.Clamp(Math.Max(sizePercent, textScalePercent), 100, MaxTextScale) / 100f;

    /// <summary>테마 팔레트(설정 창 색 한 벌)로 정한 말풍선 색. 고대비 모드는 앱 쪽에서 시스템 색을 그대로 쓴다.</summary>
    public static CalloutColors For(CalloutKind kind, ThemePalette p)
    {
        bool dark = p.Dark;
        int fill, title, detail, icon;
        if (kind == CalloutKind.Warning)
        {
            // Windows 11 InfoBar 의 주의(Caution) 색.
            fill = dark ? unchecked((int)0xFF433519) : unchecked((int)0xFFFFF4CE);
            title = dark ? White : unchecked((int)0xFF1A1A1A);
            detail = dark ? unchecked((int)0xFFE3DCC6) : unchecked((int)0xFF4A4A4A);
            icon = dark ? unchecked((int)0xFFFCE100) : unchecked((int)0xFF9D5D00);
        }
        else
        {
            fill = ColorHex.Mix(Opaque(p.Card), Opaque(p.Accent), dark ? 0.08 : 0.04);
            title = Opaque(p.Text);
            detail = DetailOn(fill, title);
            icon = Opaque(p.Accent);
        }
        int border = BorderOn(fill, dark ? DarkDocument : LightDocument, dark ? White : Black);
        return new(fill, border, icon, title, detail, icon);
    }

    /// <summary>
    /// 설명색: 제목색을 바탕 쪽으로 조금 옅게(12%) 해 제목과 위계를 두되, 대비가 <see cref="DetailTarget"/>(10:1)에 못 미치면 덜 옅게.
    /// </summary>
    static int DetailOn(int fill, int title)
    {
        for (int step = 6; step >= 0; step--)   // 12%, 10%, ... 0%
        {
            int c = ColorHex.Mix(title, fill, step * 0.02);
            if (ColorHex.ContrastRatio(c, fill) >= DetailTarget) return c;
        }
        return title;
    }

    /// <summary>테두리: 바탕을 <paramref name="toward"/>(밝게 = 검정, 어둡게 = 흰색) 쪽으로 섞어 문서 바탕과 3:1 이 되는 가장 옅은 색.</summary>
    static int BorderOn(int fill, int document, int toward)
    {
        for (int step = 7; step <= 45; step++)   // 14% ... 90%
        {
            int c = ColorHex.Mix(fill, toward, step * 0.02);
            if (ColorHex.ContrastRatio(c, document) >= BorderContrast) return c;
        }
        return ColorHex.Mix(fill, toward, 0.9);
    }

    static int Opaque(int argb) => argb | Black;
}
