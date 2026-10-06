using Xunit;

namespace ImeBadge.Tests;

public sealed class PasswordGuardTests
{
    const long Browser = 10, Other = 20;

    [Fact]
    public void EnteringWithHangul_Warns_ThenSwitchingToEnglishHides()
    {
        var g = new PasswordGuard();
        Assert.Equal(PasswordRisk.Hangul, g.Update(true, Browser, PasswordRisk.Hangul));
        Assert.Null(g.Update(true, Browser, PasswordRisk.Hangul));                  // 되풀이하지 않는다
        Assert.Equal(PasswordRisk.None, g.Update(true, Browser, PasswordRisk.None)); // 영문으로 바꿨다 → 내린다
        Assert.Null(g.Update(true, Browser, PasswordRisk.None));
    }

    [Fact]
    public void RiskChangesInsideTheField_WarnsAgain()
    {
        var g = new PasswordGuard();
        Assert.Null(g.Update(true, Browser, PasswordRisk.None));                    // 안전하게 들어왔다
        Assert.Equal(PasswordRisk.CapsLock, g.Update(true, Browser, PasswordRisk.CapsLock));
        Assert.Equal(PasswordRisk.Hangul | PasswordRisk.CapsLock, g.Update(true, Browser, PasswordRisk.Hangul | PasswordRisk.CapsLock));
    }

    [Fact]
    public void LeavingTheField_HidesOnlyWhenShown()
    {
        var g = new PasswordGuard();
        Assert.Null(g.Update(false, Browser, PasswordRisk.Hangul));                 // 비밀번호 칸이 아니면 아무 일 없다
        g.Update(true, Browser, PasswordRisk.Hangul);
        Assert.Equal(PasswordRisk.None, g.Update(false, Browser, PasswordRisk.Hangul));
        Assert.Null(g.Update(false, Browser, PasswordRisk.Hangul));
    }

    [Fact]
    public void ComingBackOrAnotherWindow_WarnsAgain()
    {
        var g = new PasswordGuard();
        g.Update(true, Browser, PasswordRisk.Hangul);
        Assert.Equal(PasswordRisk.Hangul, g.Update(true, Other, PasswordRisk.Hangul));   // 다른 창의 비밀번호 칸
        g.Update(false, Other, PasswordRisk.Hangul);
        Assert.Equal(PasswordRisk.Hangul, g.Update(true, Other, PasswordRisk.Hangul));   // 나갔다 다시 들어왔다
        g.Reset();
        Assert.Equal(PasswordRisk.Hangul, g.Update(true, Other, PasswordRisk.Hangul));
    }
}
