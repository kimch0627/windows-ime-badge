using System.Drawing;
using Xunit;

namespace ImeBadge.Tests;

public sealed class CursorBlinkTests
{
    // 대략 글자 세 칸 / 한 줄. 실제 코드도 DPI 로 이 정도를 준다.
    const int MaxW = 42, MaxH = 46, MinCount = 6;

    static Rectangle? Eval(int minX, int minY, int maxX, int maxY, int count) =>
        CursorBlink.Evaluate(minX, minY, maxX, maxY, count, MaxW, MaxH, MinCount);

    [Fact]
    public void BlockCursor_FilledSmallRect_IsAccepted()
    {
        // 8x18 블록 커서가 통째로 바뀜(꽉 참).
        var r = Eval(100, 200, 107, 217, 8 * 18);
        Assert.Equal(new Rectangle(100, 200, 8, 18), r);
    }

    [Fact]
    public void BarCursor_ThinTall_IsAccepted()
    {
        // 세로 막대 커서(2x18).
        var r = Eval(100, 200, 101, 217, 2 * 18);
        Assert.Equal(new Rectangle(100, 200, 2, 18), r);
    }

    [Fact]
    public void ScatteredChange_LargeBox_IsRejected()
    {
        // 스크롤: 넓은 영역이 바뀜(경계 상자가 커서 한 칸보다 큼).
        Assert.Null(Eval(0, 0, 300, 200, 5000));
    }

    [Fact]
    public void SparseChange_BigBoxFewPixels_IsRejected()
    {
        // 멀리 떨어진 두어 점만 바뀜 → 경계 상자는 크고 채움은 희박.
        Assert.Null(Eval(10, 10, 40, 50, 8));
    }

    [Fact]
    public void HalfEmptyBox_IsRejected()
    {
        // 커서 크기지만 절반도 안 채워짐(글자 획 일부만 바뀐 경우).
        Assert.Null(Eval(100, 200, 109, 219, 10 * 20 / 3));   // 약 33% 채움
    }

    [Fact]
    public void TooFewPixels_IsRejected() => Assert.Null(Eval(100, 200, 101, 203, 3));

    [Fact]
    public void TooShort_IsRejected() => Assert.Null(Eval(100, 200, 110, 202, 30));   // 높이 3

    [Fact]
    public void NoChange_IsRejected() => Assert.Null(Eval(int.MaxValue, int.MaxValue, -1, -1, 0));

    [Fact]
    public void TwoAdjacentCells_WhileTyping_IsAccepted()
    {
        // 타이핑: 옛 커서 칸 + 새 커서 칸(약 16px 폭, 18 높이)이 함께 바뀜. 세 칸 이내라 인정.
        var r = Eval(100, 200, 115, 217, 16 * 18 * 3 / 4);
        Assert.Equal(new Rectangle(100, 200, 16, 18), r);
    }

    static bool Wide(int minX, int minY, int maxX, int maxY, int count) =>
        CursorBlink.IsWide(minX, minY, maxX, maxY, count, MaxW, MaxH);

    [Fact]
    public void Wide_ScrollTallChange_IsWide()
    {
        // 스크롤: 세로로 여러 줄이 바뀜.
        Assert.True(Wide(0, 0, 300, 200, 5000));
    }

    [Fact]
    public void Wide_ManyPixelsInTwoLines_IsWide()
    {
        // 두 줄 높이지만 바뀐 픽셀이 커서 네 칸어치를 훌쩍 넘음(긴 출력).
        Assert.True(Wide(0, 100, 1200, 100 + 2 * MaxH - 1, 4 * MaxW * MaxH + 1));
    }

    [Fact]
    public void Wide_EnterMovesCursorOneLine_IsNotWide()
    {
        // Enter: 옛 커서 칸이 지워지고 다음 줄에 프롬프트 + 새 커서가 그려짐(두 줄 안, 픽셀 수 적당).
        Assert.False(Wide(0, 200, 120, 236, 900));
    }

    [Fact]
    public void Wide_BlinkingCursor_IsNotWide()
    {
        Assert.False(Wide(100, 200, 107, 217, 8 * 18));
    }

    [Fact]
    public void Wide_NoChange_IsNotWide()
    {
        Assert.False(Wide(int.MaxValue, int.MaxValue, -1, -1, 0));
    }

    [Fact]
    public void OverlapsAny_TraceOfOldBadgePosition_IsDetected()
    {
        // 배지가 (100,100) 40x40 에 있다가 옮겨 간 뒤, 그 자리에 남은 흔적을 커서로 찾은 경우.
        var badges = new[] { Rectangle.Empty, new Rectangle(100, 100, 40, 40) };
        Assert.True(CursorBlink.OverlapsAny(new Rectangle(110, 110, 20, 20), badges, 4));
    }

    [Fact]
    public void OverlapsAny_EdgeWithinMargin_IsDetected()
    {
        // 배지 오른쪽 가장자리에서 2px 떨어진 그림자 흔적.
        var badges = new[] { new Rectangle(100, 100, 40, 40) };
        Assert.True(CursorBlink.OverlapsAny(new Rectangle(142, 110, 2, 18), badges, 4));
    }

    [Fact]
    public void OverlapsAny_RealCursorAwayFromBadge_IsNotDetected()
    {
        var badges = new[] { new Rectangle(100, 100, 40, 40) };
        Assert.False(CursorBlink.OverlapsAny(new Rectangle(300, 110, 8, 18), badges, 4));
    }

    [Fact]
    public void OverlapsAny_NoBadges_IsNotDetected()
    {
        Assert.False(CursorBlink.OverlapsAny(new Rectangle(0, 0, 8, 18), new[] { Rectangle.Empty }, 4));
        Assert.False(CursorBlink.OverlapsAny(new Rectangle(0, 0, 8, 18), System.Array.Empty<Rectangle>(), 4));
    }
}
