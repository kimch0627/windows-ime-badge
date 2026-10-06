using System;
using System.Drawing;
using System.Text;

namespace ImeBadge;

/// <summary>실험 기능이 포커스된 입력칸에서 알아낼 것. 설정에서 켠 것만 묻는다(모두 꺼져 있으면 대상 앱에 한 번도 더 묻지 않는다).</summary>
[Flags]
enum FocusQuery { None = 0, Field = 1, Password = 2, Selection = 4 }

/// <summary>포커스된 입력칸에 대해 알아낸 것.</summary>
/// <param name="Field">입력칸을 알아보는 키(<see cref="FieldMemory.KeyOf"/> 로 해시한 값). 같은 입력칸이면 다음에도 같다. 모르면 null.</param>
/// <param name="Password">비밀번호 칸인가.</param>
/// <param name="Selection">선택한 글이 있어 다음 글자가 그 글을 지우는가.</param>
readonly record struct FocusFacts(string? Field = null, bool Password = false, bool Selection = false);

/// <summary>UI Automation 경로(<see cref="A11yCaret"/>)의 결과: caret 과 입력칸 정보.</summary>
readonly record struct A11yHit(Rectangle? Caret, FocusFacts Facts = default);

/// <summary>
/// caret 을 찾은 경로마다 입력칸 정보를 만든다. 입력칸 키는 앱 이름과 입력칸을 알아보는 값을 이어 해시한다:
/// Win32 입력칸은 최상위 창·입력칸 창의 클래스와 컨트롤 ID, UI Automation 입력칸은 컨트롤 종류·자동화 ID·이름(<see cref="UiaCaret"/>).
/// 어느 쪽도 입력한 글자는 쓰지 않는다.
/// </summary>
static class Focus
{
    public static FocusQuery QueryFor(Settings s) =>
        (s.RememberFieldMode ? FocusQuery.Field : FocusQuery.None) | (s.ShowSelection ? FocusQuery.Selection : FocusQuery.None)
        | (s.PasswordWarning ? FocusQuery.Password : FocusQuery.None);

    /// <summary>
    /// Win32 caret 을 만드는 앱(메모장·워드 등). 입력칸 키는 창 관리자에게만 묻는다. 비밀번호 칸·선택 영역은 표준 입력칸(Edit·RichEdit 계열)과
    /// Scintilla(Notepad++)에만, 포인터가 없는 메시지로 묻는다(다른 프로세스에도 그대로 전달된다). 비밀번호 칸은 입력칸 키를 만들지 않는다.
    /// </summary>
    public static FocusFacts FromWin32(IntPtr focus, string process, FocusQuery query)
    {
        if (query == FocusQuery.None || focus == IntPtr.Zero) return default;
        string cls = Native.ClassName(focus);
        bool password = (query & (FocusQuery.Field | FocusQuery.Password)) != 0 && IsWin32Password(focus, cls);
        string? field = null;
        if ((query & FocusQuery.Field) != 0 && !password)
        {
            var root = Native.GetAncestor(focus, Native.GA_ROOT);
            // 최상위 창의 GetDlgCtrlID 는 컨트롤 ID 가 아니다(메뉴 핸들일 수 있어 실행할 때마다 다르다). 자식 창일 때만 쓴다.
            bool child = ((long)Native.GetWindowLongPtr(focus, Native.GWL_STYLE) & Native.WS_CHILD) != 0;
            int id = child ? Native.GetDlgCtrlID(focus) : 0;
            field = FieldMemory.KeyOf($"w32|{process}|{Native.ClassName(root)}|{cls}|{id}");
        }
        bool selection = (query & FocusQuery.Selection) != 0 && HasWin32Selection(focus, cls);
        return new FocusFacts(field, password && (query & FocusQuery.Password) != 0, selection);
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
    /// 표준 입력칸: EM_GETSEL 의 반환값(아래 16비트 = 시작, 위 16비트 = 끝). 둘 중 하나가 65535 를 넘으면 -1 이라 알 수 없음(선택 없음으로 본다).
    /// Scintilla: 선택 시작·끝 위치. 응답 없는 앱은 기다리지 않는다(SMTO_ABORTIFHUNG, 50ms).
    /// </summary>
    static bool HasWin32Selection(IntPtr focus, string cls)
    {
        if (IsEdit(cls))
        {
            if (!Ask(focus, EM_GETSEL, out long v)) return false;
            uint packed = unchecked((uint)v);
            return packed != 0xFFFFFFFF && (packed & 0xFFFF) != (packed >> 16);
        }
        if (cls.StartsWith("Scintilla", StringComparison.OrdinalIgnoreCase))
            return Ask(focus, SCI_GETSELECTIONSTART, out long start) && Ask(focus, SCI_GETSELECTIONEND, out long end) && start != end;
        return false;
    }

    static bool Ask(IntPtr hwnd, uint msg, out long result)
    {
        bool ok = Native.SendMessageTimeout(hwnd, msg, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, AskTimeoutMs, out var r) != IntPtr.Zero;
        result = (long)r;
        return ok;
    }

    /// <summary>커서를 자식 창으로 그리는 터미널(Xshell). 입력칸은 터미널 화면 하나뿐이다.</summary>
    public static FocusFacts FromCursorWindow(IntPtr focus, string process, FocusQuery query) =>
        (query & FocusQuery.Field) != 0 ? new FocusFacts(FieldMemory.KeyOf($"cw|{process}|{Native.ClassName(focus)}")) : default;

    /// <summary>UI Automation 이 준 입력칸 값(해시 전)을 앱 이름과 이어 해시한다.</summary>
    public static FocusFacts FromUia(in FocusFacts raw, string process) =>
        raw with { Field = raw.Field is { } f ? FieldMemory.KeyOf($"uia|{process}|{f}") : null };

    /// <summary>디버그 로그용 요약(해시 앞 6자).</summary>
    public static void Describe(in FocusFacts f, StringBuilder? dump)
    {
        if (dump is null) return;
        if (f.Field is { } k) dump.Append(" field=").Append(k, 0, 6);
        if (f.Password) dump.Append(" password");
        if (f.Selection) dump.Append(" selection");
    }
}
