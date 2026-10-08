using System;
using System.Drawing;
using System.Text;

namespace ImeBadge;

/// <summary>실험 기능이 포커스된 입력칸에서 알아낼 것. 설정에서 켠 것만 묻는다(모두 꺼져 있으면 대상 앱에 한 번도 더 묻지 않는다).</summary>
[Flags]
enum FocusQuery { None = 0, Password = 1, Selection = 2 }

/// <summary>포커스된 입력칸에 대해 알아낸 것.</summary>
/// <param name="Password">비밀번호 칸인가.</param>
/// <param name="Selection">선택한 글이 있어 다음 글자가 그 글을 지우는가.</param>
readonly record struct FocusFacts(bool Password = false, bool Selection = false);

/// <summary>UI Automation 경로(<see cref="A11yCaret"/>)의 결과: caret 과 입력칸 정보.</summary>
/// <param name="Unverified">caret 을 앞뒤 글자와 대조하지 못했다(UI Automation 이 준 글자 사각형이 망가짐, <see cref="TextCaretPick.Unverified"/>).
/// <see cref="A11yCaret"/> 이 MSAA 가상 caret 을 찾아 바꾼다.</param>
readonly record struct A11yHit(Rectangle? Caret, FocusFacts Facts = default, bool Unverified = false);

/// <summary>caret 을 찾은 경로마다 입력칸 정보를 만든다. 입력한 글자는 읽지 않는다.</summary>
static class Focus
{
    public static FocusQuery QueryFor(Settings s) =>
        (s.ShowSelection ? FocusQuery.Selection : FocusQuery.None)
        | (s.PasswordWarning || s.KeepImeMode ? FocusQuery.Password : FocusQuery.None);   // 한/영 유지는 비밀번호 칸을 한글로 바꾸지 않으려고 묻는다

    /// <summary>
    /// Win32 caret 을 만드는 앱(메모장·워드 등). 비밀번호 칸·선택 영역은 표준 입력칸(Edit·RichEdit 계열)과 Scintilla(Notepad++)에만,
    /// 포인터가 없는 메시지로 묻는다(다른 프로세스에도 그대로 전달된다).
    /// </summary>
    public static FocusFacts FromWin32(IntPtr focus, FocusQuery query)
    {
        if (query == FocusQuery.None || focus == IntPtr.Zero) return default;
        string cls = Native.ClassName(focus);
        bool password = (query & FocusQuery.Password) != 0 && IsWin32Password(focus, cls);
        bool selection = (query & FocusQuery.Selection) != 0 && HasWin32Selection(focus, cls);
        return new FocusFacts(password, selection);
    }

    const uint EM_GETSEL = 0x00B0, EM_GETPASSWORDCHAR = 0x00D2, SCI_GETSELECTIONSTART = 2143, SCI_GETSELECTIONEND = 2145;
    const long ES_PASSWORD = 0x0020;
    const uint AskTimeoutMs = 50;

    static bool IsEdit(string cls) => cls.Contains("edit", StringComparison.OrdinalIgnoreCase);

    /// <summary>Windows 표준 입력칸 계열(Edit, RichEdit, WinForms TextBox). ES_PASSWORD 같은 스타일 비트의 뜻이 정해져 있는 클래스만.</summary>
    static bool IsStandardEdit(string cls) =>
        cls.Equals("Edit", StringComparison.OrdinalIgnoreCase) || cls.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
        || cls.Contains(".EDIT.", StringComparison.OrdinalIgnoreCase);

    /// <summary>표준 입력칸의 비밀번호 칸: ES_PASSWORD 스타일이거나, 앱이 가림 글자(●)를 정해 두었다(EM_GETPASSWORDCHAR).</summary>
    static bool IsWin32Password(IntPtr focus, string cls)
    {
        if (!IsStandardEdit(cls)) return false;
        if (((long)Native.GetWindowLongPtr(focus, Native.GWL_STYLE) & ES_PASSWORD) != 0) return true;
        return Ask(focus, EM_GETPASSWORDCHAR, out long ch) && ch != 0;
    }

    /// <summary>
    /// 표준 입력칸: EM_GETSEL 로 선택 시작·끝을 두 포인터에 받는다. 반환값(아래 16비트 = 시작, 위 16비트 = 끝)은 쓰지 않는다:
    /// 다른 프로세스에 SendMessageTimeout 으로 보내면 선택과 상관없이 엉뚱한 값이 와서(카카오톡·Xshell 에서 확인), 글이 있는 칸은 늘 선택이 있어 보였다.
    /// Scintilla: 선택 시작·끝 위치. 응답 없는 앱은 기다리지 않는다(SMTO_ABORTIFHUNG, 50ms).
    /// </summary>
    static bool HasWin32Selection(IntPtr focus, string cls)
    {
        if (IsEdit(cls))
            return Native.SendMessageTimeout(focus, EM_GETSEL, out int start, out int end, Native.SMTO_ABORTIFHUNG, AskTimeoutMs, out _) != IntPtr.Zero
                   && start != end;
        if (cls.StartsWith("Scintilla", StringComparison.OrdinalIgnoreCase))
            return Ask(focus, SCI_GETSELECTIONSTART, out long from) && Ask(focus, SCI_GETSELECTIONEND, out long to) && from != to;
        return false;
    }

    static bool Ask(IntPtr hwnd, uint msg, out long result)
    {
        bool ok = Native.SendMessageTimeout(hwnd, msg, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, AskTimeoutMs, out var r) != IntPtr.Zero;
        result = (long)r;
        return ok;
    }

    /// <summary>디버그 로그용 요약.</summary>
    public static void Describe(in FocusFacts f, StringBuilder? dump)
    {
        if (dump is null) return;
        if (f.Password) dump.Append(" password");
        if (f.Selection) dump.Append(" selection");
    }
}
