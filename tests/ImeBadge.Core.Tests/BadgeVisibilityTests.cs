using Xunit;

namespace ImeBadge.Tests;

/// <summary>표시 방식별 배지 짙기: 늘 / 타이핑 중 옅게 / 바뀔 때만.</summary>
public sealed class BadgeVisibilityTests
{
    const long Never = long.MinValue, Now = 1_000_000;

    [Theory]
    [InlineData(null, BadgeVisibility.Always)]
    [InlineData("", BadgeVisibility.Always)]
    [InlineData("ONCHANGE", BadgeVisibility.OnChange)]
    [InlineData("dimWhileTyping", BadgeVisibility.DimWhileTyping)]
    [InlineData("hidden", BadgeVisibility.Always)]
    public void Normalize(string? input, string expected) => Assert.Equal(expected, BadgeVisibility.Normalize(input));

    [Fact]
    public void Always_IsAlwaysOpaque()
    {
        Assert.Equal(1f, BadgeVisibility.Factor(BadgeVisibility.Always, Now, Now, Never));
        Assert.Equal(1f, BadgeVisibility.Factor(BadgeVisibility.Always, Now, Never, Never));
    }

    [Fact]
    public void DimWhileTyping_DimsRightAfterTyping_ThenRampsBack()
    {
        const string m = BadgeVisibility.DimWhileTyping;
        Assert.Equal(1f, BadgeVisibility.Factor(m, Now, Never, Never));                    // 아직 안 쳤다
        Assert.Equal(BadgeVisibility.TypingFactor, BadgeVisibility.Factor(m, Now, Now - 100, Never));
        Assert.Equal(BadgeVisibility.TypingFactor, BadgeVisibility.Factor(m, Now, Now - (BadgeVisibility.TypingQuietMs - 1), Never));
        float mid = BadgeVisibility.Factor(m, Now, Now - BadgeVisibility.TypingQuietMs - BadgeVisibility.RampMs / 2, Never);
        Assert.InRange(mid, BadgeVisibility.TypingFactor + 0.01f, 0.99f);                  // 서서히 돌아온다
        Assert.Equal(1f, BadgeVisibility.Factor(m, Now, Now - BadgeVisibility.TypingQuietMs - BadgeVisibility.RampMs, Never));
    }

    [Fact]
    public void DimWhileTyping_StaysOpaqueRightAfterAChange()
    {
        // 한/영을 바꾸자마자 치기 시작해도 바뀐 것은 또렷하게 보여야 한다.
        Assert.Equal(1f, BadgeVisibility.Factor(BadgeVisibility.DimWhileTyping, Now, Now - 50, Now - 300));
        Assert.Equal(BadgeVisibility.TypingFactor,
            BadgeVisibility.Factor(BadgeVisibility.DimWhileTyping, Now, Now - 50, Now - BadgeVisibility.ShowMs));
    }

    [Fact]
    public void OnChange_ShowsThenFadesOut()
    {
        const string m = BadgeVisibility.OnChange;
        Assert.Equal(0f, BadgeVisibility.Factor(m, Now, Never, Never));                    // 바뀐 적이 없으면 숨김
        Assert.Equal(1f, BadgeVisibility.Factor(m, Now, Never, Now));
        Assert.Equal(1f, BadgeVisibility.Factor(m, Now, Never, Now - (BadgeVisibility.ShowMs - 1)));
        float mid = BadgeVisibility.Factor(m, Now, Never, Now - BadgeVisibility.ShowMs - BadgeVisibility.FadeOutMs / 2);
        Assert.InRange(mid, 0.01f, 0.99f);
        Assert.Equal(0f, BadgeVisibility.Factor(m, Now, Never, Now - BadgeVisibility.ShowMs - BadgeVisibility.FadeOutMs));
        Assert.Equal(1f, BadgeVisibility.Factor(m, Now, Now, Now - 100));                  // 타이핑은 상관없다
    }
}
