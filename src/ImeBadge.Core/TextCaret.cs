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
/// <param name="Unverified">대조할 글자는 있었지만 그 글자들의 사각형이 망가져(<see cref="TextCaret.IsGlyph"/> 가 아님) 맞는 자리인지 알 수 없다.
/// 부르는 쪽은 다른 출처(MSAA 가상 caret)가 있으면 그것을 쓴다.</param>
public readonly record struct TextCaretPick(RectangleF Rect, TextCaretSource From, bool CaretRejected = false, bool Unverified = false);

/// <summary>
/// UI Automation 이 주는 커서 위치 후보 셋(넓이 0인 커서 범위, 앞 글자, 뒤 글자) 중 무엇을 쓸지 정하는 순수 로직.
///
/// <para>보통은 커서 범위의 사각형이 가장 정확하다. 그런데 크롬 주소창(Chrome 154 의 OmniboxViewViews)은 커서가 어디에 있든
/// 커서 범위 사각형으로 입력칸 맨 앞(왼쪽 끝)의 1px 막대를 준다. 비어 있지 않은 그럴듯한 사각형이라 그대로 믿으면 배지가
/// 입력칸 맨 앞에 붙어 움직이지 않는다. 앞·뒤 글자의 사각형은 정확히 주므로, 커서 사각형이 그중 한쪽과 맞닿아 있을 때만 믿는다.</para>
///
/// <para>같은 Chrome 이라도 어떤 프로필(창)에서는 글자 사각형까지 망가져, 어느 글자를 물어도 그 1px 막대를 준다(글자 내용은 맞다).
/// 그러면 커서 막대와 앞 글자가 "맞닿아" 보여 대조를 통과해 버린다. 그래서 넓이가 1px 이하인 글자 사각형은 믿지 않고,
/// 대조할 수 없으면 <see cref="TextCaretPick.Unverified"/> 로 알린다(부르는 쪽이 MSAA 가상 caret 으로 바꾼다).</para>
///
/// <para>비유하면 "지금 몇 번째 칸에 서 있나" 라는 답이 수상할 때 "바로 앞 사람과 뒤 사람이 누구냐" 로 교차 확인하는 셈이다.</para>
/// </summary>
public static class TextCaret
{
    /// <param name="caret">넓이 0으로 접은 커서 범위의 사각형. 비었으면 null.</param>
    /// <param name="prev">커서 바로 앞 글자의 사각형. 앞 글자가 없거나(글 맨 앞) 줄바꿈이거나 조회에 실패했으면 null.
    /// 망가진 사각형(넓이 1px 이하)도 그대로 넘긴다: "앞 글자가 없다" 와 "앞 글자는 있는데 위치를 믿을 수 없다" 를 구별해야 한다.</param>
    /// <param name="next">커서 바로 뒤 글자의 사각형을 구하는 함수. UI Automation 호출이 한 번 더 들므로 필요할 때만 부른다.</param>
    public static TextCaretPick Pick(RectangleF? caret, RectangleF? prev, Func<RectangleF?> next)
    {
        RectangleF? p = prev is { } pr && IsGlyph(pr) ? pr : null;
        // 뒤 글자는 한 번만 묻는다(UI Automation 호출). 망가진 사각형은 쓸 수 있는 글자로 보지 않는다.
        bool asked = false;
        RectangleF? nextRaw = null;
        RectangleF? NextRaw() { if (!asked) { asked = true; nextRaw = next(); } return nextRaw; }
        RectangleF? Next() => NextRaw() is { } nr && IsGlyph(nr) ? nr : null;

        if (caret is { } c)
        {
            // 앞 글자가 없으면(글 맨 앞 등) 대조할 것이 없으니 그대로 믿는다. 다만 뒤 글자 사각형이 망가졌으면 알린다: 맨 앞의 값은 맞지만,
            // 다른 자리에서 대신 쓰는 MSAA 가상 caret 과 높이가 달라 Home 을 누를 때마다 배지가 위아래로 튄다.
            if (prev is null) return new(Bar(c.Left, c), TextCaretSource.Caret, Unverified: NextRaw() is { } b && !IsGlyph(b));
            // 앞 글자의 오른쪽 끝에 붙어 있으면 맞는 값이다.
            if (p is { } pv && Touches(c, pv.Right, pv)) return new(Bar(c.Left, c), TextCaretSource.Caret);
            // 앞 글자와 맞지 않아도 뒤 글자의 왼쪽에 붙어 있으면 맞는 값이다. 자동 줄바꿈된 줄의 맨 앞에서는 앞 글자가 윗줄 끝에 있다.
            var n = Next();
            if (n is { } nv && Touches(c, nv.Left, nv)) return new(Bar(c.Left, c), TextCaretSource.Caret);
            // 어느 쪽과도 맞닿지 않는다 → 입력칸 맨 앞 같은 엉뚱한 자리를 준 것이다. 앞 글자의 오른쪽 끝을 쓴다.
            if (p is { } pw) return new(Bar(pw.Right, pw), TextCaretSource.Prev, CaretRejected: true);
            // 앞 글자 사각형이 망가졌으면 뒤 글자의 왼쪽을 쓴다.
            if (n is { } nw) return new(Bar(nw.Left, nw), TextCaretSource.Next, CaretRejected: true);
            // 앞뒤 글자가 모두 망가졌다 → 커서 사각형이 맞는지 알 수 없다.
            return new(Bar(c.Left, c), TextCaretSource.Caret, Unverified: true);
        }
        // 커서 범위가 비었으면(커서가 글 끝에 있을 때 흔함) 앞 글자의 오른쪽 끝, 그것도 없으면 뒤 글자의 왼쪽.
        if (p is { } q) return new(Bar(q.Right, q), TextCaretSource.Prev);
        if (Next() is { } m) return new(Bar(m.Left, m), TextCaretSource.Next);
        // 망가진 사각형밖에 없으면 그 자리를 쓰되 확인하지 못했다고 알린다.
        if (prev is { } bp) return new(Bar(bp.Right, bp), TextCaretSource.Prev, Unverified: true);
        if (NextRaw() is { } bn) return new(Bar(bn.Left, bn), TextCaretSource.Next, Unverified: true);
        return new(RectangleF.Empty, TextCaretSource.None);
    }

    /// <summary>
    /// 글자 사각형으로 믿을 수 있는가: 넓이가 1px 보다 넓어야 한다. 실제 글자는 'i'·공백처럼 좁아도 2px 이상이고,
    /// 망가진 값은 커서 막대처럼 정확히 1px 이다(Chrome 주소창이 입력칸 맨 앞에 주는 막대).
    /// </summary>
    public static bool IsGlyph(RectangleF ch) => ch.Width > 1.5f && ch.Height > 0;

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
