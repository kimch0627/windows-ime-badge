using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ImeBadge;

enum ImeState { Unknown, Hangul, English, OtherLang }

/// <summary>한 번 읽은 결과. 배지를 띄우지 않을 이유가 있으면 <see cref="Suppressed"/> 에 적힌다.</summary>
readonly record struct Snapshot(ImeState State, Rectangle? Caret, IntPtr Foreground = default, string? Suppressed = null);

/// <summary>활성 창의 caret 위치와 한/영 상태를 한 번 읽어 <see cref="Snapshot"/> 으로 돌려준다.</summary>
static class ImeReader
{
    // pid → 프로세스 이름. 활성 창이 바뀔 때마다 OpenProcess 를 다시 하지 않도록 캐시한다.
    static readonly Dictionary<uint, string> ProcessNames = new();

    public static Snapshot Read(IntPtr selfHandle, Settings settings)
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == selfHandle) return new(ImeState.Unknown, null);

        uint tid = Native.GetWindowThreadProcessId(fg, out uint pid);

        // 우리 자신의 창(설정·정보 창)이 활성이면 조사하지 않는다. 같은 프로세스의 UI 스레드에서 UI Automation 클라이언트를
        // 부르면 공급자(우리 창)가 같은 스레드에 있어 서로를 기다리다 시간 초과가 나고, 그동안 메시지 펌프가 재진입해
        // 배지가 깜빡이고 버튼이 늦게 반응한다. 설정 창에는 배지가 필요 없으니 숨긴다.
        if (pid == (uint)Environment.ProcessId) return new(ImeState.Unknown, null, fg, "self");

        if (settings.ExcludedProcesses.Count > 0 && ProcessFilter.IsExcluded(settings.ExcludedProcesses, ProcessName(pid)))
            return new(ImeState.Unknown, null, fg, "excluded");
        if (settings.HideOnFullscreen && Native.IsFullscreen(fg))
            return new(ImeState.Unknown, null, fg, "fullscreen");

        var gti = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(tid, ref gti)) return new(ImeState.Unknown, null);

        var dump = Log.Enabled ? new StringBuilder() : null;
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

        var state = ReadImeState(fg, gti.hwndFocus, tid, dump);

        if (dump is not null)
        {
            // 한 번 읽는 데 오래 걸리면(UIA 응답 지연 등) 별도로 남긴다. 배지가 멈춘 듯 보이는 원인 추적용.
            long ms = Environment.TickCount64 - t0;
            if (ms > 200) Log.Write($"SLOW read {ms}ms fg='{Native.ClassName(fg)}'");
            Log.WriteIfChanged($"fg='{Native.ClassName(fg)}' focus='{Native.ClassName(gti.hwndFocus)}' tid={tid} pid={pid} => {state}{dump}");
        }

        return new(state, caret, fg);
    }

    /// <summary>
    /// 한국어 IME의 한/영 판정. 한/영 키는 "열림(open)"이 아니라 "변환 모드"의 한글 비트를 바꾼다.
    /// </summary>
    static ImeState ReadImeState(IntPtr fg, IntPtr hwndFocus, uint tid, StringBuilder? dump)
    {
        ushort lang = (ushort)((long)Native.GetKeyboardLayout(tid) & 0xFFFF);
        if (lang != Native.LANG_KOREAN) { dump?.Append($" lang=0x{lang:X4}"); return ImeState.OtherLang; }

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
