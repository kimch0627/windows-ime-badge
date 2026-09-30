using System;
using System.Drawing;

namespace ImeBadge;

/// <summary>커서 사각형을 어느 후보에서 얻었는가(<see cref="TextCaret.Pick"/>).</summary>
public enum TextCaretSource
{
    /// <summary>후보가 하나도 없다.</summary>
    None,
    /// <summary>넓이 0인 커서 범위의 사각형 그대로.</summary>
    Caret,
    /// <summary>커서 바로 앞 글자의 오른쪽 끝.</summary>
    Prev,
    /// <summary>커서 바로 뒤 글자의 왼쪽 끝.</summary>
    Next,
}

/// <param name="Rect">커서 사각형(화면 좌표, 넓이 1). <see cref="From"/> 이 None 이면 비어 있다.</param>
/// <param name="From">어느 후보를 썼는가.</param>
/// <param name="CaretRejected">커서 범위 사각형이 있었지만 앞·뒤 글자 어느 쪽과도 맞닿지 않아 버렸다(로그용).</param>
public readonly record struct TextCaretPick(RectangleF Rect, TextCaretSource From, bool CaretRejected = false);

/// <summary>
/// UI Automation 이 주는 커서 위치 후보 셋(넓이 0인 커서 범위, 앞 글자, 뒤 글자) 중 무엇을 쓸지 정하는 순수 로직.
///
/// <para>보통은 커서 범위의 사각형이 가장 정확하다. 그런데 크롬 주소창(Chrome 154 의 OmniboxViewViews)은 커서가 어디에 있든
/// 커서 범위 사각형으로 입력칸 맨 앞(왼쪽 끝)의 1px 막대를 준다. 비어 있지 않은 그럴듯한 사각형이라 그대로 믿으면 배지가
/// 입력칸 맨 앞에 붙어 움직이지 않는다. 앞·뒤 글자의 사각형은 정확히 주므로, 커서 사각형이 그중 한쪽과 맞닿아 있을 때만 믿는다.</para>
///
/// <para>비유하면 "지금 몇 번째 칸에 서 있나" 라는 답이 수상할 때 "바로 앞 사람과 뒤 사람이 누구냐" 로 교차 확인하는 셈이다.</para>
/// </summary>
public static class TextCaret
{
    /// <param name="caret">넓이 0으로 접은 커서 범위의 사각형. 비었으면 null.</param>
    /// <param name="prev">커서 바로 앞 글자의 사각형. 앞 글자가 없거나(글 맨 앞) 줄바꿈이거나 조회에 실패했으면 null.</param>
    /// <param name="next">커서 바로 뒤 글자의 사각형을 구하는 함수. UI Automation 호출이 한 번 더 들므로 필요할 때만 부른다.</param>
    public static TextCaretPick Pick(RectangleF? caret, RectangleF? prev, Func<RectangleF?> next)
    {
        if (caret is { } c)
        {
            // 대조할 앞 글자가 없으면(글 맨 앞 등) 그대로 믿는다. 앞 글자의 오른쪽 끝에 붙어 있으면 맞는 값이다.
            if (prev is not { } p || Touches(c, p.Right, p)) return new(Bar(c.Left, c), TextCaretSource.Caret);
            // 앞 글자와 맞지 않아도 뒤 글자의 왼쪽에 붙어 있으면 맞는 값이다. 자동 줄바꿈된 줄의 맨 앞에서는 앞 글자가 윗줄 끝에 있다.
            if (next() is { } n && Touches(c, n.Left, n)) return new(Bar(c.Left, c), TextCaretSource.Caret);
            // 어느 쪽과도 맞닿지 않는다 → 입력칸 맨 앞 같은 엉뚱한 자리를 준 것이다. 앞 글자의 오른쪽 끝을 쓴다.
            return new(Bar(p.Right, p), TextCaretSource.Prev, CaretRejected: true);
        }
        // 커서 범위가 비었으면(커서가 글 끝에 있을 때 흔함) 앞 글자의 오른쪽 끝, 그것도 없으면 뒤 글자의 왼쪽.
        if (prev is { } q) return new(Bar(q.Right, q), TextCaretSource.Prev);
        if (next() is { } m) return new(Bar(m.Left, m), TextCaretSource.Next);
        return new(RectangleF.Empty, TextCaretSource.None);
    }

    /// <summary>
    /// 커서 사각형 <paramref name="c"/> 가 글자 사각형 <paramref name="ch"/> 의 가장자리(x = <paramref name="edge"/>)에 맞닿아 있는가.
    /// 같은 줄(세로로 겹침)이고, 커서 막대의 가운데가 가장자리에서 허용 오차 안에 있어야 한다.
    /// </summary>
    static bool Touches(RectangleF c, float edge, RectangleF ch)
    {
        bool sameLine = c.Top < ch.Bottom && ch.Top < c.Bottom;
        float center = c.Left + c.Width / 2;
        return sameLine && Math.Abs(center - edge) <= Tolerance(c.Height);
    }

    /// <summary>
    /// 허용 오차: 줄 높이의 15%(최소 2px). 커서 막대를 글자 경계 가운데에 두는 앱(넓이 2px 등)과 반올림 차이는 덮고,
    /// 글자 한 칸(보통 줄 높이의 30~60%)만큼 어긋난 값은 걸러 낸다.
    /// </summary>
    static float Tolerance(float lineHeight) => Math.Max(2f, lineHeight * 0.15f);

    static RectangleF Bar(float x, RectangleF line) => new(x, line.Top, 1, line.Height);
}
