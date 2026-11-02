using System;
using System.Drawing;

namespace ImeBadge;

/// <param name="Location">말풍선 몸통(꼬리 제외)의 왼쪽 위 화면 좌표.</param>
/// <param name="Below">말풍선이 커서 아래에 있는가. 꼬리는 아래면 몸통 위쪽, 위면 몸통 아래쪽에 붙는다.</param>
/// <param name="TailX">몸통 왼쪽에서 꼬리 끝까지의 거리(px). 꼬리 끝이 커서를 가리킨다.</param>
public readonly record struct CalloutPlacement(Point Location, bool Below, int TailX);

/// <summary>
/// 커서 옆 말풍선(실험 기능의 안내·경고)을 어디에 둘지 정하는 순수 계산. <see cref="BadgeLayout"/> 처럼 Win32 없이 테스트한다.
/// 말풍선은 배지와 겹치지 않게 배지의 반대쪽(배지가 커서 위면 아래)에 두고, 꼬리 끝이 커서 왼쪽 끝을 가리키게 한다.
/// 화면(작업 영역) 끝에 자리가 없으면 위아래를 바꾸고, 좌우는 화면 안으로 밀어 넣되 꼬리는 계속 커서를 가리킨다.
/// 가리면 안 되는 창을 주면 그 창과 겹치지 않는 자리를 먼저 찾는다(포커스 뺏김 경고가 앞으로 나온 창의 내용을 덮지 않게).
/// </summary>
public static class CalloutLayout
{
    /// <param name="caret">커서 사각형(화면 좌표). 높이 0 이면 입력칸 모서리로 근사한 위치라 늘 아래에 둔다(위는 입력칸 안이다).</param>
    /// <param name="body">몸통 크기(꼬리 제외).</param>
    /// <param name="tail">꼬리 높이.</param>
    /// <param name="gap">커서와 꼬리 끝 사이 틈.</param>
    /// <param name="area">커서가 있는 모니터의 작업 영역.</param>
    /// <param name="preferBelow">자리가 있으면 커서 아래에 둔다(배지가 커서 위에 있을 때).</param>
    /// <param name="tailInset">몸통 왼쪽 끝에서 꼬리까지의 최소 거리. 둥근 모서리에 꼬리가 걸리지 않게.</param>
    public static CalloutPlacement Compute(Rectangle caret, Size body, int tail, int gap, Rectangle area, bool preferBelow, int tailInset)
    {
        int total = body.Height + tail;
        bool below = preferBelow || caret.Height == 0;
        bool roomBelow = caret.Bottom + gap + total <= area.Bottom;
        bool roomAbove = caret.Top - gap - total >= area.Top;
        if (below && !roomBelow && roomAbove && caret.Height > 0) below = false;
        else if (!below && !roomAbove && roomBelow) below = true;

        int y = below ? caret.Bottom + gap + tail : caret.Top - gap - total;
        y = Math.Max(area.Top + (below ? tail : 0), Math.Min(y, area.Bottom - total + (below ? tail : 0)));

        int x = caret.Left - tailInset;
        x = Math.Max(area.Left, Math.Min(x, area.Right - body.Width));
        int tailX = Math.Clamp(caret.Left - x, Math.Min(tailInset, body.Width / 2), Math.Max(body.Width - tailInset, body.Width / 2));
        return new CalloutPlacement(new Point(x, y), below, tailX);
    }

    /// <summary>
    /// <see cref="Compute(Rectangle, Size, int, int, Rectangle, bool, int)"/> 와 같되, 말풍선 몸통이 <paramref name="avoid"/> 와 겹치지 않는
    /// 자리를 이 순서로 찾는다.
    /// <list type="number">
    /// <item>커서 옆 원래 자리.</item>
    /// <item>커서의 반대쪽(아래 ↔ 위). 꼬리는 계속 커서를 가리킨다.</item>
    /// <item>그 창의 바깥, 커서에 가까운 가장자리(아래·위)부터. 꼬리는 커서와 같은 x 의 창 가장자리를 가리킨다.
    ///   커서가 그 창에 덮여 있으면 커서를 가리켜도 보이지 않으니, 덮은 창을 가리키는 편이 뜻이 통한다.</item>
    /// </list>
    /// 어디에도 자리가 없으면(창이 화면을 거의 덮음) 1 의 자리. <paramref name="avoid"/> 가 비어 있으면 원래 계산 그대로다.
    /// </summary>
    /// <param name="avoid">가리면 안 되는 창(화면 좌표). 포커스 뺏김 경고에서는 앞으로 나온 창.</param>
    public static CalloutPlacement Compute(Rectangle caret, Size body, int tail, int gap, Rectangle area, bool preferBelow, int tailInset, Rectangle avoid)
    {
        var first = Compute(caret, body, tail, gap, area, preferBelow, tailInset);
        if (avoid.Width <= 0 || avoid.Height <= 0 || !Overlaps(first, body, avoid)) return first;

        if (caret.Height > 0)
        {
            var flipped = Compute(caret, body, tail, gap, area, !first.Below, tailInset);
            if (flipped.Below != first.Below && !Overlaps(flipped, body, avoid)) return flipped;
        }

        // 창 가장자리 한 줄을 커서처럼 보고 그 바깥에 둔다(아래면 창 아래, 위면 창 위). 꼬리는 창의 둥근 모서리를 피해 안쪽을 가리킨다.
        int x = Math.Clamp(caret.Left, avoid.Left + tailInset, Math.Max(avoid.Left + tailInset, avoid.Right - tailInset));
        var edge = new Rectangle(x, avoid.Top, 1, avoid.Height);
        int middle = caret.Top + caret.Height / 2;
        bool belowFirst = avoid.Bottom - middle <= middle - avoid.Top;
        foreach (bool below in new[] { belowFirst, !belowFirst })
        {
            var outside = Compute(edge, body, tail, gap, area, below, tailInset);
            if (outside.Below == below && !Overlaps(outside, body, avoid)) return outside;
        }
        return first;
    }

    static bool Overlaps(CalloutPlacement p, Size body, Rectangle window) => new Rectangle(p.Location, body).IntersectsWith(window);
}
