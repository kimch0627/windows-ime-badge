using System;
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
        // 막대 둘레의 특수 키 표시 자리(아래 Tail, 좌우 Side)는 작업 영역 밖으로 나가도 된다(막대가 caret 줄로 올라오지 않게).
        var edge = new Rectangle(1915, 1030, 2, 20);
        var p = BadgeLayout.Compute(In(edge, style: BadgeStyle.Underline));
        Assert.Equal(1920 - 24 + BadgeLayout.Side(BadgeStyle.Underline, 1f), p.X);
        Assert.Equal(1040 - (20 - BadgeLayout.Tail(BadgeStyle.Underline, 1f)), p.Y);
    }

    // 점 그림(배율 1): 몸통 = 지름 9 + 그림자 여백 → 13×15. 그 둘레에 특수 키 표시 자리: 좌우 Side(9), 아래 Tail(6) → 31×21.
    static readonly int DotSide = BadgeLayout.Side(BadgeStyle.Dot, 1f);
    static readonly Size DotPicture = new(13 + 2 * DotSide, 15 + BadgeLayout.Tail(BadgeStyle.Dot, 1f));

    [Fact]
    public void MarkRoom_IsOnlyForDotAndUnderline_AndScales()
    {
        Assert.Equal(6, BadgeLayout.Tail(BadgeStyle.Dot, 1f));
        Assert.Equal(7, BadgeLayout.Tail(BadgeStyle.Underline, 1f));
        Assert.Equal(9, BadgeLayout.Tail(BadgeStyle.Dot, 1.5f));
        Assert.Equal(0, BadgeLayout.Tail(BadgeStyle.Pill, 1f));
        Assert.Equal(0, BadgeLayout.Tail(BadgeStyle.Box, 2f));
        Assert.Equal(9, BadgeLayout.Side(BadgeStyle.Dot, 1f));
        Assert.Equal(5, BadgeLayout.Side(BadgeStyle.Underline, 1f));
        Assert.Equal(0, BadgeLayout.Side(BadgeStyle.Pill, 1f));
    }

    /// <summary>
    /// VW-08: 점·밑줄 아래 기호 줄(▲ ▁ ■, 둘레 포함)이 그림에 비워 둔 자리 안에 들어간다. 몸통 치수는 렌더러와 같은 계산
    /// (점 지름 round(9·배율), 밑줄 round(16·배율)×round(3·배율), 그림자 여백 ceil(2·배율)). 0.5px 까지는 그림자 가장자리라 봐준다.
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void MarkRow_FitsItsRoom(float s)
    {
        int pad = (int)Math.Ceiling(2 * s);
        float row = BadgeLayout.MarkRowHalf(s), outline = BadgeLayout.MarkOutline * s, tri = MathF.Round(BadgeLayout.MarkTriH * s);

        int d = (int)Math.Round(9 * s);
        Assert.True(BadgeLayout.Side(BadgeStyle.Dot, s) + d / 2f + pad >= row - 0.5f, $"dot side @{s}");
        float dotBottom = MathF.Round(pad + d + BadgeLayout.MarkGap * s) + tri + outline;
        Assert.True(d + 3 * pad + BadgeLayout.Tail(BadgeStyle.Dot, s) >= dotBottom - 0.5f, $"dot tail @{s}");

        int w = (int)Math.Round(16 * s), h = (int)Math.Round(3 * s);
        Assert.True(BadgeLayout.Side(BadgeStyle.Underline, s) + w / 2f + pad >= row - 0.5f, $"underline side @{s}");
        float barBottom = MathF.Round(h + BadgeLayout.MarkGap * s) + tri + outline;
        Assert.True(h + pad + BadgeLayout.Tail(BadgeStyle.Underline, s) >= barBottom - 0.5f, $"underline tail @{s}");
    }

    /// <summary>VW-08: 100% 에서 세 기호가 구별될 만큼 크다(3px 기호는 서로 구별되지 않았다). 밑줄이 네모보다 뚜렷이 넓다.</summary>
    [Fact]
    public void Marks_AreBigEnoughToTellApart()
    {
        Assert.True(BadgeLayout.MarkTriH >= 4 && BadgeLayout.MarkTriW >= 6);
        Assert.True(BadgeLayout.MarkBarW >= 2 * BadgeLayout.MarkTriH);
        Assert.Equal(1, (int)BadgeLayout.MarkBarW % 2);   // 홀수 폭: 점(지름 9) 가운데에 맞아 좌우 틈이 같다
    }

    /// <summary>표시 자리를 뺀 몸통(13×15)이 예전(1.13.0) 점 자리와 같다. 표시 자리는 caret 과의 틈이나 바깥으로 나간다.</summary>
    [Theory]
    [InlineData(BadgePlacement.AboveRight, 502 + 3, 300 - 3 - 15)]
    [InlineData(BadgePlacement.Above, 501 - 6, 300 - 3 - 15)]
    [InlineData(BadgePlacement.AboveLeft, 500 - 3 - 13, 300 - 3 - 15)]
    [InlineData(BadgePlacement.BelowRight, 502 + 3, 320 + 3)]
    [InlineData(BadgePlacement.BelowLeft, 500 - 3 - 13, 320 + 3)]
    public void Dot_KeepsItsPlace_WithMarkRoom(BadgePlacement place, int bodyX, int y)
    {
        var p = BadgeLayout.Compute(new LayoutInput(Caret, DotPicture, BadgeStyle.Dot, place, 1f, Work));
        Assert.Equal(new Point(bodyX - DotSide, y), p);
    }

    [Fact]
    public void Dot_RoomAboveIgnoresMarkRoom()
    {
        // 위로 18px: 몸통(3+15)은 들어가고 표시 자리까지(3+19)는 안 들어감 → 몸통 기준이라 위에 그대로 둔다(아래로 튀지 않음).
        var caret = new Rectangle(500, 18, 2, 20);
        var p = BadgeLayout.Compute(new LayoutInput(caret, DotPicture, BadgeStyle.Dot, BadgePlacement.AboveRight, 1f, Work));
        Assert.Equal(0, p.Y);
    }

    [Fact]
    public void Dot_AtLeftScreenEdge_KeepsBodyInside()
    {
        // 왼쪽 위치인데 왼쪽에 자리가 없어 오른쪽으로 옮긴 뒤에도, 작업 영역 밖으로 나가는 것은 투명한 표시 자리뿐이다.
        var caret = new Rectangle(5, 300, 2, 20);
        var p = BadgeLayout.Compute(new LayoutInput(caret, DotPicture, BadgeStyle.Dot, BadgePlacement.AboveLeft, 1f, Work));
        Assert.Equal(7 + 3 - DotSide, p.X);
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
        Assert.Equal(work.Left - BadgeLayout.Side(BadgeStyle.Underline, 1f), p.X);   // 막대(몸통)는 작업 영역 안, 좌우 표시 자리는 밖
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
