using System;
using System.Linq;
using Xunit;

namespace ImeBadge.Tests;

/// <summary>배지가 안 뜬 앱 찾기: 1초 단위로 세기, 목록에 올리는 기준, 까닭 고르기, 순서, 무시·지우기, 상한.</summary>
public sealed class MissedAppTrackerTests
{
    static readonly DateTime At = new(2026, 10, 15, 14, 0, 0);
    const int Min = MissedAppTracker.MinMissedSeconds;

    static bool Miss(MissedAppTracker t, string app, int second, string reason = MissReason.NoCaret, string detail = "") =>
        t.Observe(app, reason, second * (long)MissedAppTracker.BucketMs, At.AddSeconds(second), detail);

    static void Shown(MissedAppTracker t, string app, int second) =>
        t.Observe(app, null, second * (long)MissedAppTracker.BucketMs, At.AddSeconds(second));

    [Fact]
    public void ListsApp_OnlyAfterEnoughMissedSeconds()
    {
        var t = new MissedAppTracker();
        for (int s = 0; s < Min - 1; s++) Miss(t, "javaw", s);
        Assert.Empty(t.List());   // 우연히 한두 번 놓친 것은 올리지 않는다

        Miss(t, "javaw", 10);
        var app = Assert.Single(t.List());
        Assert.Equal("javaw", app.Process);
        Assert.Equal(MissReason.NoCaret, app.Reason);
        Assert.True(app.NeverShown);
        Assert.Equal(Min, app.MissedSeconds);
    }

    [Fact]
    public void SameSecond_CountsOnce()
    {
        // 폴링(기본 0.1초)마다 불러도 1초에 한 번만 센다.
        var t = new MissedAppTracker();
        for (int ms = 0; ms < 1000; ms += 100) t.Observe("javaw", MissReason.NoCaret, ms, At);
        t.Observe("javaw", MissReason.NoCaret, 1000, At);
        t.Observe("javaw", MissReason.NoCaret, 1999, At);
        Assert.Empty(t.List());
        t.Observe("javaw", MissReason.NoCaret, 2000, At);
        Assert.Equal(3, Assert.Single(t.List()).MissedSeconds);
    }

    [Fact]
    public void Observe_ReturnsTrue_OnlyWhenTheAppIsNewlyListed()
    {
        var t = new MissedAppTracker();
        for (int s = 0; s < Min - 1; s++) Assert.False(Miss(t, "javaw", s));
        Assert.True(Miss(t, "javaw", Min - 1));
        Assert.False(Miss(t, "javaw", Min));
    }

    [Fact]
    public void ShownOnlyApp_IsNeverListed()
    {
        var t = new MissedAppTracker();
        for (int s = 0; s < 10; s++) Shown(t, "notepad", s);
        Assert.Empty(t.List());
    }

    [Fact]
    public void ShownBeforeMisses_MakesItPartial()
    {
        // 배지가 잘 뜨던 앱이 몇 번 놓쳤으면 "한 번도 안 뜸" 이 아니다.
        var t = new MissedAppTracker();
        Shown(t, "chrome", 0);
        for (int s = 1; s <= Min; s++) Miss(t, "chrome", s, MissReason.Slow);
        var app = Assert.Single(t.List());
        Assert.False(app.NeverShown);
        Assert.Equal(1, app.ShownSeconds);
    }

    [Fact]
    public void ShownAfterListed_ChangesVersion()
    {
        var t = new MissedAppTracker();
        for (int s = 0; s < Min; s++) Miss(t, "chrome", s);
        int before = t.Version;
        Shown(t, "chrome", 10);
        Assert.NotEqual(before, t.Version);
        Assert.False(Assert.Single(t.List()).NeverShown);
    }

    [Fact]
    public void UnlistedChanges_KeepVersion()
    {
        // 목록에 오르지 않은 앱이 바뀌어도 설정 창이 다시 그릴 필요가 없다.
        var t = new MissedAppTracker();
        int before = t.Version;
        Shown(t, "notepad", 0);
        Miss(t, "javaw", 0);
        Assert.Equal(before, t.Version);
    }

    [Fact]
    public void Reason_IsMostFrequent_TiesGoToLatest()
    {
        var t = new MissedAppTracker();
        Miss(t, "app", 0, MissReason.Slow);
        Miss(t, "app", 1, MissReason.NoCaret);
        Miss(t, "app", 2, MissReason.NoCaret);
        Miss(t, "app", 3, MissReason.Slow);   // 2:2 → 최근 것
        Assert.Equal(MissReason.Slow, Assert.Single(t.List()).Reason);
        Miss(t, "app", 4, MissReason.NoCaret);   // 3:2
        Assert.Equal(MissReason.NoCaret, Assert.Single(t.List()).Reason);
    }

