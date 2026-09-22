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
}
