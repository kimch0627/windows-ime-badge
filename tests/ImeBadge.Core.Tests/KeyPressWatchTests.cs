using Xunit;

namespace ImeBadge.Tests;

public sealed class KeyPressWatchTests
{
    [Fact]
    public void ToggleBitFlip_IsAPress_FirstReadingIsNot()
    {
        var w = new KeyPressWatch();
        Assert.False(w.Update(toggled: false, down: false, 0));
        Assert.True(w.Update(toggled: true, down: false, 50));     // 두 폴링 사이에 눌렀다 뗐다
        Assert.False(w.Update(toggled: true, down: false, 100));
        Assert.True(w.Update(toggled: false, down: false, 150));
    }

    [Fact]
    public void HeldKey_CountsOnce_EvenBeforeTheToggleBitFollows()
    {
        var w = new KeyPressWatch();
        w.Update(false, false, 0);
        Assert.True(w.Update(toggled: false, down: true, 50));     // 눌림이 먼저 보인다
        Assert.False(w.Update(toggled: false, down: true, 100));   // 누르고 있는 동안은 다시 세지 않는다
        Assert.True(w.Update(toggled: true, down: false, 150));    // 토글 비트가 뒤늦게 따라온다(같은 누름이어도 "방금 눌렀다" 가 이어질 뿐)
    }

    [Fact]
    public void OwnKey_IsNotCountedOnce()
    {
        var w = new KeyPressWatch();
        w.Update(false, false, 0);
        w.SentOwn(10);
        Assert.False(w.Update(toggled: true, down: false, 50));    // 이 프로그램이 보낸 키
        Assert.True(w.Update(toggled: false, down: false, 100));   // 그 다음은 사용자
    }

    [Fact]
    public void OwnKey_IsForgottenWhenItNeverShows()
    {
        var w = new KeyPressWatch();
        w.Update(false, false, 0);
        w.SentOwn(10);
        Assert.False(w.Update(false, false, 50));
        Assert.False(w.Update(false, false, 10 + KeyPressWatch.OwnKeyMs + 1));
        Assert.True(w.Update(true, false, 700));                   // 사용자가 누른 것을 빼먹지 않는다
    }

    [Fact]
    public void Reset_StartsOver()
    {
        var w = new KeyPressWatch();
        w.Update(false, false, 0);
        w.Reset();
        Assert.False(w.Update(true, false, 50));                   // 꺼 둔 사이 바뀐 것은 세지 않는다
    }
}