    [Fact]
    public void KeepsLastMissedTime_AndDetail()
    {
        var t = new MissedAppTracker();
        for (int s = 0; s < Min; s++) Miss(t, "javaw", s, detail: $"fg='SunAwtFrame' n={s}");
        Shown(t, "javaw", 50);   // 보인 순간은 마지막으로 안 보인 때를 바꾸지 않는다
        var app = Assert.Single(t.List());
        Assert.Equal(At.AddSeconds(Min - 1), app.LastMissed);
        Assert.Equal($"fg='SunAwtFrame' n={Min - 1}", app.Detail);
    }

    [Fact]
    public void ProcessNames_IgnoreCase_AndEmptyIsIgnored()
    {
        var t = new MissedAppTracker();
        Miss(t, "Javaw", 0);
        Miss(t, "JAVAW", 1);
        Miss(t, "javaw", 2);
        Miss(t, "", 3);
        t.Observe(null, MissReason.NoCaret, 4000, At);
        Assert.Equal(Min, Assert.Single(t.List()).MissedSeconds);
    }

    [Fact]
    public void List_NeverShownFirst_ThenMostRecentMiss()
    {
        var t = new MissedAppTracker();
        Shown(t, "partial", 0);
        for (int s = 0; s < Min; s++) Miss(t, "partial", 100 + s);   // 가장 최근이지만 가끔 뜬다
        for (int s = 0; s < Min; s++) Miss(t, "old", s);
        for (int s = 0; s < Min; s++) Miss(t, "recent", 50 + s);
        Assert.Equal(new[] { "recent", "old", "partial" }, t.List().Select(a => a.Process));
    }

    [Fact]
    public void Ignore_RemovesAndStopsCounting_UntilClear()
    {
        var t = new MissedAppTracker();
        for (int s = 0; s < Min; s++) Miss(t, "game", s);
        int before = t.Version;
        t.Ignore("GAME");
        Assert.Empty(t.List());
        Assert.NotEqual(before, t.Version);

        for (int s = 10; s < 20; s++) Miss(t, "game", s);
        Assert.Empty(t.List());   // 무시한 앱은 세지 않는다

        t.Clear();                // 모두 지우면 무시도 잊는다
        for (int s = 30; s < 30 + Min; s++) Miss(t, "game", s);
        Assert.Single(t.List());
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var t = new MissedAppTracker();
        int empty = t.Version;
        t.Clear();
        Assert.Equal(empty, t.Version);   // 지울 것이 없으면 그대로

        for (int s = 0; s < Min; s++) Miss(t, "javaw", s);
        int before = t.Version;
        t.Clear();
        Assert.Empty(t.List());
        Assert.NotEqual(before, t.Version);
    }

    [Fact]
    public void WhenFull_ForgetsAppsWithoutMissesFirst()
    {
        var t = new MissedAppTracker();
        for (int s = 0; s < Min; s++) Miss(t, "listed", s);   // 가장 오래전에 본 앱이지만 놓친 적이 있다
        for (int i = 0; i < MissedAppTracker.MaxApps - 1; i++) Shown(t, "ok" + i, 10 + i);
        for (int s = 0; s < Min; s++) Miss(t, "new", 100 + s);
        Assert.Equal(new[] { "new", "listed" }, t.List().Select(a => a.Process));
    }

    [Fact]
    public void WhenFull_ForgetsTheLeastRecentlySeenApp()
    {
        var t = new MissedAppTracker();
        for (int i = 0; i < MissedAppTracker.MaxApps; i++)
            for (int s = 0; s < Min - 1; s++) Miss(t, "app" + i, i * 10 + s);   // 모두 한 번 덜 놓쳤다
        Miss(t, "fresh", 1000);          // 꽉 찼다: 가장 오래전에 본 app0 을 잊는다
        Miss(t, "app1", 1001);           // app1 은 남아 있어 목록에 오른다
        Miss(t, "app0", 1002);           // app0 은 처음부터 다시 센다
        Assert.Equal(new[] { "app1" }, t.List().Select(a => a.Process));
    }

    [Theory]
    [InlineData(InputSurface.Unknown)]
    [InlineData(InputSurface.Edit)]
    [InlineData(InputSurface.Document)]
    [InlineData(InputSurface.ComboBox)]
    [InlineData(InputSurface.Pane)]      // 직접 글자를 그리는 앱(터미널·Java·Qt)은 입력칸을 Pane·Custom·Window 로만 알린다
    [InlineData(InputSurface.Custom)]
    [InlineData(InputSurface.Window)]
    [InlineData(InputSurface.Group)]
    public void MayTakeText_TextLikeOrUnknown(int controlType) => Assert.True(InputSurface.MayTakeText(controlType));

    [Theory]
    [InlineData(50000)]   // Button: 스페이스 = 누르기
    [InlineData(50002)]   // CheckBox
    [InlineData(50007)]   // ListItem: 탐색기 파일 목록(백스페이스 = 뒤로)
    [InlineData(50011)]   // MenuItem
    [InlineData(50019)]   // TabItem
    [InlineData(50020)]   // Text(글자 라벨)
    [InlineData(50024)]   // TreeItem
    [InlineData(50005)]   // Hyperlink
    public void MayTakeText_NotForControlsThatDontTakeText(int controlType) => Assert.False(InputSurface.MayTakeText(controlType));

