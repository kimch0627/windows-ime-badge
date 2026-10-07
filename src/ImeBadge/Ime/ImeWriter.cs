using System;

namespace ImeBadge;

/// <summary>
/// 다른 프로그램의 한/영을 바꾼다(실험 기능 "모든 프로그램에서 한/영 유지"). 읽기(<see cref="ImeReader"/>)의 짝이다.
/// <list type="number">
/// <item>IME 에 요청: 포커스 창의 기본 IME 창에 WM_IME_CONTROL(IMC_SETOPENSTATUS·IMC_SETCONVERSIONMODE)을 보낸다. 읽을 때 쓰는
///   IMC_GETCONVERSIONMODE 의 짝으로, 받은 쪽 스레드에서 처리되므로 다른 프로세스에도 통한다. 키 입력을 흉내 내지 않아 가장 조용하다.</item>
/// <item>한/영 키: 1이 통하지 않는 앱(TSF 로만 IME 를 쓰는 Windows Terminal·UWP 등)에는 한/영 키를 한 번 누른 것처럼 보낸다.
///   누를 때마다 뒤집으므로 창 하나에 한 번만 쓰고, 보조키(Alt+Tab 의 Alt 등)를 누르고 있으면 미룬다.</item>
/// </list>
/// </summary>
static class ImeWriter
{
    const uint TimeoutMs = 100;

    /// <summary>IME 에 바꿔 달라고 요청한다. 요청이 전달됐으면 true(실제로 바뀌었는지는 다음 읽기로 확인한다).</summary>
    /// <param name="focus">포커스 창(없으면 활성 창). UWP 는 안쪽 CoreWindow 쪽.</param>
    public static bool RequestImm(IntPtr focus, bool hangul)
    {
        if (focus == IntPtr.Zero) return false;
        var imeWnd = Native.ImmGetDefaultIMEWnd(focus);
        if (imeWnd == IntPtr.Zero) return false;
        // 영문은 IME 를 연 채로 한글 비트만 끈다(한/영 키와 같은 결과). 한글은 IME 가 닫혀 있으면(열림 0) 먼저 연다.
        if (hangul && !Control(imeWnd, Native.IMC_SETOPENSTATUS, 1, out _)) return false;
        if (!Control(imeWnd, Native.IMC_GETCONVERSIONMODE, 0, out long mode)) return false;
        long next = hangul ? mode | Native.IME_CMODE_HANGUL : mode & ~(long)Native.IME_CMODE_HANGUL;
        return Control(imeWnd, Native.IMC_SETCONVERSIONMODE, next, out _);
    }

    /// <summary>한/영 키를 한 번 누른 것처럼 보낸다. 보조키를 누르고 있으면 보내지 않고 false(다음 갱신에 다시).</summary>
    public static bool TapHangulKey() => !Native.IsModifierDown() && Native.TapKey(Native.VK_HANGUL);

    static bool Control(IntPtr imeWnd, int command, long value, out long result)
    {
        bool ok = Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, (IntPtr)command, (IntPtr)value,
                                            Native.SMTO_ABORTIFHUNG, TimeoutMs, out var r) != IntPtr.Zero;
        result = (long)r;
        return ok;
    }
}
