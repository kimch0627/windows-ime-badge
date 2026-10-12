using Xunit;

namespace ImeBadge.Tests;

/// <summary>실험 기능의 경고 말풍선이 포커스가 벗어나면 바로 내려가는지.</summary>
public sealed class CalloutFocusTests
{
    const long Browser = 100, Field = 101, OtherField = 102, Messenger = 200, MessengerInput = 201;

    static CalloutFocus Password()
    {
        var f = new CalloutFocus();
        f.Pin("password", Browser, Field, needsCaret: true);
        return f;
    }

    static CalloutFocus Steal()
    {
        var f = new CalloutFocus();
        f.Pin("steal", Messenger, focus: 0, needsCaret: false);
        return f;
    }

    [Fact]
    public void Password_StaysWhileTypingInTheField() => Assert.Null(Password().Check(Browser, Field, hasCaret: true));

    [Fact]
    public void Password_DropsWhenTheBadgeHides()   // 입력칸 밖을 눌러 caret 을 잃음: 잇달아 두 번
    {
        var f = Password();
        Assert.Null(f.Check(Browser, Field, hasCaret: false));
        Assert.Equal("password", f.Check(Browser, Field, hasCaret: false));
        Assert.Null(f.Tag);
    }

    /// <summary>한 번 놓친 caret(웹 페이지의 UI Automation 이 잠깐 못 줌)으로는 내리지 않는다. 깜빡이며 다시 뜨지 않게.</summary>
    [Fact]
    public void Password_IgnoresASingleMissedCaret()
    {
        var f = Password();
        Assert.Null(f.Check(Browser, Field, hasCaret: false));
        Assert.Null(f.Check(Browser, Field, hasCaret: true));    // 다시 찾으면 센 것을 잊는다
        Assert.Null(f.Check(Browser, Field, hasCaret: false));
        Assert.Equal("password", f.Tag);
    }

    /// <summary>다른 칸·창으로 옮긴 것은 caret 을 잃은 것과 달리 기다리지 않는다.</summary>
    [Fact]
    public void Password_DropsAtOnceWhenFocusMovesEvenWithoutCaret() =>
        Assert.Equal("password", Password().Check(Browser, OtherField, hasCaret: false));

    [Fact]
    public void Password_DropsWhenFocusMovesToAnotherControl() => Assert.Equal("password", Password().Check(Browser, OtherField, hasCaret: true));

    [Fact]
    public void Password_DropsWhenTheWindowChanges() => Assert.Equal("password", Password().Check(Messenger, MessengerInput, hasCaret: true));

    [Fact]
    public void Steal_StaysWhileTheIntruderIsInFront_EvenWithoutCaretOrWithFocusMoves()
    {
        var f = Steal();
        Assert.Null(f.Check(Messenger, 0, hasCaret: false));               // 입력칸이 없는 창이라 배지가 숨어도
        Assert.Null(f.Check(Messenger, MessengerInput, hasCaret: true));   // 끼어든 창이 스스로 포커스를 옮겨도
        Assert.Equal("steal", f.Tag);
    }

    [Fact]
    public void Steal_DropsWhenTheUserSwitchesWindows() => Assert.Equal("steal", Steal().Check(Browser, Field, hasCaret: true));

    [Fact]
    public void DropsOnlyOnce_AndNewPinReplacesTheOld()
    {
        var f = Password();
        Assert.Equal("password", f.Check(Messenger, 0, hasCaret: false));   // 창이 바뀌면 바로
        Assert.Null(f.Check(Browser, Field, hasCaret: true));   // 이미 잊었다
        f.Pin("password", Browser, Field, needsCaret: true);
        f.Pin("steal", Messenger, 0, needsCaret: false);         // 새 경고가 덮으면 그 자리만 본다
        Assert.Null(f.Check(Messenger, 0, hasCaret: false));
    }

    [Fact]
    public void NothingPinned_NothingToDrop() => Assert.Null(new CalloutFocus().Check(Browser, Field, hasCaret: false));
}