    [Fact]
    public void MayTakeText_ChromiumGroupNeedsTextPattern()
    {
        const string chrome = InputSurface.ChromiumFramework;
        // 키보드 포커스만 받는 웹 요소(Paseo 의 사이드바 손잡이·파일 목록 줄): 입력칸이 아니다
        Assert.False(InputSurface.MayTakeText(InputSurface.Group, chrome, hasTextPattern: false));
        // 역할이 없는 contentEditable 도 묶음이지만 TextPattern 이 있다
        Assert.True(InputSurface.MayTakeText(InputSurface.Group, chrome, hasTextPattern: true));
        // 웹 내용의 접근성이 켜지기 전의 창 껍데기(Pane)와 다른 프레임워크의 묶음은 그대로 센다
        Assert.True(InputSurface.MayTakeText(InputSurface.Pane, chrome, hasTextPattern: false));
        Assert.True(InputSurface.MayTakeText(InputSurface.Group, "Win32", hasTextPattern: false));
        Assert.True(InputSurface.MayTakeText(InputSurface.Group, null, hasTextPattern: false));
        // 버튼은 프레임워크와 상관없이 입력칸이 아니다
        Assert.False(InputSurface.MayTakeText(50000, chrome, hasTextPattern: false));
    }

    const int Confirm = MissConfirm.ConfirmMs;

    [Fact]
    public void Confirm_IgnoresMomentaryGap()
    {
        // Xshell: 창에 들어오는 동안 주 창이 0.1초쯤 포커스를 갖다가 입력칸으로 넘긴다. 그 순간 한/영이 달라 보여도 놓친 것이 아니다.
        var c = new MissConfirm();
        Assert.False(c.Update(0, window: 1, typing: true, missing: true));
        Assert.True(c.Pending);
        Assert.False(c.Update(100, window: 1, typing: false, missing: false));   // 배지가 떴다
        Assert.False(c.Pending);
        Assert.False(c.Update(Confirm + 100, window: 1, typing: false, missing: true));   // 입력하지 않는 동안 다시 안 보인 것은 기다리지 않는다
    }

    [Fact]
    public void Confirm_CountsWhenStillMissing_EvenAfterKeyIsReleased()
    {
        var c = new MissConfirm();
        Assert.False(c.Update(0, window: 1, typing: true, missing: true));   // 한/영 키를 한 번 눌렀다
        Assert.False(c.Update(Confirm - 1, window: 1, typing: false, missing: true));
        Assert.True(c.Update(Confirm, window: 1, typing: false, missing: true));
        Assert.False(c.Pending);   // 한 번 정하면 다음 입력부터 다시 기다린다
        Assert.False(c.Update(Confirm + 100, window: 1, typing: false, missing: true));

        // 백스페이스를 계속 누르고 있으면 기다림이 끝날 때마다 다시 센다(MissedAppTracker 가 1초에 한 번으로 줄인다)
        Assert.False(c.Update(Confirm + 200, window: 1, typing: true, missing: true));
        Assert.True(c.Update(2 * Confirm + 200, window: 1, typing: true, missing: true));
    }

    [Fact]
    public void Confirm_StartsOverInAnotherWindow_AndDropsWhenNotInput()
    {
        var c = new MissConfirm();
        c.Update(0, window: 1, typing: true, missing: true);
        Assert.False(c.Update(Confirm, window: 2, typing: false, missing: true));   // 다른 창: 그 창에서 입력하기 전에는 세지 않는다
        Assert.False(c.Pending);

        c.Update(1000, window: 2, typing: true, missing: true);
        Assert.False(c.Update(1000 + Confirm, window: 2, typing: false, missing: false));   // 버튼으로 옮겼다(입력칸이 아님)
        Assert.False(c.Pending);

        c.Update(3000, window: 2, typing: true, missing: true);
        Assert.False(c.Update(3000 + Confirm, window: 3, typing: true, missing: true));   // 바뀐 창에서 입력: 그 창의 기다림을 새로 시작
        Assert.True(c.Update(3000 + 2 * Confirm, window: 3, typing: false, missing: true));
    }

    [Theory]
    [InlineData(UiLanguage.Korean)]
    [InlineData(UiLanguage.English)]
    public void EveryReason_HasTextAndDetails(UiLanguage lang)
    {
        var table = Strings.Table(lang);
        foreach (var reason in MissReason.All)
        {
            Assert.True(table.ContainsKey("missed.reason." + reason), $"missed.reason.{reason} 없음");
            Assert.True(table.ContainsKey("missed.reason." + reason + ".details"), $"missed.reason.{reason}.details 없음");
        }
    }
}
