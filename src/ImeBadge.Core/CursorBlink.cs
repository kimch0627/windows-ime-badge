using System.Collections.Generic;
using System.Drawing;

namespace ImeBadge;

/// <summary>
/// 두 화면 캡처의 차이로 깜빡이는 텍스트 커서 위치를 판정하는 순수 로직. 픽셀 비교는 WinForms 쪽(ImageCaret)에서 하고,
/// 여기서는 그 요약(바뀐 픽셀의 경계 상자와 개수)만 받아 "커서다/아니다"를 정한다. Win32 없이 단위 테스트가 가능하다.
///
/// 비유하면, 두 장의 사진을 겹쳐 달라진 부분을 표시했을 때 그것이 "깜빡이는 커서 한 칸"처럼 작고 꽉 찬 사각형이면 커서로 보고,
/// 화면 여기저기가 바뀌었으면(스크롤·출력) 커서가 아니라고 판단한다.
/// </summary>
public static class CursorBlink
{
    /// <summary>
    /// 바뀐 픽셀의 경계 상자(<paramref name="minX"/>~<paramref name="maxX"/>, 포함)와 개수로 커서 사각형을 판정한다.
    /// 커서가 아니라고 보면 null.
    /// </summary>
    /// <param name="changedCount">바뀐 픽셀 수(표본 스캔이면 실제값으로 환산해서 넘긴다).</param>
    /// <param name="maxCellW">커서로 인정할 최대 너비(px). 대략 글자 세 칸.</param>
    /// <param name="maxCellH">커서로 인정할 최대 높이(px). 대략 한 줄.</param>
    /// <param name="minCount">이보다 적게 바뀌면 잡음으로 보고 무시한다.</param>
    public static Rectangle? Evaluate(int minX, int minY, int maxX, int maxY, int changedCount, int maxCellW, int maxCellH, int minCount)
    {
        if (changedCount < minCount) return null;      // 변화 없음 또는 잡음
        if (maxX < minX || maxY < minY) return null;   // 바뀐 픽셀 없음
        int w = maxX - minX + 1, h = maxY - minY + 1;
        if (w > maxCellW || h > maxCellH) return null;  // 스크롤·출력 등 넓게 흩어진 변화
        if (h < 4) return null;                         // 커서라기엔 너무 낮다(밑줄 커서도 몇 px 높이)
        // 변화가 경계 상자를 촘촘히 채워야 커서(막대·블록·밑줄)다. 절반도 못 채우면 흩어진 변화로 본다.
        if (changedCount * 2 < w * h) return null;
        return new Rectangle(minX, minY, w, h);
    }

    /// <summary>
    /// 변화가 "화면이 크게 바뀐 것"(스크롤·긴 출력)인가. 커서가 아닌 변화 중에서도 이 정도면 이전에 찾아 둔 커서 위치를
    /// 더는 믿을 수 없으므로, 부르는 쪽은 기억을 버리고 모서리로 되돌아간다. 한두 줄 출력(Enter, 프롬프트 다시 그림)은
    /// 넓다고 보지 않아 옛 위치를 유지한다(커서가 다시 깜빡이면 그때 갱신).
    /// </summary>
    /// <param name="maxCellW">커서 한 칸의 최대 너비(px), <see cref="Evaluate"/> 와 같은 값.</param>
    /// <param name="maxCellH">커서 한 칸의 최대 높이(px), <see cref="Evaluate"/> 와 같은 값.</param>
    public static bool IsWide(int minX, int minY, int maxX, int maxY, int changedCount, int maxCellW, int maxCellH)
    {
        if (maxX < minX || maxY < minY) return false;
        int h = maxY - minY + 1;
        if (h > 2 * maxCellH) return true;                       // 두 줄 남짓보다 높게 퍼진 변화(스크롤)
        return changedCount > 4 * maxCellW * maxCellH;           // 바뀐 픽셀이 커서 네 칸어치보다 많음(넓은 출력)
    }

    /// <summary>
    /// 찾은 커서 사각형이 최근 배지 자리 중 하나와 겹치거나 <paramref name="margin"/> 안으로 붙어 있는가.
    /// 화면 캡처에는 우리 배지도 찍히는데, 배지를 옮긴 직후 화면 합성(DWM)이 늦으면 "옛 자리에서 배지가 사라진 흔적"이
    /// 작고 꽉 찬 사각형으로 잡혀 커서로 오인된다. 그 자리로 배지가 옮겨 가면 또 흔적이 생겨 배지가 계속 튀므로(되먹임) 걸러 낸다.
    /// 배지는 커서 옆에 놓이므로 진짜 커서가 배지와 겹치는 일은 없다.
    /// </summary>
    /// <param name="found">찾은 커서 사각형(배지와 같은 좌표계).</param>
    /// <param name="badges">최근 배지 사각형들. 빈 사각형은 무시한다.</param>
    /// <param name="margin">배지 가장자리(그림자·안티에일리어싱)까지 덮도록 넓힐 폭(px).</param>
    public static bool OverlapsAny(Rectangle found, IReadOnlyList<Rectangle> badges, int margin)
    {
        if (found.IsEmpty) return false;
        foreach (var b in badges)
        {
            if (b.IsEmpty) continue;
            if (Rectangle.Inflate(b, margin, margin).IntersectsWith(found)) return true;
        }
        return false;
    }
}
