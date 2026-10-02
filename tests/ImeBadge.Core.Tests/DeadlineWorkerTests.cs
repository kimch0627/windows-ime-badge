using System.Threading;
using Xunit;

namespace ImeBadge.Tests;

public sealed class DeadlineWorkerTests
{
    long _now;

    DeadlineWorker<int> New(int waitMs = 50, int abandonAfterMs = 1000, int maxAbandoned = 2) =>
        new("test", waitMs, abandonAfterMs, maxAbandoned, clock: () => Volatile.Read(ref _now));

    static void Eventually(System.Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, 5000));

    [Fact]
    public void FastJob_ReturnsResult()
    {
        var w = New();
        Assert.Equal(DeadlineWorker<int>.Outcome.Done, w.Run(() => 7, out var r));
        Assert.Equal(7, r);
        Assert.Equal(DeadlineWorker<int>.Outcome.Done, w.Run(() => 8, out r));   // 같은 스레드를 다시 쓴다
        Assert.Equal(8, r);
    }

    [Fact]
    public void StuckJob_TimesOut_ThenBusyWithoutWaiting_UntilItFinishes()
    {
        var w = New();
        using var release = new ManualResetEventSlim(false);
        Assert.Equal(DeadlineWorker<int>.Outcome.TimedOut, w.Run(() => { release.Wait(); return 1; }, out _));

        bool ran = false;
        Assert.Equal(DeadlineWorker<int>.Outcome.Busy, w.Run(() => { ran = true; return 2; }, out _));
        Assert.False(ran);                       // 쌓지 않는다

        release.Set();
        Eventually(() => !w.IsBusy);
        Assert.Equal(DeadlineWorker<int>.Outcome.Done, w.Run(() => 3, out var r));
        Assert.Equal(3, r);                      // 늦게 끝난 1 은 버렸다
        Assert.Equal(0, w.AbandonedThreads);
    }

    [Fact]
    public void LongStuckThread_IsAbandoned_AndANewThreadServes()
    {
        var w = New(abandonAfterMs: 1000);
        using var release = new ManualResetEventSlim(false);
        Assert.Equal(DeadlineWorker<int>.Outcome.TimedOut, w.Run(() => { release.Wait(); return 1; }, out _));

        _now = 999;
        Assert.Equal(DeadlineWorker<int>.Outcome.Busy, w.Run(() => 2, out _));
        _now = 1000;
        Assert.Equal(DeadlineWorker<int>.Outcome.Done, w.Run(() => 3, out var r));   // 새 스레드
        Assert.Equal(3, r);
        Assert.Equal(1, w.AbandonedThreads);

        release.Set();
        Eventually(() => w.AbandonedThreads == 0);   // 버린 스레드는 일을 마치고 끝난다
        Assert.Equal(DeadlineWorker<int>.Outcome.Done, w.Run(() => 4, out r));
        Assert.Equal(4, r);
    }

    [Fact]
    public void AbandonedThreads_AreCapped()
    {
        var w = New(abandonAfterMs: 1000, maxAbandoned: 1);
        using var release = new ManualResetEventSlim(false);
        int Stuck() { release.Wait(); return 0; }

        Assert.Equal(DeadlineWorker<int>.Outcome.TimedOut, w.Run(Stuck, out _));
        _now = 2000;
        Assert.Equal(DeadlineWorker<int>.Outcome.TimedOut, w.Run(Stuck, out _));   // 첫 스레드를 버리고 둘째도 묶임
        Assert.Equal(1, w.AbandonedThreads);
        _now = 4000;
        Assert.Equal(DeadlineWorker<int>.Outcome.Busy, w.Run(() => 5, out _));     // 더 버리지 않는다

        release.Set();
        Eventually(() => w.AbandonedThreads == 0 && !w.IsBusy);
        Assert.Equal(DeadlineWorker<int>.Outcome.Done, w.Run(() => 6, out var r));
        Assert.Equal(6, r);
    }

    [Fact]
    public void ThrowingJob_DoesNotKillTheThread()
    {
        var w = New();
        Assert.Equal(DeadlineWorker<int>.Outcome.Done, w.Run(() => throw new System.InvalidOperationException(), out var r));
        Assert.Equal(0, r);
        Assert.Equal(DeadlineWorker<int>.Outcome.Done, w.Run(() => 9, out r));
        Assert.Equal(9, r);
    }
}
