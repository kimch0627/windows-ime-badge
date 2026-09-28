using Xunit;

namespace ImeBadge.Tests;

public sealed class ShiftHoldTests
{
    [Fact]
    public void ShortPress_DoesNotCount()
    {
        var h = new ShiftHold();
        Assert.False(h.Update(true, 1000));
        Assert.False(h.Update(true, 1000 + ShiftHold.ThresholdMs - 1));
        Assert.False(h.Update(false, 1000 + ShiftHold.ThresholdMs + 50));   // 대문자 한 글자: 임계값 전에 뗌
    }

    [Fact]
    public void HeldPastThreshold_Counts()
    {
        var h = new ShiftHold();
        Assert.False(h.Update(true, 1000));
        Assert.True(h.Update(true, 1000 + ShiftHold.ThresholdMs));
        Assert.True(h.Update(true, 5000));
    }

    [Fact]
    public void Release_Resets_AndNextPressStartsOver()
    {
        var h = new ShiftHold();
        h.Update(true, 1000);
        Assert.True(h.Update(true, 2000));
        Assert.False(h.Update(false, 2100));
        Assert.False(h.Update(true, 2200));                                  // 다시 누른 시각부터 센다
        Assert.False(h.Update(true, 2200 + ShiftHold.ThresholdMs - 1));
        Assert.True(h.Update(true, 2200 + ShiftHold.ThresholdMs));
    }

    [Fact]
    public void NotPressed_IsFalse()
    {
        var h = new ShiftHold();
        Assert.False(h.Update(false, 0));
        Assert.False(h.Update(false, 10_000));
    }
}
