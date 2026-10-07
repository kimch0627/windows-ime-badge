using Xunit;

namespace ImeBadge.Tests;

public sealed class ImeModeKeeperTests
{
    const long Notepad = 1, Chrome = 2, Codex = 3;
    const bool Han = true, Eng = false;

    static bool? See(ImeModeKeeper k, long window, bool? hangul, long now, bool canSwitch = true, bool learn = true) =>
        k.Observe(window, hangul, canSwitch, learn, now);

    [Fact]
    public void ModeChosenInOneApp_IsCarriedToTheNext()
    {
        var k = new ImeModeKeeper();
        Assert.Null(See(k, Notepad, Eng, 0));      // 처음 본 모드를 기억
        Assert.Null(See(k, Notepad, Han, 100));    // 메모장에서 한/영 키 → 한글을 기억
        Assert.Equal(Han, k.Desired);
        Assert.Equal(Han, See(k, Chrome, Eng, 200));   // 크롬은 영문 그대로 → 한글로 바꾸라고
        Assert.Null(See(k, Chrome, Han, 300));     // 바뀌었다
        Assert.Null(See(k, Chrome, Han, 400));
        Assert.Equal(Han, k.Desired);
    }

    [Fact]
    public void SameModeInTheNextApp_DoesNothing()
    {
        var k = new ImeModeKeeper();
        See(k, Notepad, Han, 0);
        Assert.Null(See(k, Chrome, Han, 100));
    }

    [Fact]
    public void TogglingAfterArrival_BecomesTheNewMode()
    {
        var k = new ImeModeKeeper();
        See(k, Notepad, Han, 0);
        See(k, Chrome, Eng, 100);                  // 바꾸라고 알림
        See(k, Chrome, Han, 200);                  // 맞춰짐
        Assert.Null(See(k, Chrome, Eng, 5000));    // 크롬에서 사용자가 영문으로
        Assert.Equal(Eng, k.Desired);
        Assert.Equal(Eng, See(k, Codex, Han, 6000));   // Codex 는 한글이었다 → 영문으로
    }

    [Fact]
    public void WaitsForATextField_ThenSwitches()
    {
        var k = new ImeModeKeeper();
        See(k, Notepad, Han, 0);
        Assert.Null(See(k, Chrome, Eng, 100, canSwitch: false));   // 입력칸이 아직 없다(페이지를 보는 중)
        Assert.Null(See(k, Chrome, Eng, 900, canSwitch: false));
        Assert.Equal(Han, See(k, Chrome, Eng, 1500, canSwitch: true));
    }

    [Fact]
    public void UserTogglesBeforeAField_IsNotOverridden()
    {
        var k = new ImeModeKeeper();
        See(k, Notepad, Eng, 0);
        See(k, Chrome, Han, 100, canSwitch: false);   // 크롬은 한글, 입력칸 없음
        See(k, Chrome, Eng, 400, canSwitch: false);   // 사용자가 한/영 → 영문(기억과 같아짐)
        See(k, Chrome, Han, 800, canSwitch: false);   // 다시 한글로: 이제는 사용자 선택
        Assert.Equal(Han, k.Desired);
        Assert.Null(See(k, Chrome, Han, 1200, canSwitch: true));   // 입력칸을 눌러도 되돌리지 않는다
    }

    [Fact]
    public void RetriesOnce_ThenGivesUp()
    {
        var k = new ImeModeKeeper();
        See(k, Notepad, Han, 0);
        Assert.Equal(Han, See(k, Chrome, Eng, 100));
        Assert.Equal(1, k.Attempts);
        Assert.Null(See(k, Chrome, Eng, 100 + ImeModeKeeper.RetryMs - 1));   // 결과를 기다린다
        Assert.Equal(Han, See(k, Chrome, Eng, 100 + ImeModeKeeper.RetryMs));  // 두 번째 방법
        Assert.Equal(2, k.Attempts);
        Assert.Null(See(k, Chrome, Eng, 2000));                                // 더는 하지 않는다
        Assert.Equal(Han, k.Desired);                                          // 바꾸지 못한 것을 사용자 선택으로 보지 않는다
        Assert.Equal(Han, See(k, Codex, Eng, 3000));                           // 다른 창에서는 다시 시도
    }

    [Fact]
    public void NotSent_TriesAgainNextTime_GiveUp_Stops()
    {
        var k = new ImeModeKeeper();
        See(k, Notepad, Han, 0);
        Assert.Equal(Han, See(k, Chrome, Eng, 100));
        k.NotSent();                                 // Alt 를 아직 누르고 있었다
        Assert.Equal(Han, See(k, Chrome, Eng, 150));
        Assert.Equal(1, k.Attempts);
        k.GiveUp();                                  // 한/영 키를 보냈다: 되풀이하지 않는다
        Assert.Null(See(k, Chrome, Eng, 1000));
    }

    [Fact]
    public void PasswordField_DoesNotTeachTheMode()
    {
        var k = new ImeModeKeeper();
        See(k, Notepad, Han, 0);
        See(k, Chrome, Han, 100);
        Assert.Null(See(k, Chrome, Eng, 500, learn: false));   // 비밀번호 칸에서 영문으로
        Assert.Equal(Han, k.Desired);
    }

    [Fact]
    public void OtherLanguageOrExcludedApp_IsLeftAlone_AndResetForgets()
    {
        var k = new ImeModeKeeper();
        See(k, Notepad, Han, 0);
        Assert.Null(See(k, Chrome, null, 100));    // 일본어 IME, 제외 앱 등
        Assert.Null(See(k, Chrome, null, 1000));
        k.Reset();
        Assert.Null(k.Desired);
        Assert.Null(See(k, Codex, Eng, 2000));     // 다시 처음 본 모드를 기억
        Assert.Equal(Eng, k.Desired);
    }
}
