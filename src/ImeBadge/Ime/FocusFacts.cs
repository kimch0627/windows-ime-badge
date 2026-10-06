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
    public static FocusQuery QueryFor(Settings s) => s.RememberFieldMode ? FocusQuery.Field : FocusQuery.None;

    /// <summary>Win32 caret 을 만드는 앱(메모장·워드 등). 창 관리자에게만 묻는다.</summary>
    public static FocusFacts FromWin32(IntPtr focus, string process, FocusQuery query)
    {
        if (query == FocusQuery.None || focus == IntPtr.Zero) return default;
        string? field = null;
        if ((query & FocusQuery.Field) != 0)
        {
            var root = Native.GetAncestor(focus, Native.GA_ROOT);
            // 최상위 창의 GetDlgCtrlID 는 컨트롤 ID 가 아니다(메뉴 핸들일 수 있어 실행할 때마다 다르다). 자식 창일 때만 쓴다.
            bool child = ((long)Native.GetWindowLongPtr(focus, Native.GWL_STYLE) & Native.WS_CHILD) != 0;
            int id = child ? Native.GetDlgCtrlID(focus) : 0;
            field = FieldMemory.KeyOf($"w32|{process}|{Native.ClassName(root)}|{Native.ClassName(focus)}|{id}");
        }
        return new FocusFacts(field);
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
