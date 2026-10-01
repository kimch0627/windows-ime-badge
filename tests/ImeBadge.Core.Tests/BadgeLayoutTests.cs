using System.Drawing;
using Xunit;

namespace ImeBadge.Tests;

public sealed class BadgeLayoutTests
{
    static readonly Rectangle Work = new(0, 0, 1920, 1040);
    static readonly Rectangle Caret = new(500, 300, 2, 20);   // Left=500 Right=502 Top=300 Bottom=320
    static readonly Size Badge = new(24, 20);

    static LayoutInput In(Rectangle caret, BadgeStyle style = BadgeStyle.Pill, BadgePlacement place = BadgePlacement.AboveRight,
                          float scale = 1f, Rectangle? work = null) =>
        new(caret, Badge, style, place, scale, work ?? Work);

    [Fact]
    public void AboveRight_PutsBadgeAboveAndRightOfCaret()
    {
        var p = BadgeLayout.Compute(In(Caret));
        Assert.Equal(new Point(502 + 3, 300 - 20 - 3), p);
    }

    [Fact]
    public void BelowRight_PutsBadgeBelowCaret()
    {
        var p = BadgeLayout.Compute(In(Caret, place: BadgePlacement.BelowRight));
        Assert.Equal(new Point(505, 323), p);
    }

    [Fact]
    public void AboveLeft_PutsBadgeLeftOfCaret()
    {
        var p = BadgeLayout.Compute(In(Caret, place: BadgePlacement.AboveLeft));
        Assert.Equal(new Point(500 - 3 - 24, 300 - 20 - 3), p);
    }

    [Fact]
    public void BelowLeft_PutsBadgeBelowLeftOfCaret()
    {
        var p = BadgeLayout.Compute(In(Caret, place: BadgePlacement.BelowLeft));
        Assert.Equal(new Point(500 - 3 - 24, 323), p);
    }

    [Fact]
    public void NoRoomLeft_FallsBackRight()
    {
        var edge = new Rectangle(10, 300, 2, 20);
        var p = BadgeLayout.Compute(In(edge, place: BadgePlacement.AboveLeft));
        Assert.Equal(12 + 3, p.X);
    }

    [Fact]
    public void Above_CentersBadgeOverCaret()
    {
        var p = BadgeLayout.Compute(In(Caret, place: BadgePlacement.Above));
        Assert.Equal(new Point(500 + 1 - 12, 300 - 20 - 3), p);
    }

    [Fact]
    public void Below_CentersBadgeUnderCaret()
    {
        var p = BadgeLayout.Compute(In(Caret, place: BadgePlacement.Below));
        Assert.Equal(new Point(500 + 1 - 12, 320 + 3), p);
    }

    [Fact]
    public void Above_NoRoomAbove_FallsBackBelow_StaysCentered()
    {
        var top = new Rectangle(500, 5, 2, 20);
        var p = BadgeLayout.Compute(In(top, place: BadgePlacement.Above));
        Assert.Equal(new Point(500 + 1 - 12, 25 + 3), p);
    }

    [Fact]
    public void GapScalesWithDpi()
    {
        var p = BadgeLayout.Compute(In(Caret, scale: 2f));
        Assert.Equal(new Point(502 + 6, 300 - 20 - 6), p);
    }

    [Fact]
    public void ApproximateCaret_AlwaysGoesBelow()
    {
        var approx = new Rectangle(500, 320, 1, 0);   // 높이 0 = UIA 근사 위치
        var p = BadgeLayout.Compute(In(approx, place: BadgePlacement.AboveRight));
        Assert.Equal(new Point(501 + 3, 320 + 3), p);
    }

    [Fact]
    public void Underline_IsCenteredUnderCaret_IgnoringPlacement()
    {
        var p = BadgeLayout.Compute(In(Caret, style: BadgeStyle.Underline, place: BadgePlacement.AboveRight));
        Assert.Equal(new Point(500 + 1 - 12, 321), p);
    }

    [Fact]
    public void NoRoomAbove_FallsBackBelow()
    {
        var top = new Rectangle(500, 5, 2, 20);
        var p = BadgeLayout.Compute(In(top));
        Assert.Equal(25 + 3, p.Y);
    }

