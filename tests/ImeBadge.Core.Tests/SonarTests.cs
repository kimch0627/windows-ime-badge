using Xunit;

namespace ImeBadge.Tests;

public sealed class SonarTests
{
    const float Start = 80, End = 12;

    [Fact]
    public void Rings_CloseInOnTheCaret()
    {
        float last = float.MaxValue;
        for (float t = 0.01f; t < Sonar.RingSpan; t += 0.05f)
        {
            var (r, a) = Sonar.Ring(0, t, Start, End);
            Assert.True(r < last, $"t={t}: {r} >= {last}");   // 좁혀 들기만 한다
            Assert.InRange(r, End, Start);
            Assert.InRange(a, 0f, 1f);
            last = r;
        }
    }

    [Fact]
    public void SecondRing_StartsLater_AndAllEndInvisible()
    {
        Assert.Equal(0f, Sonar.Ring(1, Sonar.RingDelay / 2, Start, End).Alpha);   // 아직 출발 전
        Assert.True(Sonar.Ring(1, Sonar.RingDelay + 0.1f, Start, End).Alpha > 0f);
        for (int i = 0; i < Sonar.Rings; i++) Assert.Equal(0f, Sonar.Ring(i, 1f, Start, End).Alpha);
        Assert.Equal(0f, Sonar.Glow(1f));
        Assert.Equal(0f, Sonar.Glow(0f));
    }

    [Fact]
    public void Glow_PeaksNearTheEnd()
    {
        Assert.True(Sonar.Glow(0.72f) > 0.95f);
        Assert.True(Sonar.Glow(0.9f) > 0f && Sonar.Glow(0.9f) < 1f);
    }

    /// <summary>애니메이션을 끈 사용자에게 보여 주는 한 장(VW-03): 커서 둘레의 원 하나와 빛이 반투명 없이 또렷하다.</summary>
    [Fact]
    public void StillFrame_IsOpaque()
    {
        var (r, a) = Sonar.Ring(1, Sonar.StillT, Start, End, still: true);
        Assert.Equal(1f, a);
        Assert.InRange(r, End + 2, Start / 2);   // 커서 둘레, 빛과 겹치지 않을 만큼 떨어져
        Assert.Equal(0f, Sonar.Ring(0, Sonar.StillT, Start, End, still: true).Alpha);   // 커서에 거의 닿은 앞 원은 빛과 겹쳐 그리지 않는다
        Assert.Equal(1f, Sonar.Glow(Sonar.StillT, still: true));
    }

    /// <summary>
    /// VW-03: 원이 커서에 거의 다 와서(남은 거리 2% 안) 사라지기까지가 전체 재생 시간의 10% 이하. 멈춘 채 반투명하게 남는 잔상이 짧다.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Ring_DoesNotLingerAtTheCaret(int ring)
    {
        float arrived = float.NaN, gone = float.NaN;
        for (float t = 0; t <= 1.0001f; t += 0.001f)
        {
            var (r, a) = Sonar.Ring(ring, t, Start, End);
            float p = (t - ring * Sonar.RingDelay) / Sonar.RingSpan;
            if (p <= 0) continue;
            if (float.IsNaN(arrived) && (r - End) / (Start - End) <= 0.02f) arrived = t;
            if (!float.IsNaN(arrived) && a == 0f) { gone = t; break; }
        }
        Assert.False(float.IsNaN(gone));
        Assert.True(gone - arrived <= 0.1f, $"ring {ring}: {gone - arrived:0.000}");
    }

    /// <summary>출발할 때 금방(재생 시간의 5% 안에) 또렷해진다.</summary>
    [Fact]
    public void Ring_IsOpaqueSoonAfterStart() =>
        Assert.Equal(1f, Sonar.Ring(0, 0.05f, Start, End).Alpha);

    /// <summary>VW-04: 소나는 배지 크기를 따르되 100~200% 로 묶는다(작은 배지에서도 원은 그대로, 300% 배지에서도 2배까지).</summary>
    [Theory]
    [InlineData(1f, 100, 1f)]
    [InlineData(1f, 150, 1.5f)]
    [InlineData(1f, 50, 1f)]
    [InlineData(1f, 300, 2f)]
    [InlineData(1.25f, 130, 1.625f)]
    public void Scale_FollowsBadgeSize(float dpi, int size, float expected) => Assert.Equal(expected, Sonar.Scale(dpi, size), 3);

    /// <summary>
    /// VW-03: 밝은 배지색(금색·민트·분홍·코랄)의 원은 짝 색(같은 색조의 진한 색, <see cref="Sonar.PartnerAlpha"/>)이 흰 문서 위에서 3:1 이상이라
    /// 원 윤곽이 보인다. 어두운 바탕에서는 배지색 자체가 3:1 이상이다.
    /// </summary>
    [Theory]
    [InlineData("#F2C75C")]
    [InlineData("#7FD8C4")]
    [InlineData("#F29CBF")]
    [InlineData("#FF6B81")]
    public void LightRing_IsOutlinedOnWhite(string hex)
    {
        ColorHex.TryParse(hex, out int fill);
        int white = ColorHex.LightDocumentBackground;
        Assert.True(ColorHex.OuterShadeAlpha(fill) > 0);
        Assert.True(ColorHex.ContrastRatio(ColorHex.Over(ColorHex.OuterShadeColor(fill), Sonar.PartnerAlpha, white), white) >= ColorHex.OutlineContrast);
        Assert.True(ColorHex.ContrastRatio(fill, ColorHex.DarkEditorBackground) >= ColorHex.OutlineContrast);
    }
}
