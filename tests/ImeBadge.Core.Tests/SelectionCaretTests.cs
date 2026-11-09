using System.Drawing;
using Xunit;

namespace ImeBadge.Tests;

public sealed class SelectionCaretTests
{
    // 카카오톡 대화 입력칸(배율 125%)에서 잰 값. 입력칸 왼쪽 위가 화면 (1375,924), "asdfsadfsadf" 는 x=1375~1441, 줄 높이 19.
    static readonly Point TextStart = new(1375, 924), TextEnd = new(1441, 924);

    [Fact]
    public void SelectAll_WideCaretAtEnd_BecomesOnePixelCaretAtEnd()
    {
        // Ctrl+A: caret 이 글 끝에서 8px 넓이로 바뀐다(1440~1448).
        var caret = SelectionCaret.Fix(new Rectangle(1440, 924, 8, 19), TextStart, TextEnd, lineHeight: 19);
        Assert.Equal(new Rectangle(1440, 924, 1, 19), caret);
    }

    [Fact]
    public void SelectedTowardStart_WideCaretAtStart_StaysAtStart()
    {
        // 글 끝에서 Shift+Home 이나 오른쪽에서 왼쪽으로 끌어 고르면 커서는 글 앞에 있다.
        var caret = SelectionCaret.Fix(new Rectangle(1375, 924, 8, 19), TextStart, TextEnd, lineHeight: 19);
        Assert.Equal(new Rectangle(1375, 924, 1, 19), caret);
    }

    [Fact]
    public void CaretReportedOneLineUp_MovesBackToSelectedLine()
    {
        // 신고된 화면: 선택한 글은 y=757 줄에 있는데 caret 이 한 줄 위(위쪽 끝 742)라 밑줄 배지가 글 위에 겹쳤다.
        var caret = SelectionCaret.Fix(new Rectangle(23, 742, 8, 15), new Point(24, 757), new Point(110, 757), lineHeight: 15);
        Assert.Equal(new Rectangle(23, 757, 1, 15), caret);
    }

    [Fact]
    public void BrokenCaretHeight_UsesLineHeightSeenBeforeSelecting()
    {
        var caret = SelectionCaret.Fix(new Rectangle(1440, 924, 8, 2), TextStart, TextEnd, lineHeight: 19);
        Assert.Equal(19, caret.Height);
        Assert.Equal(924, caret.Top);
    }

    [Fact]
    public void MultiLineSelection_UsesLineOfNearerEnd()
    {
        // 둘째 줄 중간(10,21)에서 넷째 줄(30,63)까지 고르고 커서는 넷째 줄 끝에 있다.
        var caret = SelectionCaret.Fix(new Rectangle(30, 63, 8, 21), new Point(10, 21), new Point(30, 63), lineHeight: 21);
        Assert.Equal(new Rectangle(30, 63, 1, 21), caret);
    }

    [Fact]
    public void UnknownPositions_KeepsCaretButNarrowsIt()
    {
        var caret = SelectionCaret.Fix(new Rectangle(1440, 924, 8, 19), null, null, lineHeight: 0);
        Assert.Equal(new Rectangle(1440, 924, 1, 19), caret);
    }

    [Fact]
    public void OnlyOnePositionKnown_UsesItsLine()
    {
        var caret = SelectionCaret.Fix(new Rectangle(1440, 905, 8, 19), null, TextEnd, lineHeight: 0);
        Assert.Equal(new Rectangle(1440, 924, 1, 19), caret);
    }
}