    [Fact]
    public void ClampedToWorkArea_RightAndBottom()
    {
        // 밑줄은 caret 에 붙은 막대라 아래 자리가 없어도 위로 옮기지 않고 작업 영역 안으로 밀어 넣는다.
        var edge = new Rectangle(1915, 1030, 2, 20);
        var p = BadgeLayout.Compute(In(edge, style: BadgeStyle.Underline));
        Assert.Equal(1920 - 24, p.X);
        Assert.Equal(1040 - 20, p.Y);
    }

    [Theory]
    [InlineData(BadgePlacement.Below, 500 + 1 - 12)]
    [InlineData(BadgePlacement.BelowRight, 502 + 3)]
    [InlineData(BadgePlacement.BelowLeft, 500 - 3 - 24)]
    public void NoRoomBelow_FallsBackAbove(BadgePlacement place, int x)
    {
        // 작업 영역 아래 끝에 붙은 caret(예: 최대화한 터미널의 마지막 줄). 밀어 올리면 배지가 caret 을 가린다.
        var bottom = new Rectangle(500, 1015, 2, 20);   // Bottom=1035, 아래 남은 자리 5px
        var p = BadgeLayout.Compute(In(bottom, place: place));
        Assert.Equal(new Point(x, 1015 - 20 - 3), p);
    }

    [Fact]
    public void ClampedToWorkArea_LeftAndTop_OnSecondaryMonitorWithNegativeOrigin()
    {
        var work = new Rectangle(-1920, -100, 1920, 1000);
        var caret = new Rectangle(-1925, -150, 2, 20);   // 작업 영역 밖
        var p = BadgeLayout.Compute(In(caret, style: BadgeStyle.Underline, work: work));
        Assert.Equal(work.Left, p.X);
        Assert.Equal(work.Top, p.Y);
    }

    // 한/영 전환 효과(펄스)로 배지가 18% 커지는 동안에도 고른 쪽에 머문다. 원래 크기 24×20 은 들어가고 커진 28×24 는 안 들어가는 자리.
    static readonly Size Pulsed = new(28, 24);

    [Fact]
    public void Pulse_NearTop_StaysAbove()
    {
        var caret = new Rectangle(500, 24, 2, 20);   // 위로 24px: 원래 크기(20+3) 는 들어가고 커진 크기(24+3) 는 안 들어감
        var resting = BadgeLayout.Compute(In(caret));
        var pulsed = BadgeLayout.Compute(new LayoutInput(caret, Pulsed, BadgeStyle.Pill, BadgePlacement.AboveRight, 1f, Work, Badge));
        Assert.Equal(1, resting.Y);
        Assert.Equal(0, pulsed.Y);   // 위쪽에 붙어 작업 영역 안으로 밀림. 아래(47)로 튀지 않는다
    }

    [Fact]
    public void Pulse_NearBottom_StaysBelow()
    {
        var caret = new Rectangle(500, 996, 2, 20);   // 아래로 24px
        var resting = BadgeLayout.Compute(In(caret, place: BadgePlacement.Below));
        var pulsed = BadgeLayout.Compute(new LayoutInput(caret, Pulsed, BadgeStyle.Pill, BadgePlacement.Below, 1f, Work, Badge));
        Assert.Equal(1019, resting.Y);
        Assert.Equal(1040 - 24, pulsed.Y);   // 아래쪽에 붙어 작업 영역 안으로 밀림. 위(969)로 튀지 않는다
    }

    [Fact]
    public void Pulse_NearLeftEdge_StaysLeft()
    {
        var caret = new Rectangle(28, 300, 2, 20);   // 왼쪽으로 28px: 원래 폭(24+3) 은 들어가고 커진 폭(28+3) 은 안 들어감
        var pulsed = BadgeLayout.Compute(new LayoutInput(caret, Pulsed, BadgeStyle.Pill, BadgePlacement.AboveLeft, 1f, Work, Badge));
        Assert.Equal(0, pulsed.X);   // 오른쪽(33)으로 튀지 않는다
    }
}
