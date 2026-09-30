using System.Drawing;
using Xunit;

namespace ImeBadge.Tests;

public sealed class TextCaretTests
{
    // 좌표는 Chrome 154 에서 실제로 잰 값(배율 100%). 주소창은 x=300 에서 시작하고 줄 높이 27, 웹 페이지 textarea 는 줄 높이 13.
    static readonly RectangleF OmniboxStart = new(300, 11, 1, 27);   // 주소창이 커서 위치와 상관없이 주는 커서 범위 사각형

    static RectangleF? None() => null;

    [Fact]
    public void Omnibox_CaretAtEnd_BogusCaretRect_UsesPrevRightEdge()
    {
        // "hello world" 끝에 커서. 커서 범위는 입력칸 맨 앞(300), 앞 글자 'd' 는 362~372.
        var pick = TextCaret.Pick(OmniboxStart, new RectangleF(362, 11, 10, 27), None);
        Assert.Equal(TextCaretSource.Prev, pick.From);
        Assert.True(pick.CaretRejected);
        Assert.Equal(372, pick.Rect.Left);
        Assert.Equal(27, pick.Rect.Height);
    }

    [Fact]
    public void Omnibox_CaretInMiddle_BogusCaretRect_UsesPrevRightEdge()
    {
        // "hello wo|rld": 앞 글자 'o' 346~354, 뒤 글자 'r' 354~359. 커서 범위는 여전히 300.
        var pick = TextCaret.Pick(OmniboxStart, new RectangleF(346, 11, 8, 27), () => new RectangleF(354, 11, 5, 27));
        Assert.Equal(TextCaretSource.Prev, pick.From);
        Assert.Equal(354, pick.Rect.Left);
    }

    [Fact]
    public void Omnibox_CaretAtStart_NoPrevChar_TrustsCaretRect()
    {
        // 맨 앞(Home)에서는 커서 범위 300 이 실제로 맞다. 앞 글자가 없으니 대조 없이 쓴다.
        var pick = TextCaret.Pick(OmniboxStart, null, () => new RectangleF(300, 11, 8, 27));
        Assert.Equal(TextCaretSource.Caret, pick.From);
        Assert.False(pick.CaretRejected);
        Assert.Equal(300, pick.Rect.Left);
    }

    [Fact]
    public void Textarea_CaretRectTouchesPrevChar_IsTrusted()
    {
        // 웹 페이지 textarea: 커서 범위 145, 앞 글자 'd' 138~145.
        var pick = TextCaret.Pick(new RectangleF(145, 96, 1, 13), new RectangleF(138, 96, 7, 13), None);
        Assert.Equal(TextCaretSource.Caret, pick.From);
        Assert.Equal(new RectangleF(145, 96, 1, 13), pick.Rect);
    }

    [Fact]
    public void CenteredTwoPixelCaret_IsTrusted()
    {
        // 글자 경계(145)를 가운데에 둔 2px 커서(144~146).
        var pick = TextCaret.Pick(new RectangleF(144, 96, 2, 13), new RectangleF(138, 96, 7, 13), None);
        Assert.Equal(TextCaretSource.Caret, pick.From);
    }

    [Fact]
    public void WrappedLineStart_CaretRectTouchesNextChar_IsTrusted()
    {
        // 자동 줄바꿈된 둘째 줄 맨 앞: 앞 글자는 윗줄 끝(290), 커서와 뒤 글자는 아랫줄 맨 앞(68).
        var pick = TextCaret.Pick(new RectangleF(68, 111, 1, 13), new RectangleF(283, 96, 7, 13), () => new RectangleF(68, 111, 7, 13));
        Assert.Equal(TextCaretSource.Caret, pick.From);
        Assert.Equal(68, pick.Rect.Left);
    }

    [Fact]
    public void CaretRectOneCharOff_IsRejected()
    {
        // 한 글자(8px)만큼 어긋나도 버린다: 주소창에서 첫 글자 뒤에 커서가 있을 때(앞 글자 300~308, 커서 범위 300).
        var pick = TextCaret.Pick(OmniboxStart, new RectangleF(300, 11, 8, 27), None);
        Assert.Equal(TextCaretSource.Prev, pick.From);
        Assert.Equal(308, pick.Rect.Left);
    }

    [Fact]
    public void CaretRectOnOtherLine_IsRejected()
    {
        // 같은 x 라도 다른 줄이면 맞닿은 것이 아니다.
        var pick = TextCaret.Pick(new RectangleF(145, 11, 1, 13), new RectangleF(138, 96, 7, 13), None);
        Assert.Equal(TextCaretSource.Prev, pick.From);
        Assert.Equal(96, pick.Rect.Top);
    }

    [Fact]
    public void EmptyCaretRect_UsesPrevRightEdge()
    {
        // 커서 범위가 비었을 때(커서가 글 끝): 앞 글자 'ㅅ' 435~450 의 오른쪽 끝.
        var pick = TextCaret.Pick(null, new RectangleF(435, 11, 15, 27), None);
        Assert.Equal(TextCaretSource.Prev, pick.From);
        Assert.False(pick.CaretRejected);
        Assert.Equal(450, pick.Rect.Left);
    }

    [Fact]
    public void EmptyCaretRect_NoPrev_UsesNextLeftEdge()
    {
        // 줄바꿈 바로 뒤(앞 글자를 쓰지 않음): 뒤 글자 's' 의 왼쪽.
        var pick = TextCaret.Pick(null, null, () => new RectangleF(68, 111, 7, 13));
        Assert.Equal(TextCaretSource.Next, pick.From);
        Assert.Equal(new RectangleF(68, 111, 1, 13), pick.Rect);
    }

    [Fact]
    public void NoCandidates_IsNone()
    {
        Assert.Equal(TextCaretSource.None, TextCaret.Pick(null, null, None).From);
    }
}
