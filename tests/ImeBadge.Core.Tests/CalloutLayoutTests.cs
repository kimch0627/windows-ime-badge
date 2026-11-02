using System.Drawing;
using Xunit;

namespace ImeBadge.Tests;

public sealed class CalloutLayoutTests
{
    static readonly Rectangle Area = new(0, 0, 1920, 1040);
    static readonly Size Body = new(240, 44);
    const int Tail = 7, Gap = 4, Inset = 18;

    [Fact]
    public void Below_TailPointsAtTheCaret()
    {
        var caret = new Rectangle(500, 300, 1, 20);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset);
        Assert.True(p.Below);
        Assert.Equal(caret.Bottom + Gap + Tail, p.Location.Y);
        Assert.Equal(caret.Left, p.Location.X + p.TailX);
    }

    [Fact]
    public void Above_WhenPreferred()
    {
        var caret = new Rectangle(500, 300, 1, 20);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: false, Inset);
        Assert.False(p.Below);
        Assert.Equal(caret.Top - Gap - Tail - Body.Height, p.Location.Y);
    }

    [Fact]
    public void FlipsUp_AtTheBottomOfTheScreen()
    {
        var caret = new Rectangle(500, 1010, 1, 20);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset);
        Assert.False(p.Below);
        Assert.True(p.Location.Y + Body.Height + Tail <= caret.Top);
    }

    [Fact]
    public void FlipsDown_AtTheTopOfTheScreen()
    {
        var caret = new Rectangle(500, 10, 1, 20);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: false, Inset);
        Assert.True(p.Below);
        Assert.True(p.Location.Y >= caret.Bottom);
    }

    [Fact]
    public void RightEdge_PushesBodyIn_TailStillPointsAtCaret()
    {
        var caret = new Rectangle(1900, 300, 1, 20);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset);
        Assert.Equal(Area.Right - Body.Width, p.Location.X);
        Assert.Equal(caret.Left, p.Location.X + p.TailX);
        Assert.True(p.TailX <= Body.Width - Inset);
    }

    [Fact]
    public void LeftEdge_KeepsTailAwayFromTheCorner()
    {
        var caret = new Rectangle(2, 300, 1, 20);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset);
        Assert.Equal(Area.Left, p.Location.X);
        Assert.Equal(Inset, p.TailX);
    }

    [Fact]
    public void ApproximateCaret_AlwaysBelow()
    {
        var caret = new Rectangle(500, 300, 1, 0);   // UIA 가 입력칸 모서리만 준 경우
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: false, Inset);
        Assert.True(p.Below);
    }

    // ── 가리면 안 되는 창(포커스 뺏김 경고: 앞으로 나온 창) ──

    static Rectangle BodyAt(CalloutPlacement p) => new(p.Location, Body);

    [Fact]
    public void Avoid_WindowElsewhere_KeepsTheCaretPlacement()
    {
        var caret = new Rectangle(500, 300, 1, 20);
        var plain = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset);
        Assert.Equal(plain, CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset, avoid: new Rectangle(1200, 100, 500, 400)));
        Assert.Equal(plain, CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset, avoid: Rectangle.Empty));
    }

    [Fact]
    public void Avoid_WindowRightUnderTheCaret_FlipsAbove_TailStillOnTheCaret()
    {
        var caret = new Rectangle(500, 300, 1, 20);
        var popup = new Rectangle(300, 330, 600, 300);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset, popup);
        Assert.False(p.Below);
        Assert.Equal(caret.Left, p.Location.X + p.TailX);
        Assert.False(BodyAt(p).IntersectsWith(popup));
    }

    [Fact]
    public void Avoid_CaretCoveredByTheWindow_GoesBelowItsNearerBottomEdge()
    {
        // 쓰던 자리를 새 창(쪽지쓰기 등)이 덮었다: 그 창의 내용 대신 바깥 아래 가장자리에, 꼬리는 커서와 같은 x 의 창 가장자리로.
        var window = new Rectangle(400, 200, 500, 300);   // 아래 가장자리 500
        var caret = new Rectangle(600, 420, 1, 20);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset, window);
        Assert.True(p.Below);
        Assert.Equal(window.Bottom + Gap + Tail, p.Location.Y);
        Assert.Equal(caret.Left, p.Location.X + p.TailX);
        Assert.False(BodyAt(p).IntersectsWith(window));
    }

    [Fact]
    public void Avoid_CaretNearTheTopOfTheWindow_GoesAboveIt()
    {
        var window = new Rectangle(400, 200, 500, 300);
        var caret = new Rectangle(600, 230, 1, 20);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset, window);
        Assert.False(p.Below);
        Assert.Equal(window.Top - Gap - Tail - Body.Height, p.Location.Y);
        Assert.False(BodyAt(p).IntersectsWith(window));
    }

    [Fact]
    public void Avoid_NoRoomUnderTheWindow_UsesTheOtherEdge()
    {
        var window = new Rectangle(400, 600, 500, 420);   // 아래 가장자리 1020, 작업 영역 끝 1040
        var caret = new Rectangle(600, 950, 1, 20);
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset, window);
        Assert.False(p.Below);
        Assert.True(BodyAt(p).Bottom + Tail <= window.Top);
    }

    [Fact]
    public void Avoid_TailStaysInsideTheWindowWidth()
    {
        var window = new Rectangle(400, 200, 500, 300);
        var caret = new Rectangle(window.Right - 2, 420, 1, 20);   // 창 오른쪽 끝(둥근 모서리) 바로 안
        var p = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset, window);
        Assert.Equal(window.Right - Inset, p.Location.X + p.TailX);
    }

    [Fact]
    public void Avoid_WindowFillsTheScreen_FallsBackToTheCaret()
    {
        var caret = new Rectangle(500, 300, 1, 20);
        var plain = CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset);
        Assert.Equal(plain, CalloutLayout.Compute(caret, Body, Tail, Gap, Area, preferBelow: true, Inset, avoid: Area));
    }
}
