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
    public void Glow_PeaksNearTheEnd_AndStillFrameShowsSomething()
    {
        Assert.True(Sonar.Glow(0.72f) > 0.95f);
        Assert.True(Sonar.Glow(0.9f) > 0f && Sonar.Glow(0.9f) < 1f);
        // 애니메이션을 끈 사용자에게 보여 주는 한 장: 커서 가까운 원과 빛이 함께 보여야 한다.
        var (r, a) = Sonar.Ring(1, Sonar.StillT, Start, End);
        Assert.True(a > 0.3f);
        Assert.True(r < Start / 2);
        Assert.True(Sonar.Glow(Sonar.StillT) > 0.5f);
    }
}
