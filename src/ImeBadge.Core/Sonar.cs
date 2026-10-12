using System;

namespace ImeBadge;

/// <summary>
/// 커서 소나(실험 기능)의 움직임. 원 두 개가 커서 둘레의 큰 원에서 커서 쪽으로 좁혀 들고, 끝에 커서 자리가 잠깐 빛난다.
/// 물에 돌을 던졌을 때 퍼지는 물결을 거꾸로 돌린 모습이다: 바깥에서 시작해 한 점(커서)으로 모인다.
/// 그리기(GDI+)는 앱 쪽 <c>OverlayRenderer</c> 가 하고, 여기서는 시각 t(0~1)마다 원의 반지름·짙기와 빛의 짙기만 정한다(단위 테스트 가능).
/// </summary>
public static class Sonar
{
    /// <summary>한 번 재생하는 시간(ms).</summary>
    public const int DurationMs = 800;

    /// <summary>원이 출발하는 반지름(배율 1 기준 px). 배지와 글자 몇 줄을 둘러쌀 만큼 크게 잡아 눈이 먼저 원을 찾게 한다.</summary>
    public const float StartRadius = 80f;

    /// <summary>원의 개수.</summary>
    public const int Rings = 2;

    /// <summary>원·끝 윤곽 둘레에 까는 짝 색(흰색 또는 같은 색조의 진한 색)의 알파. 짝 색이 바탕과 3:1 이 되기에 넉넉하다(테스트).</summary>
    public const int PartnerAlpha = 230;

    /// <summary>
    /// 소나 배율(VW-04): 모니터 배율 × 배지 크기(100~200%). 배지를 키운 사용자에게는 원도 굵고 크게. 작게 줄인 배지에서도 원은 줄이지 않고
    /// (커서를 찾는 것이 목적), 아주 크게 키운 배지(300%)에서도 원이 화면을 덮지 않게 2배까지만. 글자가 없으므로 텍스트 크기와는 상관없다.
    /// </summary>
    public static float Scale(float dpi, int sizePercent) => dpi * Math.Clamp(sizePercent, 100, 200) / 100f;

    /// <summary>뒤 원이 앞 원보다 늦게 출발하는 시간(전체에 대한 비율).</summary>
    public const float RingDelay = 0.2f;

    /// <summary>원 하나가 좁혀 드는 데 쓰는 시간(전체에 대한 비율). 뒤 원이 끝나는 0.82 뒤로는 커서 자리의 빛만 남는다.</summary>
    public const float RingSpan = 0.62f;

    /// <summary>
    /// 애니메이션 효과를 끈 사용자에게 보여 줄 멈춘 한 장의 시각. 뒤 원이 커서 둘레 가까이 있고 빛도 켜져 있다.
    /// 멈춘 한 장은 <c>still: true</c> 로 그려 반투명 없이 또렷하게 보인다(VW-03).
    /// </summary>
    public const float StillT = 0.6f;

    /// <summary>원이 사라지기 시작하는 진행도(0~1). 커서 가까이 거의 다 와서야 옅어져, 멈춘 채 서서히 사라지는 잔상이 없다.</summary>
    public const float FadeFrom = 0.85f;

    /// <summary>원이 출발할 때 짙어지는 데 걸리는 진행도.</summary>
    const float FadeIn = 0.08f;

    /// <summary>
    /// <paramref name="ring"/> 번째 원의 반지름(px)과 짙기(0~1). 아직 출발하지 않았거나 다 좁혀 든 뒤면 짙기 0.
    /// 처음에 빠르게, 커서 가까이에서 천천히 움직인다(ease-out, 2차). 출발할 때 잠깐(8%) 짙어지고, 커서에 거의 닿은 마지막 15% 에서 옅어진다.
    /// 예전(3차 ease-out, 70% 부터 옅어짐)에는 원이 커서 옆에 멈춘 채 오래 반투명하게 남아 흐린 잔상처럼 보였다.
    /// <paramref name="still"/> 이면 멈춘 한 장: 보이는 원은 짙기 1, 커서에 거의 닿아 빛과 겹치는 원(<see cref="FadeFrom"/> 넘음)은 그리지 않는다.
    /// </summary>
    public static (float Radius, float Alpha) Ring(int ring, float t, float startRadius, float endRadius, bool still = false)
    {
        float p = (t - ring * RingDelay) / RingSpan;
        if (p <= 0f) return (startRadius, 0f);
        if (p >= 1f) return (endRadius, 0f);
        float eased = 1f - (1f - p) * (1f - p);
        float radius = startRadius + (endRadius - startRadius) * eased;
        if (still) return (radius, p <= FadeFrom ? 1f : 0f);
        float alpha = Math.Min(1f, p / FadeIn) * (p > FadeFrom ? 1f - (p - FadeFrom) / (1f - FadeFrom) : 1f);
        return (radius, alpha);
    }

    /// <summary>
    /// 끝에 커서 자리를 감싸는 윤곽의 짙기(0~1). 뒤 원이 거의 다 좁혀 들 때 켜졌다가 끝에서 사라진다. <paramref name="still"/> 이면 켜져 있는 동안 1.
    /// </summary>
    public static float Glow(float t, bool still = false)
    {
        const float rise = 0.55f, peak = 0.72f;
        if (t <= rise || t >= 1f) return 0f;
        if (still) return 1f;
        if (t < peak) return (t - rise) / (peak - rise);
        return 1f - (t - peak) / (1f - peak);
    }
}
