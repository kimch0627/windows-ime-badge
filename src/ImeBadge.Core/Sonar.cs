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

    /// <summary>뒤 원이 앞 원보다 늦게 출발하는 시간(전체에 대한 비율).</summary>
    public const float RingDelay = 0.2f;

    /// <summary>원 하나가 좁혀 드는 데 쓰는 시간(전체에 대한 비율). 뒤 원이 끝나는 0.82 뒤로는 커서 자리의 빛만 남는다.</summary>
    public const float RingSpan = 0.62f;

    /// <summary>애니메이션 효과를 끈 사용자에게 보여 줄 멈춘 한 장의 시각. 뒤 원이 커서 가까이 있고 빛도 켜져 있다.</summary>
    public const float StillT = 0.7f;

    /// <summary>
    /// <paramref name="ring"/> 번째 원의 반지름(px)과 짙기(0~1). 아직 출발하지 않았거나 다 좁혀 든 뒤면 짙기 0.
    /// 처음에 빠르게, 커서 가까이에서 천천히 움직인다(ease-out). 출발할 때 잠깐 짙어지고 끝나기 전에 옅어진다.
    /// </summary>
    public static (float Radius, float Alpha) Ring(int ring, float t, float startRadius, float endRadius)
    {
        float p = (t - ring * RingDelay) / RingSpan;
        if (p <= 0f) return (startRadius, 0f);
        if (p >= 1f) return (endRadius, 0f);
        float eased = 1f - (1f - p) * (1f - p) * (1f - p);
        float radius = startRadius + (endRadius - startRadius) * eased;
        float alpha = Math.Min(1f, p / 0.15f) * (p > 0.7f ? 1f - (p - 0.7f) / 0.3f : 1f);
        return (radius, alpha);
    }

    /// <summary>끝에 커서 자리가 빛나는 짙기(0~1). 뒤 원이 거의 다 좁혀 들 때 켜졌다가 끝에서 사라진다.</summary>
    public static float Glow(float t)
    {
        const float rise = 0.55f, peak = 0.72f;
        if (t <= rise || t >= 1f) return 0f;
        if (t < peak) return (t - rise) / (peak - rise);
        return 1f - (t - peak) / (1f - peak);
    }
}
