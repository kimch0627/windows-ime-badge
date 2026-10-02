using System;
using System.Drawing;

namespace ImeBadge;

/// <summary>배지 위치 계산에 필요한 입력. 모두 화면 좌표(px).</summary>
/// <param name="Caret">caret 사각형. 높이 0 이면 "입력창 모서리로 근사한 위치"라는 뜻(UIA 경로).</param>
/// <param name="Badge">그려진 배지 비트맵 크기.</param>
/// <param name="WorkArea">caret 이 속한 모니터의 작업 영역(작업 표시줄 제외).</param>
/// <param name="RestingBadge">
/// 한/영 전환 효과(펄스)로 잠깐 커진 배지면 원래 크기. 자리가 모자라 반대쪽으로 옮길지는 이 크기로 정한다.
/// 커진 크기로 정하면 가장자리 근처에서 0.26초 동안 반대쪽으로 튀었다 돌아온다. 비우면 <paramref name="Badge"/>.
/// </param>
public readonly record struct LayoutInput(
    Rectangle Caret, Size Badge, BadgeStyle Style, BadgePlacement Placement, float Scale, Rectangle WorkArea, Size RestingBadge = default);

/// <summary>caret 옆 어디에 배지를 둘지 정하는 순수 계산. Win32 에 의존하지 않아 단위 테스트가 가능하다.</summary>
public static class BadgeLayout
{
    /// <summary>caret 과 배지 사이 간격(px, 배율 1 기준).</summary>
    public const int Gap = 3;

    /// <summary>
    /// 점·밑줄 그림의 몸통 아래에 늘 비워 두는 특수 키 표시 자리(px). 글자가 없는 두 모양은 표시(▲ ▁ ■, <see cref="BadgeMark"/>)를
    /// 몸통 아래 한 줄에 그린다. 표시가 있든 없든 그림 크기를 같게 해 Caps Lock·Shift·Insert 를 바꿔도 몸통이 움직이지 않는다
    /// (렌더러도 이 값으로 그림을 늘린다). 글자 배지는 표시가 배지 안팎 모서리에 들어가 0.
    /// </summary>
    public static int Tail(BadgeStyle style, float scale) => style switch
    {
        BadgeStyle.Dot => (int)Math.Ceiling(4 * scale),         // 점 그림은 원래 아래에 그림자 여백이 있어 조금만 더한다
        BadgeStyle.Underline => (int)Math.Ceiling(6 * scale),
        _ => 0,
    };

    /// <summary>
    /// 점·밑줄 그림의 몸통 좌우에 각각 늘 비워 두는 표시 자리(px). 아래 줄의 ▲ 는 왼쪽, ■ 는 오른쪽 자리에 그려
    /// 점(지름 9)·밑줄(폭 16)보다 넓어진다. 아래 자리(<see cref="Tail"/>)와 함께 위치 계산에서는 빼고 몸통으로 자리를 정하므로,
    /// 점·밑줄은 예전(1.13.0) 자리 그대로이고 표시 자리만 caret 과의 틈이나 화면 밖으로 나갈 수 있다(투명이라 보이지 않는다).
    /// </summary>
    public static int Side(BadgeStyle style, float scale) => style switch
    {
        // 아래 줄은 가운데에서 ▲ 왼쪽 끝까지 약 11px(+테두리·그림자). 점은 반지름 6.5, 밑줄은 10 짜리 그림에 이만큼 더한다.
        BadgeStyle.Dot => (int)Math.Ceiling(5.5f * scale + 0.5f),
        BadgeStyle.Underline => (int)Math.Ceiling(2 * scale + 0.5f),
        _ => 0,
    };

    /// <summary>
    /// 배지 그림의 왼쪽 위 화면 좌표. 점·밑줄 그림은 둘레의 표시 자리(<see cref="Side"/>·<see cref="Tail"/>)를 뺀 몸통으로 자리를 정한 뒤
    /// 그만큼 되돌린다.
    /// </summary>
    public static Point Compute(in LayoutInput i)
    {
        int side = Side(i.Style, i.Scale), tail = Tail(i.Style, i.Scale);
        if (side == 0 && tail == 0) return Place(i);
        Size Body(Size s) => s.IsEmpty ? s : new Size(Math.Max(1, s.Width - 2 * side), Math.Max(1, s.Height - tail));
        var p = Place(i with { Badge = Body(i.Badge), RestingBadge = Body(i.RestingBadge) });
        return new Point(p.X - side, p.Y);
    }

    /// <summary>몸통(표시 자리를 뺀 그림) 크기로 자리를 정한다.</summary>
    static Point Place(in LayoutInput i)
    {
        int gap = (int)Math.Round(Gap * i.Scale);
        var caret = i.Caret;
        var bs = i.Badge;
        var fit = i.RestingBadge.IsEmpty ? bs : i.RestingBadge;   // 반대쪽 대피를 정할 크기
        var area = i.WorkArea;
        bool approx = caret.Height == 0;   // 근사 위치면 항상 아래쪽(위쪽은 입력창 내부라 글자를 가림)
        bool center = i.Placement is BadgePlacement.Above or BadgePlacement.Below;
        bool left = i.Placement is BadgePlacement.AboveLeft or BadgePlacement.BelowLeft;
        bool above = i.Placement is BadgePlacement.AboveRight or BadgePlacement.AboveLeft or BadgePlacement.Above;
        int centerX = caret.Left + caret.Width / 2 - bs.Width / 2;
        int x = center ? centerX : left ? caret.Left - gap - bs.Width : caret.Right + gap;
        if (left && caret.Left - gap - fit.Width < area.Left) x = caret.Right + gap;   // 왼쪽에 자리가 없으면 오른쪽으로

        Point pos;
        if (i.Style == BadgeStyle.Underline)
            pos = new Point(centerX, caret.Bottom + 1);
        else
        {
            bool up = above && !approx;
            bool roomAbove = caret.Top - gap - fit.Height >= area.Top;
            bool roomBelow = caret.Bottom + gap + fit.Height <= area.Bottom;
            if (up && !roomAbove) up = false;                               // 위에 자리가 없으면 아래로
            else if (!up && !approx && !roomBelow && roomAbove) up = true;  // 아래에 자리가 없으면 위로(밀어 올리면 caret 을 가린다)
            pos = new Point(x, up ? caret.Top - gap - bs.Height : caret.Bottom + gap);
        }

        pos.X = Math.Max(area.Left, Math.Min(pos.X, area.Right - bs.Width));
        pos.Y = Math.Max(area.Top, Math.Min(pos.Y, area.Bottom - bs.Height));
        return pos;
    }
}
