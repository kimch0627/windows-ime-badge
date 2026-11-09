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
/// <param name="Facts">실험 기능이 물은 입력칸 정보(<see cref="FocusFacts"/>). 실험 기능을 모두 껐으면 비어 있다.</param>
/// <param name="Focus">한/영을 읽은 창(포커스 창, 없으면 활성 창. UWP 는 안쪽 CoreWindow). 한/영 유지가 같은 창에 바꾸라고 보낸다.</param>
/// <param name="PreferTsf">IMM 대신 TSF 로 읽는 앱(UWP, Windows Terminal). IMM 으로 바꾸라는 요청도 통하지 않는다.</param>
/// <param name="ModeUncertain">IMM 의 변환 모드가 한국어 IME 의 값이 아니었다(<see cref="KoreanIme.IsPlausibleMode"/>). 배지는 그대로 그리지만
/// 한/영 유지는 이 값으로 기억하거나 바꾸지 않는다.</param>
/// <param name="Process">활성 창 프로세스의 실행 파일 이름(UWP 는 안쪽 앱). 모르면 "".</param>
/// <param name="Miss">"배지가 안 뜬 앱 찾기" 가 켜져 있고 배지를 띄우지 못했을 때 그 까닭(<see cref="MissReason"/>). 입력칸이 아닌 곳
/// (버튼·목록·읽기 전용)이거나 일부러 숨긴 곳(제외 앱·이 프로그램)이면 null.</param>
/// <param name="MissControlType">caret 을 못 찾은 요소의 UI Automation 컨트롤 종류 ID(모르면 0). 보고서용.</param>
/// <param name="MissNote">caret 을 못 찾은 사정을 짧게(보고서용, 없으면 ""). "caret=empty"(Win32 caret 창은 있는데 높이 0),
/// "uia-err=0x80040201"·"uia=nofocus"(UI Automation 이 포커스된 요소를 주지 못함).</param>
readonly record struct Snapshot(ImeState State, Rectangle? Caret, IntPtr Foreground = default, string? Suppressed = null, bool CapsLock = false,
                                bool Shift = false, IntPtr CaretWindow = default, bool Insert = false, FocusFacts Facts = default,
                                IntPtr Focus = default, bool PreferTsf = false, bool ModeUncertain = false, string Process = "",
                                string? Miss = null, int MissControlType = 0, string MissNote = "");

/// <summary>활성 창의 caret 위치와 한/영 상태를 한 번 읽어 <see cref="Snapshot"/> 으로 돌려준다.</summary>
static class ImeReader
{
    // pid → 프로세스 이름. 활성 창이 바뀔 때마다 OpenProcess 를 다시 하지 않도록 캐시한다.
    static readonly Dictionary<uint, string> ProcessNames = new();
    // pid → 관리자 권한으로 실행 중인가. "배지가 안 뜬 앱 찾기" 가 caret 을 못 찾았을 때만 묻는다.
    static readonly Dictionary<uint, bool> Elevation = new();
    static bool? _selfElevated;

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
            return new(ImeState.Unknown, null, fg, "excluded", Process: process);
        bool track = settings.TrackMissedApps;
        if (settings.HideOnFullscreen && Native.IsFullscreen(fg))
            // 게임·동영상에서 스페이스를 누른 것까지 세지 않도록, 전체 화면은 Win32 caret 이 있는(글을 쓰는) 창만 센다.
            // UI Automation 은 묻지 않는다(전체 화면 게임에 접근성 질문을 보내지 않으려고).
            return new(ImeState.Unknown, null, fg, "fullscreen", Process: process,
                       Miss: track && HasWin32Caret(tid) ? MissReason.Fullscreen : null);

