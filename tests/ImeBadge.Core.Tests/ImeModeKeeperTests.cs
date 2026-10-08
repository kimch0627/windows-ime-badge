using Xunit;

namespace ImeBadge.Tests;

public sealed class ImeModeKeeperTests
{
    const long Notepad = 1, Chrome = 2, Codex = 3, Xshell = 4;
    const long XshellEdit = 41, XshellMain = 42, XshellToolbar = 43;   // Xshell 안의 칸들(입력칸, 메인 창, 도구 모음)
    const bool Han = true, Eng = false;
    const long Later = ImeModeKeeper.ArrivalGraceMs + 500;               // 창에 들어오고 한참 뒤(사용자가 한/영 키를 누를 때)

    // 칸을 따로 주지 않으면 창마다 칸 하나(창 핸들과 같은 값)로 본다.
    static bool? See(ImeModeKeeper k, long window, bool? hangul, long now, bool canSwitch = true, bool learn = true, long focus = -1) =>
        k.Observe(window, focus < 0 ? window : focus, hangul, canSwitch, learn, now);

    [Fact]
    public void ModeChosenInOneApp_IsCarriedToTheNext()
    {
        var k = new ImeModeKeeper();
        Assert.Null(See(k, Notepad, Eng, 0));          // 처음 본 모드를 기억
        Assert.Null(See(k, Notepad, Han, Later));      // 메모장에서 한/영 키 → 한글을 기억
        Assert.Equal(Han, k.Desired);
        Assert.Equal(Han, See(k, Chrome, Eng, Later + 100));   // 크롬은 영문 그대로 → 한글로 바꾸라고
        Assert.Null(See(k, Chrome, Han, Later + 200));         // 바뀌었다
        Assert.Null(See(k, Chrome, Han, Later + 300));
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
        See(k, Chrome, Han, 100, canSwitch: false);           // 크롬은 한글, 입력칸 없음
        See(k, Chrome, Eng, 100 + Later, canSwitch: false);   // 사용자가 한/영 → 영문(기억과 같아짐)
        See(k, Chrome, Han, 600 + Later, canSwitch: false);   // 다시 한글로: 이제는 사용자 선택
        Assert.Equal(Han, k.Desired);
        Assert.Null(See(k, Chrome, Han, 1000 + Later, canSwitch: true));   // 입력칸을 눌러도 되돌리지 않는다
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
        Assert.Null(See(k, Chrome, Eng, 100 + Later, canSwitch: false, learn: false));   // 비밀번호 칸에서 영문으로
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

    // 실제 로그(Chrome → Alt+Tab → Xshell): 포커스가 없음(영문) → 입력칸(한글) → 메인 창(영문, 입력칸 아님) → 입력칸(영문).
    // 예전에는 입력칸이 한글로 맞춰진 뒤 메인 창의 영문을 한/영 키로 보고 영문을 기억해 크롬까지 영문으로 바꿨다.
    [Fact]
    public void FocusBouncingInsideAnApp_IsNotAToggle()
    {
        var k = new ImeModeKeeper();
        See(k, Chrome, Han, 0);
        long t = 10_000;
        Assert.Null(See(k, Xshell, Eng, t, canSwitch: false, focus: 0));
        Assert.Null(See(k, Xshell, Han, t + 10, focus: XshellEdit));
        Assert.Null(See(k, Xshell, Eng, t + 106, canSwitch: false, focus: XshellMain));
        Assert.Equal(Han, k.Desired);
        Assert.Equal(Han, See(k, Xshell, Eng, t + 119, focus: XshellEdit));   // 입력칸이 영문으로 돌아왔다 → 한글로
        Assert.Null(See(k, Xshell, Han, t + 150, focus: XshellEdit));
        Assert.Equal(Han, k.Desired);
        Assert.Null(See(k, Chrome, Han, t + 5000));                            // 크롬으로 돌아가도 한글 그대로
    }

    // 실제 로그: 도구 모음 등을 거쳐 입력칸이 한글로 잡힌 뒤 47ms 만에 Xshell 이 스스로 영문으로 바꿨다.
    [Fact]
    public void AppSwitchingItselfRightAfterArrival_IsUndone()
    {
        var k = new ImeModeKeeper();
        See(k, Chrome, Han, 0);
        long t = 10_000;
        See(k, Xshell, Eng, t, canSwitch: false, focus: 0);
        See(k, Xshell, Eng, t + 145, canSwitch: false, focus: XshellMain);
        See(k, Xshell, Eng, t + 181, canSwitch: false, focus: XshellToolbar);
        Assert.Null(See(k, Xshell, Han, t + 186, focus: XshellEdit));
        Assert.Equal(Han, See(k, Xshell, Eng, t + 233, focus: XshellEdit));   // 스스로 영문으로 → 되돌린다
        Assert.Equal(Han, k.Desired);
        Assert.Equal(1, k.Attempts);                                           // 처음부터 IME 요청으로
        Assert.Null(See(k, Xshell, Han, t + 260, focus: XshellEdit));
        Assert.Null(See(k, Xshell, Eng, t + 4000, focus: XshellEdit));         // 한참 뒤 한/영 키는 사용자 선택
        Assert.Equal(Eng, k.Desired);
    }

    [Fact]
    public void AppKeepsSwitchingBack_OnlyIMERequestsAreRepeated_KeyOncePerWindow()
    {
        var k = new ImeModeKeeper();
        See(k, Chrome, Han, 0);
        long t = 10_000;
        Assert.Equal(Han, See(k, Xshell, Eng, t, focus: XshellEdit));                              // IME 요청
        Assert.Equal(Han, See(k, Xshell, Eng, t + ImeModeKeeper.RetryMs, focus: XshellEdit));      // 그대로 → 한/영 키
        k.GiveUp();
        Assert.False(k.KeyAllowed);
        Assert.Null(See(k, Xshell, Han, t + 400, focus: XshellEdit));
        Assert.Equal(Han, See(k, Xshell, Eng, t + 450, focus: XshellEdit));   // 또 스스로 영문으로: IME 요청은 다시
        Assert.Equal(1, k.Attempts);
        Assert.Null(See(k, Xshell, Eng, t + 450 + ImeModeKeeper.RetryMs, focus: XshellEdit));   // 한/영 키는 이 창에서 이미 보냈다
        Assert.Equal(Han, See(k, Xshell, Eng, t + 900, focus: XshellMain));   // 다른 칸: IME 요청은 다시
        Assert.Equal(Han, k.Desired);
        Assert.Equal(Han, See(k, Chrome, Eng, t + 5000));                      // 다른 창에서는 한/영 키도 다시 쓸 수 있다
        Assert.True(k.KeyAllowed);
    }

    [Fact]
    public void UnknownReadingInBetween_IsNotAChange()
    {
        var k = new ImeModeKeeper();
        See(k, Notepad, Han, 0);
        Assert.Null(See(k, Chrome, null, 10_000));                 // 처음 읽기가 시간 초과
        Assert.Equal(Han, See(k, Chrome, Eng, 10_100));            // 처음 읽힌 모드가 다르면 맞춘다(사용자 선택 아님)
        Assert.Equal(Han, k.Desired);
        Assert.Null(See(k, Chrome, Han, 10_200));
        Assert.Null(See(k, Chrome, null, 20_000));                 // 잠깐 못 읽었다가
        Assert.Null(See(k, Chrome, Han, 20_100));                  // 같은 모드
        Assert.Equal(Han, k.Desired);
    }
}
