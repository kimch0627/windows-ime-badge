using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ImeBadge;

enum ImeState { Unknown, Hangul, English, OtherLang }

/// <summary>한 번 읽은 결과. 배지를 띄우지 않을 이유가 있으면 <see cref="Suppressed"/> 에 적힌다.</summary>
/// <param name="CapsLock">한/영 모드이고 Caps Lock 이 켜져 있는가(설정에서 표시를 껐으면 항상 false).</param>
/// <param name="Shift">한/영 모드이고 Shift 를 계속 누르고 있는가(<see cref="ShiftHold"/>). 설정에서 표시를 껐으면 항상 false.</param>
/// <param name="CaretWindow">커서를 자식 창으로 그리는 앱(Xshell)에서 찾은 그 커서 창(<see cref="CursorWindow"/>). 없으면 0.
/// BadgeForm 이 이 창의 위치 변경 이벤트를 받아 타이머를 기다리지 않고 배지를 옮긴다.</param>
/// <param name="Insert">활성 창이 Insert 로 겹쳐 쓰기를 켠 상태인가(<see cref="InsertToggle"/>). 언어와 상관없다. 설정에서 표시를 껐으면 항상 false.</param>
readonly record struct Snapshot(ImeState State, Rectangle? Caret, IntPtr Foreground = default, string? Suppressed = null, bool CapsLock = false,
                                bool Shift = false, IntPtr CaretWindow = default, bool Insert = false);

/// <summary>활성 창의 caret 위치와 한/영 상태를 한 번 읽어 <see cref="Snapshot"/> 으로 돌려준다.</summary>
static class ImeReader
{
    // pid → 프로세스 이름. 활성 창이 바뀔 때마다 OpenProcess 를 다시 하지 않도록 캐시한다.
    static readonly Dictionary<uint, string> ProcessNames = new();

    /// <summary>
    /// IMM32 가 한/영 상태를 틀리게 보고하는 것으로 알려진 앱. 이 앱들은 TSF 전역 compartment 를 먼저 읽고 IMM 은 대체 경로로 쓴다.
    /// (Windows Terminal 은 TSF 로만 IME 를 쓰고, IMM 쪽 상태는 마지막으로 IMM 을 쓴 창의 값이 남아 있는 경우가 있다.)
    /// </summary>
    static readonly string[] TsfPreferredProcesses = { "WindowsTerminal", "OpenConsole" };