        var gti = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(tid, ref gti))
            return new(ImeState.Unknown, null, Process: process, Miss: track ? Blocked(pid, MissReason.NoCaret) : null);

        var dump = Log.Enabled ? new StringBuilder() : null;
        if (core != IntPtr.Zero) dump?.Append(" uwp");
        long t0 = Environment.TickCount64;

        Rectangle? caret = null;
        IntPtr caretWindow = IntPtr.Zero;
        FocusFacts facts = default;
        bool notInput = false, slow = false;
        int controlType = 0;
        string note = "";
        var query = Focus.QueryFor(settings);
        if (gti.hwndCaret != IntPtr.Zero && gti.rcCaret.Bottom > gti.rcCaret.Top)
        {
            var tl = new Native.POINT(gti.rcCaret.Left, gti.rcCaret.Top);
            var br = new Native.POINT(gti.rcCaret.Right, gti.rcCaret.Bottom);
            Native.ClientToScreen(gti.hwndCaret, ref tl);
            Native.ClientToScreen(gti.hwndCaret, ref br);
            dump?.Append($" caret:win32('{Native.ClassName(gti.hwndCaret)}')");
            // 입력칸은 글을 선택한 동안 caret 을 넓게 바꾸거나(RichEdit 8px) 한 줄 위에 있다고 알리기도 한다(티톡). 선택 끝 위치로 바로잡는다.
            caret = EditSelectionCaret.Adjust(gti.hwndCaret, Rectangle.FromLTRB(tl.X, tl.Y, Math.Max(br.X, tl.X + 1), br.Y), dump);
            facts = Focus.FromWin32(gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : gti.hwndCaret, query);
        }
        else
        {
            if (gti.hwndCaret != IntPtr.Zero)
            {
                dump?.Append(" caret:win32-empty");   // caret 창은 있지만 높이 0
                note = "caret=empty";
            }
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
                var hit = A11yCaret.Find(gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : target, process, query, dump);
                caret = hit.Caret;
                facts = hit.Facts;
                notInput = hit.NotInput;
                slow = hit.Slow;
                controlType = hit.ControlType;
                if (hit.Failure is { } failure)
                    note = (note.Length > 0 ? note + " " : "") + (failure == "nofocus" ? "uia=nofocus" : "uia-err=" + failure);
            }
        }
        Focus.Describe(facts, dump);
        // 셋 다 못 찾으면(caret == null) 배지를 띄우지 않는다. 작업 표시줄·버튼처럼 글자를 입력하지 않는 곳에 뜨지 않게 하기 위해서다.
        // 한/영 상태는 트레이 아이콘이 계속 보여 준다.

        // IMM32 가 한/영을 틀리게 보고하는 앱(UWP, Windows Terminal)은 TSF 전역 compartment 를 먼저 읽는다. process 는 소문자라
        // 대소문자를 가리지 않고 견준다(예전에는 "WindowsTerminal" 과 견주어 한 번도 맞지 않았다).
        bool preferTsf = core != IntPtr.Zero || KoreanIme.PrefersTsf(process);
        var state = ReadImeState(target, gti.hwndFocus, tid, preferTsf, dump, out bool uncertain);
        // Caps Lock 은 배지 글자로 보여 준다(한 → 꺆, a → A). 한글 모드에서도 켜져 있으면 영문 대문자가 입력되므로 함께 본다.
        bool caps = settings.ShowCapsLock && state is (ImeState.English or ImeState.Hangul) && Native.IsCapsLockOn();
        if (caps) dump?.Append(" caps");
        // Shift 를 누르고 있는 동안은 지금 입력될 대소문자와 왼쪽 아래 ▲ 를 보여 준다(Caps Lock 표시의 하위 옵션).
        bool shift = shiftHeld && settings.ShowCapsLock && settings.ShowShiftHold && state is (ImeState.English or ImeState.Hangul);
        if (shift) dump?.Append(" shift");
        // 겹쳐 쓰기는 입력 언어와 상관없이 글자를 덮어쓰므로 다른 언어(?) 배지에도 보여 준다.
        bool insert = overtype && settings.ShowInsert && state != ImeState.Unknown;
        if (insert) dump?.Append(" overtype");

        // 배지를 띄우지 못하는 까닭("배지가 안 뜬 앱 찾기"). 입력칸이 아닌 곳(버튼·목록·읽기 전용)은 까닭이 없다(배지가 없는 것이 맞다).
        string? miss = null;
        if (track)
        {
            if (caret is null) miss = notInput ? null : slow ? MissReason.Slow : MissReason.NoCaret;
            else if (state == ImeState.Unknown) miss = MissReason.ImeUnknown;
            if (miss is not null) miss = Blocked(pid, miss);
            // 커서도 없고 입력기도 한/영을 답하지 않는 곳(Snipaste 캡처 화면·게임처럼 입력기를 끈 창)은 글을 쓰는 곳으로 보지 않는다.
            // 커서를 찾았더라도 한/영을 모르니 배지를 띄울 수 없는 곳이기도 하다. 관리자 권한 앱(둘 다 막힘)은 바로 위에서 가렸다.
            if (miss == MissReason.NoCaret && state == ImeState.Unknown) { miss = null; dump?.Append(" miss:none(no caret, no IME)"); }
            if (miss is not null) dump?.Append($" miss:{miss}");
        }

        if (dump is not null)
        {
            // 한 번 읽는 데 오래 걸리면(UIA 응답 지연 등) 별도로 남긴다. 배지가 멈춘 듯 보이는 원인 추적용.
            long ms = Environment.TickCount64 - t0;
            if (ms > 200) Log.Write($"SLOW read {ms}ms fg='{Native.ClassName(fg)}'");
            Log.WriteIfChanged($"fg='{Native.ClassName(fg)}' focus='{Native.ClassName(gti.hwndFocus)}' tid={tid} pid={pid} => {state}{dump}");
        }

        return new(state, caret, fg, CapsLock: caps, Shift: shift, CaretWindow: caretWindow, Insert: insert, Facts: facts,
                   Focus: gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : target, PreferTsf: preferTsf, ModeUncertain: uncertain,
                   Process: process, Miss: miss, MissControlType: controlType, MissNote: miss is null ? "" : note);
    }

    /// <summary>
    /// 관리자 권한으로 실행된 앱이고 이 프로그램은 보통 권한이면 <see cref="MissReason.Elevated"/>(Windows 의 UIPI 가 접근성·IME 질문을 막는다),
    /// 아니면 <paramref name="reason"/> 그대로.
    /// </summary>
    static string Blocked(uint pid, string reason)
    {
        _selfElevated ??= Native.IsElevated((uint)Environment.ProcessId) == true;
        if (_selfElevated == true) return reason;
        if (!Elevation.TryGetValue(pid, out bool elevated))
        {
            if (Elevation.Count > 256) Elevation.Clear();   // pid 재사용으로 무한히 커지지 않게
            Elevation[pid] = elevated = Native.IsElevated(pid) == true;
        }
        return elevated ? MissReason.Elevated : reason;
    }

    /// <summary>스레드에 Win32 caret 이 있는가(글을 쓰는 창). 창 관리자에게만 묻는 싼 호출이다.</summary>
    static bool HasWin32Caret(uint tid)
    {
        var gti = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        return Native.GetGUIThreadInfo(tid, ref gti) && gti.hwndCaret != IntPtr.Zero && gti.rcCaret.Bottom > gti.rcCaret.Top;
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
    static ImeState ReadImeState(IntPtr fg, IntPtr hwndFocus, uint tid, bool preferTsf, StringBuilder? dump, out bool uncertain)
    {
        uncertain = false;
        ushort lang = (ushort)((long)Native.GetKeyboardLayout(tid) & 0xFFFF);
        if (lang != Native.LANG_KOREAN) { dump?.Append($" lang=0x{lang:X4}"); return ImeState.OtherLang; }

        var state = ImeState.Unknown;
        if (preferTsf) state = Tsf.ReadState(dump) ?? ImeState.Unknown;
        if (state == ImeState.Unknown) state = ReadImm(fg, hwndFocus, dump, out uncertain);
        if (state == ImeState.Unknown && !preferTsf) state = Tsf.ReadState(dump) ?? ImeState.Unknown;
        return state;
    }

    /// <summary>
    /// IMM32 경로. 한/영 키는 "열림(open)"이 아니라 "변환 모드"의 한글 비트를 바꾼다. 변환 모드가 한국어 IME 의 값이 아니면
    /// (<see cref="KoreanIme.IsPlausibleMode"/>) <paramref name="uncertain"/> 을 켠다.
    /// </summary>
    static ImeState ReadImm(IntPtr fg, IntPtr hwndFocus, StringBuilder? dump, out bool uncertain)
    {
        uncertain = false;
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
        if (!KoreanIme.IsPlausibleMode((long)mode)) { uncertain = true; dump?.Append(" mode?"); }
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
