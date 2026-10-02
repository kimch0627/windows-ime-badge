using Xunit;

namespace ImeBadge.Tests;

public sealed class InsertToggleTests
{
    const long A = 0x1001, B = 0x2002;

    [Fact]
    public void StartsInInsertMode_EvenIfWindowsBitIsAlreadyOn()
    {
        // 앱은 늘 삽입 모드로 시작한다. 예전에 다른 앱에서 눌러 둔 토글 비트는 지금 창과 상관없다.
        var t = new InsertToggle();
        Assert.False(t.Update(toggled: true, modifierDown: false, A));
        Assert.False(t.Update(true, false, A));
    }

    [Fact]
    public void Press_FlipsOnlyTheActiveWindow_AndEachWindowKeepsItsOwnState()
    {
        var t = new InsertToggle();
        t.Update(false, false, A);
        Assert.True(t.Update(true, false, A));     // A 에서 Insert → 겹쳐 쓰기
        Assert.False(t.Update(true, false, B));    // B 로 가면 B 의 상태(삽입)
        Assert.True(t.Update(true, false, A));     // A 로 돌아오면 다시 겹쳐 쓰기
        Assert.False(t.Update(false, false, A));   // A 에서 한 번 더 → 삽입
    }

    [Fact]
    public void TwoPressesBetweenPolls_LeaveStateUnchanged()
    {
        // 토글 비트가 제자리면 앱도 두 번 바뀌어 제자리다.
        var t = new InsertToggle();
        t.Update(false, false, A);
        Assert.False(t.Update(false, false, A));
    }

    [Fact]
    public void ShortcutWithModifier_IsIgnored()
    {
        var t = new InsertToggle();
        t.Update(false, false, A);
        Assert.False(t.Update(true, modifierDown: true, A));    // Shift+Insert(붙여넣기): 보조키를 아직 누르고 있다
        Assert.False(t.Update(true, false, A));                 // 보조키를 뗌. 토글 비트는 그대로
        Assert.True(t.Update(false, false, A));                 // 그다음 Insert 만 누르면 센다
    }

    [Fact]
    public void ModifierSeenAtPreviousPoll_IsAlsoAShortcut()
    {
        // Ctrl+Insert(복사)를 치고 다음 폴링 전에 Ctrl 을 뗐다: 직전 폴링에서 Ctrl 이 보였으면 단축키로 본다.
        var t = new InsertToggle();
        t.Update(false, modifierDown: true, A);
        Assert.False(t.Update(true, modifierDown: false, A));
        Assert.True(t.Update(false, false, A));    // 그다음 그냥 Insert 는 센다
    }

    [Fact]
    public void NoActiveWindow_IsIgnored()
    {
        var t = new InsertToggle();
        t.Update(false, false, 0);
        Assert.False(t.Update(true, false, 0));
        Assert.Equal(0, t.Count);
    }

    [Fact]
    public void Forget_DropsClosedWindows()
    {
        var t = new InsertToggle();
        t.Update(false, false, A);
        t.Update(true, false, A);
        t.Update(true, false, B);
        t.Update(false, false, B);
        Assert.Equal(2, t.Count);
        t.Forget(w => w == A);                     // A 가 닫힘
        Assert.Equal(1, t.Count);
        Assert.False(t.Update(false, false, A));   // 같은 HWND 값의 새 창은 삽입 모드
        Assert.True(t.Update(false, false, B));
    }
}
