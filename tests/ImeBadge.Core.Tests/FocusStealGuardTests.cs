using Xunit;

namespace ImeBadge.Tests;

public sealed class FocusStealGuardTests
{
    const long Editor = 100, Popup = 200, Never = long.MinValue;
    const long T = 1_000_000;   // 창이 바뀐 시각

    static FocusStealGuard Armed()
    {
        var g = new FocusStealGuard();
        Assert.True(g.OnForegroundChanged(T, Popup, Editor, lastTypingMs: T - 150, lastUserSwitchMs: Never));
        return g;
    }

    [Fact]
    public void TypingContinuesIntoAPoppedUpWindow_Warns()
    {
        var g = Armed();
        Assert.False(g.Check(T + 50, Popup, lastInputMs: T - 150, pointerUsed: false));   // 아직 새 입력 없음
        Assert.True(g.Check(T + 150, Popup, lastInputMs: T + 120, pointerUsed: false));
        Assert.False(g.Armed);
        Assert.False(g.Check(T + 250, Popup, lastInputMs: T + 220, pointerUsed: false));   // 한 번만
    }

    [Fact]
    public void NoInputAfterTheSwitch_StaysQuiet()
    {
        var g = Armed();
        Assert.False(g.Check(T + 400, Popup, T - 150, false));
        Assert.False(g.Check(T + FocusStealGuard.WatchMs + 1, Popup, T - 150, false));
        Assert.False(g.Armed);
    }

    [Fact]
    public void InputOnlyAfterTheWatchWindow_IsDeliberate()
    {
        var g = Armed();
        Assert.False(g.Check(T + 900, Popup, lastInputMs: T + 850, pointerUsed: false));   // 새 창을 보고 치기 시작했다
    }

    [Fact]
    public void InputWithinGrace_IsTheSwitchItself()
    {
        var g = Armed();
        Assert.False(g.Check(T + 30, Popup, lastInputMs: T + 10, pointerUsed: false));
        Assert.True(g.Armed);
    }

    [Fact]
    public void UserSwitchedWithMouseOrKeys_NotArmed()
    {
        var g = new FocusStealGuard();
        Assert.False(g.OnForegroundChanged(T, Popup, Editor, T - 150, lastUserSwitchMs: T - 300));   // Alt+Tab, 클릭
        Assert.False(g.Armed);
    }

    [Fact]
    public void NotTypingBeforeTheSwitch_NotArmed()
    {
        var g = new FocusStealGuard();
        Assert.False(g.OnForegroundChanged(T, Popup, Editor, lastTypingMs: T - 5000, Never));
        Assert.False(g.OnForegroundChanged(T, Popup, Editor, lastTypingMs: Never, Never));
        Assert.False(g.OnForegroundChanged(T, Editor, Editor, T - 100, Never));   // 같은 창
        Assert.False(g.OnForegroundChanged(T, 0, Editor, T - 100, Never));        // 활성 창 없음(잠금 화면 등)
    }

    [Fact]
    public void MouseUseOrAnotherSwitch_Disarms()
    {
        var g = Armed();
        Assert.False(g.Check(T + 100, Popup, T + 90, pointerUsed: true));
        Assert.False(g.Armed);

        g = Armed();
        Assert.False(g.Check(T + 100, Editor, T + 90, pointerUsed: false));   // 사용자가 이미 돌아갔다
        Assert.False(g.Armed);
    }
}
