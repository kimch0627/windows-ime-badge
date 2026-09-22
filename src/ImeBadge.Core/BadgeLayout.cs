using System;
using System.Drawing;

namespace ImeBadge;

/// <summary>배지 위치 계산에 필요한 입력. 모두 화면 좌표(px).</summary>
/// <param name="Caret">caret 사각형. 높이 0 이면 "입력창 모서리로 근사한 위치"라는 뜻(UIA 경로).</param>
/// <param name="Badge">그려진 배지 비트맵 크기.</param>
/// <param name="WorkArea">caret 이 속한 모니터의 작업 영역(작업 표시줄 제외).</param>
public readonly record struct LayoutInput(
    Rectangle Caret, Size Badge, BadgeStyle Style, BadgePlacement Placement, float Scale, Rectangle WorkArea);

/// <summary>caret 옆 어디에 배지를 둘지 정하는 순수 계산. Win32 에 의존하지 않아 단위 테스트가 가능하다.</summary>
public static class BadgeLayout
{
    /// <summary>caret 과 배지 사이 간격(px, 배율 1 기준).</summary>
    public const int Gap = 3;

    public static Point Compute(in LayoutInput i)
    {
        int gap = (int)Math.Round(Gap * i.Scale);
        var caret = i.Caret;
        var bs = i.Badge;
        bool approx = caret.Height == 0;   // 근사 위치면 항상 아래쪽(위쪽은 입력창 내부라 글자를 가림)
        bool left = i.Placement is BadgePlacement.AboveLeft or BadgePlacement.BelowLeft;
        bool above = i.Placement is BadgePlacement.AboveRight or BadgePlacement.AboveLeft;
        int x = left ? caret.Left - gap - bs.Width : caret.Right + gap;

        Point pos;
        if (i.Style == BadgeStyle.Underline)
            pos = new Point(caret.Left + caret.Width / 2 - bs.Width / 2, caret.Bottom + 1);
        else if (above && !approx)
            pos = new Point(x, caret.Top - bs.Height - gap);
        else
            pos = new Point(x, caret.Bottom + gap);

        var area = i.WorkArea;
        if (pos.Y < area.Top) pos.Y = caret.Bottom + gap;                // 위에 자리가 없으면 아래로
        if (left && pos.X < area.Left) pos.X = caret.Right + gap;        // 왼쪽에 자리가 없으면 오른쪽으로
        pos.X = Math.Max(area.Left, Math.Min(pos.X, area.Right - bs.Width));
        pos.Y = Math.Max(area.Top, Math.Min(pos.Y, area.Bottom - bs.Height));
        return pos;
    }

    /// <summary>모서리 배지가 창 가장자리에서 떨어지는 거리(px, 배율 1 기준).</summary>
    public const int CornerInset = 6;

    /// <summary>
    /// caret 을 못 찾는 앱(Xshell 같은 터미널)용. 포커스 창 <paramref name="window"/> 의 왼쪽 아래 모서리 안쪽에 배지를 둔다.
    /// 터미널은 프롬프트가 보통 아래쪽에 있어 그 근처가 눈에 잘 띈다. 위치 설정(위/아래·왼쪽/오른쪽)은 적용하지 않고,
    /// 작업 영역 밖으로 나가면 안쪽으로 밀어 넣는다.
    /// </summary>
    public static Point Corner(Rectangle window, Size badge, float scale, Rectangle workArea)
    {
        int inset = (int)Math.Round(CornerInset * scale);
        var pos = new Point(window.Left + inset, window.Bottom - inset - badge.Height);
        pos.X = Math.Max(workArea.Left, Math.Min(pos.X, workArea.Right - badge.Width));
        pos.Y = Math.Max(workArea.Top, Math.Min(pos.Y, workArea.Bottom - badge.Height));
        return pos;
    }
}
