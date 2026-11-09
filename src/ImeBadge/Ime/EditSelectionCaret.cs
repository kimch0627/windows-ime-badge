using System;
using System.Drawing;
using System.Text;

namespace ImeBadge;

/// <summary>
/// 표준 입력칸(Edit·RichEdit 계열: 카카오톡 대화 입력칸, 워드패드, 메모장, Delphi 로 만든 메신저 등)에서 글을 선택한 동안
/// Win32 caret 을 바로잡는다(<see cref="SelectionCaret"/>). 선택이 없을 때 본 caret 높이를 그 입력칸의 줄 높이로 기억해 둔다.
/// UI 스레드에서만 부른다.
/// </summary>
static class EditSelectionCaret
{
    const uint EM_GETSEL = 0x00B0, EM_POSFROMCHAR = 0x00D6, AskTimeoutMs = 50;

    // 마지막으로 선택 없이 본 입력칸과 그 caret 높이
    static IntPtr _hwnd;
    static int _lineHeight;

    /// <param name="hwnd">caret 을 가진 창(<c>GUITHREADINFO.hwndCaret</c>).</param>
    /// <param name="caret">그 창의 caret 사각형(화면 좌표).</param>
    public static Rectangle Adjust(IntPtr hwnd, Rectangle caret, StringBuilder? dump)
    {
        if (!Understands(Native.ClassName(hwnd))) return caret;
        // 선택 시작·끝은 두 포인터로 받는다(반환값은 다른 프로세스에 SendMessageTimeout 으로 보내면 엉뚱하다, Focus.HasWin32Selection 참고).
        if (Native.SendMessageTimeout(hwnd, EM_GETSEL, out int start, out int end, Native.SMTO_ABORTIFHUNG, AskTimeoutMs, out _) == IntPtr.Zero)
            return caret;
        if (start == end)
        {
            _hwnd = hwnd;
            _lineHeight = caret.Height;
            return caret;
        }
        // Edit 는 글 끝(마지막 글자 다음) 위치를 답하지 않는다. 그때는 마지막으로 고른 글자의 줄을 쓴다.
        var endAt = Locate(hwnd, end) ?? (end > start ? Locate(hwnd, end - 1) : null);
        var fixedCaret = SelectionCaret.Fix(caret, Locate(hwnd, start), endAt, hwnd == _hwnd ? _lineHeight : 0);
        dump?.Append($" caret:sel({caret.Left},{caret.Top},{caret.Width}x{caret.Height} -> {fixedCaret.Left},{fixedCaret.Top},h={fixedCaret.Height})");
        return fixedCaret;
    }

    /// <summary>
    /// 선택 시작·끝과 글자 위치를 포인터 없이 물어도 되는 입력칸: Windows 표준 Edit·RichEdit 와 그것을 감싼 WinForms·Delphi 컨트롤.
    /// RichEdit 2.0 꼴로 EM_POSFROMCHAR 의 wParam 을 주소로 읽는 컨트롤에 글자 번호를 보내면 그 앱이 엉뚱한 곳에 쓰므로, 이름에 edit 가 든
    /// 아무 컨트롤에나 묻지 않는다.
    /// Delphi(VCL) 의 TRichEdit·TMemo 를 이어받은 컨트롤은 부품 회사마다 이름이 달라(TcxRichEdit, TRzRichEdit, TsMemo 등) 이름 안의 낱말로 본다.
    /// 모두 Windows 의 RichEdit(riched20, 3.0 이상)·Edit 을 감싼 것이다. 티톡 대화 창(TfrmSendMessage)이 Delphi 로 만든 앱이다.
    /// </summary>
    static bool Understands(string cls) =>
        cls.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
        || cls.Equals("Edit", StringComparison.OrdinalIgnoreCase)
        || cls.Contains(".EDIT.", StringComparison.OrdinalIgnoreCase)   // WinForms TextBox·RichTextBox
        || cls == "TEdit"                                                 // Delphi(VCL)
        || cls.Length > 1 && cls[0] == 'T' && (cls.Contains("RichEdit", StringComparison.Ordinal) || cls.EndsWith("Memo", StringComparison.Ordinal));

    /// <summary>
    /// 글자 위치(화면 좌표). Edit 와 RichEdit 3.0 이상은 wParam = 글자 번호, 반환값 = 클라이언트 좌표 x·y 16비트씩인 꼴을 받아,
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
