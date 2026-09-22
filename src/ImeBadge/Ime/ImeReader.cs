using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ImeBadge;

enum ImeState { Unknown, Hangul, English, OtherLang }

/// <summary>한 번 읽은 결과. 배지를 띄우지 않을 이유가 있으면 <see cref="Suppressed"/> 에 적힌다.</summary>
/// <param name="CapsLock">영문 모드이고 Caps Lock 이 켜져 있는가(설정에서 표시를 껐으면 항상 false).</param>
readonly record struct Snapshot(ImeState State, Rectangle? Caret, IntPtr Foreground = default, string? Suppressed = null, bool CapsLock = false);

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

    public static Snapshot Read(IntPtr selfHandle, Settings settings)
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == selfHandle) return new(ImeState.Unknown, null);

        // UWP 앱이면 껍데기(ApplicationFrameHost)가 아니라 안쪽 CoreWindow(실제 앱 프로세스)의 스레드를 조사한다.
        var core = Native.UwpCoreWindow(fg);
        var target = core != IntPtr.Zero ? core : fg;
        uint tid = Native.GetWindowThreadProcessId(target, out uint pid);

        // 우리 자신의 창(설정·정보 창)이 활성이면 조사하지 않는다. 같은 프로세스의 UI 스레드에서 UI Automation 클라이언트를
        // 부르면 공급자(우리 창)가 같은 스레드에 있어 서로를 기다리다 시간 초과가 나고, 그동안 메시지 펌프가 재진입해
        // 배지가 깜빡이고 버튼이 늦게 반응한다. 설정 창에는 배지가 필요 없으니 숨긴다.
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
            caret = UiaCaret.Find(dump);
        }

        bool preferTsf = core != IntPtr.Zero || Array.IndexOf(TsfPreferredProcesses, process) >= 0;
        var state = ReadImeState(target, gti.hwndFocus, tid, preferTsf, dump);
        // Caps Lock 은 한글 입력에 영향이 없으므로 영문 모드에서만 본다.
        bool caps = settings.ShowCapsLock && state == ImeState.English && Native.IsCapsLockOn();
        if (caps) dump?.Append(" caps");

        if (dump is not null)
        {
            // 한 번 읽는 데 오래 걸리면(UIA 응답 지연 등) 별도로 남긴다. 배지가 멈춘 듯 보이는 원인 추적용.
            long ms = Environment.TickCount64 - t0;
            if (ms > 200) Log.Write($"SLOW read {ms}ms fg='{Native.ClassName(fg)}'");
            Log.WriteIfChanged($"fg='{Native.ClassName(fg)}' focus='{Native.ClassName(gti.hwndFocus)}' tid={tid} pid={pid} => {state}{dump}");
        }

        return new(state, caret, fg, CapsLock: caps);
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
