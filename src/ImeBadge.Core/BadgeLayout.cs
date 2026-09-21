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

        Point pos;
        if (i.Style == BadgeStyle.Underline)
            pos = new Point(caret.Left + caret.Width / 2 - bs.Width / 2, caret.Bottom + 1);
        else if (i.Placement == BadgePlacement.AboveRight && !approx)
            pos = new Point(caret.Right + gap, caret.Top - bs.Height - gap);
        else
            pos = new Point(caret.Right + gap, caret.Bottom + gap);

        var area = i.WorkArea;
        if (pos.Y < area.Top) pos.Y = caret.Bottom + gap;                // 위에 자리가 없으면 아래로
        pos.X = Math.Max(area.Left, Math.Min(pos.X, area.Right - bs.Width));
        pos.Y = Math.Max(area.Top, Math.Min(pos.Y, area.Bottom - bs.Height));
        return pos;
    }
}