    /// <param name="shiftHeld">Shift 를 계속 누르고 있는가. 누른 시간은 폴링마다 재야 하므로 부르는 쪽(<see cref="ShiftHold"/>)이 잰다.</param>
    /// <param name="overtype">활성 창이 겹쳐 쓰기 상태인가. 창마다 Insert 를 누른 것을 세야 하므로 부르는 쪽(<see cref="InsertToggle"/>)이 잰다.</param>
    public static Snapshot Read(IntPtr selfHandle, Settings settings, bool shiftHeld = false, bool overtype = false)
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == selfHandle) return new(ImeState.Unknown, null);

        // UWP 앱이면 껍데기(ApplicationFrameHost)가 아니라 안쪽 CoreWindow(실제 앱 프로세스)의 스레드를 조사한다.
        var core = Native.UwpCoreWindow(fg);
        var target = core != IntPtr.Zero ? core : fg;
        uint tid = Native.GetWindowThreadProcessId(target, out uint pid);

        // 우리 자신의 창(설정·정보 창)이 활성이면 조사하지 않는다. UI Automation 은 작업 스레드에서 묻지만(A11yCaret) 우리 창의
        // 공급자는 UI 스레드에 있고, UI 스레드는 그 답을 (메시지를 펌프하지 않고) 기다리고 있어 매번 시간을 넘긴다.
        // 설정 창에는 배지가 필요 없으니 숨긴다.
        if (pid == (uint)Environment.ProcessId) return new(ImeState.Unknown, null, fg, "self");

        string process = ProcessName(pid);
        if (settings.ExcludedProcesses.Count > 0 && ProcessFilter.IsExcluded(settings.ExcludedProcesses, process))
            return new(ImeState.Unknown, null, fg, "excluded");
        if (settings.HideOnFullscreen && Native.IsFullscreen(fg))
            return new(ImeState.Unknown, null, fg, "fullscreen");

        var gti = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(tid, ref gti)) return new(ImeState.Unknown, null);

        var dump = Log.Enabled ? new StringBuilder() : null;
        if (core != IntPtr.Zero) dump?.Append(" uwp");
        long t0 = Environment.TickCount64;

        Rectangle? caret = null;
        IntPtr caretWindow = IntPtr.Zero;
        if (gti.hwndCaret != IntPtr.Zero && gti.rcCaret.Bottom > gti.rcCaret.Top)
        {
            var tl = new Native.POINT(gti.rcCaret.Left, gti.rcCaret.Top);
            var br = new Native.POINT(gti.rcCaret.Right, gti.rcCaret.Bottom);
            Native.ClientToScreen(gti.hwndCaret, ref tl);
            Native.ClientToScreen(gti.hwndCaret, ref br);
            caret = Rectangle.FromLTRB(tl.X, tl.Y, Math.Max(br.X, tl.X + 1), br.Y);
            dump?.Append($" caret:win32('{Native.ClassName(gti.hwndCaret)}')");
        }
        else
        {
            if (gti.hwndCaret != IntPtr.Zero) dump?.Append(" caret:win32-empty");   // caret 창은 있지만 높이 0
            // 커서를 자식 창으로 그리는 터미널(Xshell 8): 포커스 창의 "CURSOR" 자식 창이 곧 커서다.
            // 창 관리자에게만 묻는 싼 호출이라 대상 앱과 주고받는 UIA 보다 먼저 본다.
            if (CursorWindow.Find(gti.hwndFocus, dump) is { } cw)
            {
                caret = cw.Rect;
                caretWindow = cw.Hwnd;
            }
            else
            {
                // UI Automation(→ 필요하면 MSAA). 대상 앱이 바쁘면 오래 걸릴 수 있어 UI 스레드 밖에서 묻고 잠깐만 기다린다.
                caret = A11yCaret.Find(gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : target, process, dump);
            }
        }
        // 셋 다 못 찾으면(caret == null) 배지를 띄우지 않는다. 작업 표시줄·버튼처럼 글자를 입력하지 않는 곳에 뜨지 않게 하기 위해서다.
        // 한/영 상태는 트레이 아이콘이 계속 보여 준다.

        bool preferTsf = core != IntPtr.Zero || Array.IndexOf(TsfPreferredProcesses, process) >= 0;
        var state = ReadImeState(target, gti.hwndFocus, tid, preferTsf, dump);
        // Caps Lock 은 배지 글자로 보여 준다(한 → 꺆, a → A). 한글 모드에서도 켜져 있으면 영문 대문자가 입력되므로 함께 본다.
        bool caps = settings.ShowCapsLock && state is (ImeState.English or ImeState.Hangul) && Native.IsCapsLockOn();
        if (caps) dump?.Append(" caps");
        // Shift 를 누르고 있는 동안은 지금 입력될 대소문자와 왼쪽 아래 ▲ 를 보여 준다(Caps Lock 표시의 하위 옵션).
        bool shift = shiftHeld && settings.ShowCapsLock && settings.ShowShiftHold && state is (ImeState.English or ImeState.Hangul);
        if (shift) dump?.Append(" shift");
        // 겹쳐 쓰기는 입력 언어와 상관없이 글자를 덮어쓰므로 다른 언어(?) 배지에도 보여 준다.
        bool insert = overtype && settings.ShowInsert && state != ImeState.Unknown;
        if (insert) dump?.Append(" overtype");

        if (dump is not null)
        {
            // 한 번 읽는 데 오래 걸리면(UIA 응답 지연 등) 별도로 남긴다. 배지가 멈춘 듯 보이는 원인 추적용.
            long ms = Environment.TickCount64 - t0;
            if (ms > 200) Log.Write($"SLOW read {ms}ms fg='{Native.ClassName(fg)}'");
            Log.WriteIfChanged($"fg='{Native.ClassName(fg)}' focus='{Native.ClassName(gti.hwndFocus)}' tid={tid} pid={pid} => {state}{dump}");
        }

        return new(state, caret, fg, CapsLock: caps, Shift: shift, CaretWindow: caretWindow, Insert: insert);
    }

    /// <summary>
    /// 한국어 IME의 한/영 판정. 두 경로가 있다.
    /// <list type="number">
    /// <item>IMM32: 포커스 창의 기본 IME 창에 WM_IME_CONTROL 로 묻는다. 대부분의 Win32 앱.</item>
    /// <item>TSF 전역 compartment(<see cref="Tsf.ReadState"/>): IMM 이 답하지 못하거나(IME 창 없음·응답 없음)
    /// 틀리게 답하는 앱(<paramref name="preferTsf"/>: UWP, Windows Terminal)용.</item>
    /// </list>
    /// 보통은 IMM → TSF 순서, preferTsf 면 TSF → IMM 순서로 시도한다.
    /// </summary>
    static ImeState ReadImeState(IntPtr fg, IntPtr hwndFocus, uint tid, bool preferTsf, StringBuilder? dump)
    {
        ushort lang = (ushort)((long)Native.GetKeyboardLayout(tid) & 0xFFFF);
        if (lang != Native.LANG_KOREAN) { dump?.Append($" lang=0x{lang:X4}"); return ImeState.OtherLang; }

        var state = ImeState.Unknown;
        if (preferTsf) state = Tsf.ReadState(dump) ?? ImeState.Unknown;
        if (state == ImeState.Unknown) state = ReadImm(fg, hwndFocus, dump);
        if (state == ImeState.Unknown && !preferTsf) state = Tsf.ReadState(dump) ?? ImeState.Unknown;
        return state;
    }

    /// <summary>IMM32 경로. 한/영 키는 "열림(open)"이 아니라 "변환 모드"의 한글 비트를 바꾼다.</summary>
    static ImeState ReadImm(IntPtr fg, IntPtr hwndFocus, StringBuilder? dump)
    {
        var target = hwndFocus != IntPtr.Zero ? hwndFocus : fg;
        var imeWnd = Native.ImmGetDefaultIMEWnd(target);
        if (imeWnd == IntPtr.Zero) { dump?.Append(" imeWnd=0"); return ImeState.Unknown; }

        var ok = Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, Native.IMC_GETOPENSTATUS,
                                           IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out var open);
        if (ok == IntPtr.Zero) { dump?.Append(" open:timeout"); return ImeState.Unknown; }
        if (open == IntPtr.Zero) { dump?.Append(" open=0"); return ImeState.English; }

        ok = Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, Native.IMC_GETCONVERSIONMODE,
                                       IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out var mode);
        if (ok == IntPtr.Zero) { dump?.Append(" mode:timeout"); return ImeState.Unknown; }

        dump?.Append($" open=1 mode=0x{(long)mode:X}");
        return (((uint)(long)mode) & Native.IME_CMODE_HANGUL) != 0 ? ImeState.Hangul : ImeState.English;
    }

    /// <summary>활성 창 프로세스의 실행 파일 이름(확장자 없이). 못 얻으면 "".</summary>
    public static string ProcessName(uint pid)
    {
        if (pid == 0) return "";
        if (ProcessNames.TryGetValue(pid, out var cached)) return cached;
        string name = "";
        try
        {
            var path = Native.ProcessImagePath(pid);
            if (path is not null) name = ProcessFilter.Normalize(path);
        }
        catch { }
        if (ProcessNames.Count > 256) ProcessNames.Clear();   // pid 재사용으로 무한히 커지지 않게
        ProcessNames[pid] = name;
        return name;
    }
}
