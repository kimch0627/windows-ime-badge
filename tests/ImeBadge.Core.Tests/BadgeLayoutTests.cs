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
        var edge = new Rectangle(1915, 1030, 2, 20);
        var p = BadgeLayout.Compute(In(edge, place: BadgePlacement.BelowRight));
        Assert.Equal(1920 - 24, p.X);
        Assert.Equal(1040 - 20, p.Y);
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
}
