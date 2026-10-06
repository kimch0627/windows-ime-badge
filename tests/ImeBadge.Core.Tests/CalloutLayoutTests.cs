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
}
