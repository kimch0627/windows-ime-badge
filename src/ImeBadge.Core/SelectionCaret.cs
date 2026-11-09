using System;
using System.Drawing;

namespace ImeBadge;

/// <summary>
/// 글을 선택한 동안 RichEdit 입력칸(카카오톡 대화 입력칸, 워드패드 등)이 주는 Win32 caret 사각형을 바로잡는 순수 로직.
///
/// <para>RichEdit 은 선택이 있으면 caret 을 넓이 8px 막대로 바꾼다. 카카오톡에서 잰 값: 글 끝(x=66)에 커서만 있으면 (66,0)-(67,19),
/// Ctrl+A 로 전체 선택하면 (65,0)-(73,19). 배지는 caret 의 가운데(밑줄·가운데 자리)나 오른쪽 끝(오른쪽 자리)에 붙으므로 커서에서 4~8px
/// 밀려났다. 일부 카카오톡 화면에서는 caret 이 한 줄 위에 있다고 알려 와 배지가 선택한 글 위에 겹쳤다.</para>
///
/// <para>넓은 caret 의 왼쪽 끝은 커서가 있는 쪽 끝(Ctrl+A 면 글 끝, 오른쪽에서 왼쪽으로 고르면 글 앞)과 맞으므로 그대로 둔다.
/// 줄은 선택 시작·끝 글자의 위치(EM_POSFROMCHAR) 중 caret 에 가까운 쪽으로 정하고, 높이는 선택하기 전 같은 입력칸의 caret 높이를 쓴다.
/// 끝 위치는 그대로 쓰지 않는다: Ctrl+A 가 보이지 않는 문단 끝 표시까지 고르면 끝 위치가 실제 커서보다 2px 오른쪽이다.</para>
/// </summary>
public static class SelectionCaret
{
    /// <param name="caret">Win32 caret 사각형(화면 좌표).</param>
    /// <param name="start">선택 시작 글자의 왼쪽 위(화면 좌표). 모르면 null.</param>
    /// <param name="end">선택 끝 자리(마지막으로 고른 글자 다음)의 왼쪽 위(화면 좌표). 모르면 null.</param>
    /// <param name="lineHeight">선택하기 전 같은 입력칸의 caret 높이. 모르면 0(caret 높이를 쓴다).</param>
    /// <returns>넓이 1px 커서 사각형.</returns>
    public static Rectangle Fix(Rectangle caret, Point? start, Point? end, int lineHeight)
    {
        int height = lineHeight > 0 ? lineHeight : caret.Height;
        int top = Nearer(caret, start, end) is { } p ? p.Y : caret.Top;
        return new Rectangle(caret.Left, top, 1, height);
    }

    static Point? Nearer(Rectangle caret, Point? a, Point? b)
    {
        if (a is not { } pa) return b;
        if (b is not { } pb) return a;
        return Distance(caret, pa) <= Distance(caret, pb) ? pa : pb;
    }

    static long Distance(Rectangle caret, Point p) => Math.Abs((long)caret.Left - p.X) + Math.Abs((long)caret.Top - p.Y);
}
