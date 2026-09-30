using System;
using System.Drawing;
using System.Text;

namespace ImeBadge;

/// <summary>
/// 커서를 작은 자식 창으로 그리는 터미널의 커서 위치 찾기.
///
/// <para>Xshell 8 은 터미널 뷰(포커스 창, <c>AfxFrameOrView140u</c>) 안에 이름이 "CURSOR" 인 자식 창을 두고, 커서가 움직일 때마다
/// 그 창을 옮긴다(한글 조합 중에는 두 칸 넓이가 된다). Win32 caret·UI Automation 텍스트·MSAA caret 은 모두 없지만 이 창의 위치가
/// 곧 커서 위치다. 창 관리자에게 이름으로 묻기만 하면 되므로 아주 싸다.</para>
///
/// <para>비유하면 커서 모양의 작은 스티커를 화면 위에 붙여 옮기는 방식이라, 스티커가 어디 붙어 있는지만 보면 된다.</para>
/// </summary>
static class CursorWindow
{
    const string Name = "CURSOR";

    /// <summary>포커스 창 <paramref name="focus"/> 의 커서 창과 그 화면 사각형. 없거나 커서처럼 생기지 않았으면 null.</summary>
    public static (Rectangle Rect, IntPtr Hwnd)? Find(IntPtr focus, StringBuilder? dump)
    {
        if (focus == IntPtr.Zero) return null;
        var h = Native.FindWindowEx(focus, IntPtr.Zero, null, Name);
        if (h == IntPtr.Zero) return null;
        if (!Native.IsWindowVisible(h) || !Native.GetWindowRect(h, out var cr) || !Native.GetWindowRect(focus, out var fr))
        {
            dump?.Append(" wnd:hidden");
            return null;
        }
        var r = cr.ToRectangle();
        // 이름만 같은 다른 창을 커서로 오인하지 않도록 커서 한 칸 크기(글자 두어 칸 넓이, 한 줄 높이)이고 포커스 창 안에 보이는지 본다.
        // 스크롤백을 거슬러 올라가 커서가 화면 밖으로 나가면 창도 뷰 밖에 놓인다. 그때는 못 찾은 것으로 보고 모서리 배지에 맡긴다.
        if (r.Width < 1 || r.Height < 4 || r.Height > 200 || r.Width > r.Height * 3 || !fr.ToRectangle().IntersectsWith(r))
        {
            dump?.Append($" wnd:odd({r.X},{r.Y},{r.Width}x{r.Height})");
            return null;
        }
        dump?.Append($" caret:wnd('{Native.ClassName(h)}')");
        return (r, h);
    }
}
