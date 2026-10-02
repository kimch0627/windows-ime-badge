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

    /// <summary>"누르는 즉시 표시": 기다리는 시간 0 이면 누른 것이 보이는 첫 폴링부터, 떼면 바로 돌아온다.</summary>
    [Fact]
    public void ZeroThreshold_CountsImmediately()
    {
        var h = new ShiftHold();
        Assert.True(h.Update(true, 1000, thresholdMs: 0));
        Assert.True(h.Update(true, 1050, thresholdMs: 0));
        Assert.False(h.Update(false, 1100, thresholdMs: 0));
        Assert.True(h.Update(true, 1150, thresholdMs: 0));   // 대문자 한 글자처럼 짧게 눌러도
    }

    [Fact]
    public void SwitchingToImmediate_WhileHeld_CountsAtOnce()
    {
        // 설정을 바꾸는 순간에도 이미 누르고 있던 Shift 를 이어서 잰다.
        var h = new ShiftHold();
        Assert.False(h.Update(true, 1000));
        Assert.True(h.Update(true, 1100, thresholdMs: 0));
        Assert.False(h.Update(true, 1150));                    // 다시 기본으로 돌리면 누른 지 150 ms 라 아직
    }
}
