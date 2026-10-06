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
}
