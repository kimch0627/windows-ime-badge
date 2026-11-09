using System;
using System.Drawing;
using System.Text;

namespace ImeBadge;

/// <summary>
/// RichEdit 입력칸(카카오톡 대화 입력칸, 워드패드 등)에서 글을 선택한 동안 Win32 caret 을 바로잡는다(<see cref="SelectionCaret"/>).
/// 선택이 없을 때 본 caret 높이를 그 입력칸의 줄 높이로 기억해 둔다. UI 스레드에서만 부른다.
/// </summary>
static class RichEditCaret
{
    const uint EM_GETSEL = 0x00B0, EM_POSFROMCHAR = 0x00D6, AskTimeoutMs = 50;

    // 마지막으로 선택 없이 본 RichEdit 창과 그 caret 높이
    static IntPtr _hwnd;
    static int _lineHeight;

    /// <param name="hwnd">caret 을 가진 창(<c>GUITHREADINFO.hwndCaret</c>).</param>
    /// <param name="caret">그 창의 caret 사각형(화면 좌표).</param>
    public static Rectangle Adjust(IntPtr hwnd, Rectangle caret, StringBuilder? dump)
    {
        if (!Native.ClassName(hwnd).StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)) return caret;
        // 선택 시작·끝은 두 포인터로 받는다(반환값은 다른 프로세스에 SendMessageTimeout 으로 보내면 엉뚱하다, Focus.HasWin32Selection 참고).
        if (Native.SendMessageTimeout(hwnd, EM_GETSEL, out int start, out int end, Native.SMTO_ABORTIFHUNG, AskTimeoutMs, out _) == IntPtr.Zero)
            return caret;
        if (start == end)
        {
            _hwnd = hwnd;
            _lineHeight = caret.Height;
            return caret;
        }
        var fixedCaret = SelectionCaret.Fix(caret, Locate(hwnd, start), Locate(hwnd, end), hwnd == _hwnd ? _lineHeight : 0);
        dump?.Append($" caret:sel({caret.Left},{caret.Top},{caret.Width}x{caret.Height} -> {fixedCaret.Left},{fixedCaret.Top},h={fixedCaret.Height})");
        return fixedCaret;
    }

    /// <summary>
    /// 글자 위치(화면 좌표). RichEdit 3.0 이상은 입력칸(Edit)과 같은 꼴(wParam = 글자 번호, 반환값 = 클라이언트 좌표 x·y 16비트씩)을 받아,
    /// 포인터 없이 다른 프로세스(32·64비트 사이 포함)에 물을 수 있다. 실패하면 null.
    /// </summary>
    static Point? Locate(IntPtr hwnd, int index)
    {
        if (Native.SendMessageTimeout(hwnd, EM_POSFROMCHAR, (IntPtr)index, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, AskTimeoutMs, out IntPtr result) == IntPtr.Zero)
            return null;
        int packed = unchecked((int)(long)result);
        if (packed == -1) return null;   // 없는 글자 번호
        var p = new Native.POINT((short)(packed & 0xFFFF), (short)(packed >> 16));
        return Native.ClientToScreen(hwnd, ref p) ? new Point(p.X, p.Y) : null;
    }
}
