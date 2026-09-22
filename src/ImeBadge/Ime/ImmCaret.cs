using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ImeBadge;

/// <summary>
/// caret 을 찾는 세 번째 경로: IMM32 조합(composition) 창 위치. Win32 caret 도 UI Automation 텍스트 정보도 없는 앱
/// (Xshell 처럼 자체 커서를 그리는 터미널)용이다.
///
/// 한글을 조합하는 글자가 커서 자리에 나타나도록, 앱은 ImmSetCompositionWindow 로 IME 에 커서 위치를 알려 준다.
/// 그 값을 앱의 기본 IME 창에 WM_IME_CONTROL / IMC_GETCOMPOSITIONWINDOW 로 되물어 읽는다. 비유하면 집 주소를 직접 못 찾을 때
/// 집배원(IME)에게 "이 집이 마지막으로 알려 준 우편함 위치" 를 묻는 것이다. 앱이 커서가 움직일 때마다 갱신하면 배지도 커서를
/// 따라가고, 조합을 시작할 때만 갱신하면 마지막으로 한글을 입력한 자리에 머무른다(앱마다 다르다).
///
/// 결과 구조체(COMPOSITIONFORM, LOGFONTW)는 Windows 커널이 프로세스 사이로 옮겨 준다(WM_IME_CONTROL 의 이 하위 명령들은
/// user32 가 marshalling 하는 메시지다). 그래서 우리 쪽 버퍼를 넘기면 되고, 상대 프로세스 메모리에 손댈 필요가 없다.
/// (상대 프로세스에 빌린 주소를 넘기면 커널이 보내는 쪽 주소로 검사하다 실패해 SendMessageTimeout 이 0 을 돌려준다.)
/// </summary>
static class ImmCaret
{
    const int BufferSize = 128;             // COMPOSITIONFORM(28 바이트) 과 LOGFONTW(92 바이트) 모두 넉넉히
    const int FallbackLineHeight = 16;      // 조합 글꼴 높이를 못 얻었을 때(px, 배율 1 기준)

    static IntPtr _buffer;                  // 한 번 만들어 계속 쓰는 비관리 버퍼(100ms 마다 할당하지 않게)

    public static Rectangle? Find(IntPtr hwndFocus, StringBuilder? dump)
    {
        if (hwndFocus == IntPtr.Zero) return null;
        var imeWnd = Native.ImmGetDefaultIMEWnd(hwndFocus);
        if (imeWnd == IntPtr.Zero) return null;
        if (_buffer == IntPtr.Zero) _buffer = Marshal.AllocHGlobal(BufferSize);

        // COMPOSITIONFORM { DWORD dwStyle; POINT ptCurrentPos; RECT rcArea; }
        if (!Query(imeWnd, Native.IMC_GETCOMPOSITIONWINDOW, out long r)) { dump?.Append($" imm:comp-none(r={r})"); return null; }
        uint style = (uint)Marshal.ReadInt32(_buffer, 0);
        var pt = new Native.POINT(Marshal.ReadInt32(_buffer, 4), Marshal.ReadInt32(_buffer, 8));
        if ((style & (Native.CFS_POINT | Native.CFS_RECT | Native.CFS_FORCE_POSITION)) == 0)
        {
            dump?.Append($" imm:comp(style={style})");   // CFS_DEFAULT: 앱이 위치를 정해 준 적이 없다
            return null;
        }
        // 클라이언트 좌표가 창 안에 있어야 믿을 수 있다(초기화만 하고 쓰지 않는 앱의 쓰레기 값 방어).
        if (!Native.GetClientRect(hwndFocus, out var client) ||
            pt.X < client.Left || pt.X > client.Right || pt.Y < client.Top || pt.Y > client.Bottom)
        {
            dump?.Append($" imm:comp-outside({pt.X},{pt.Y})");
            return null;
        }

        // LOGFONTW 의 첫 필드 lfHeight. 음수(글자 높이)로 오는 것이 보통이라 절댓값을 쓴다.
        int height = Query(imeWnd, Native.IMC_GETCOMPOSITIONFONT, out _) ? Math.Abs(Marshal.ReadInt32(_buffer, 0)) : 0;
        Native.ClientToScreen(hwndFocus, ref pt);
        if (height <= 0 || height > 200) height = (int)Math.Round(FallbackLineHeight * Native.DpiScaleAt(new Point(pt.X, pt.Y)));

        dump?.Append($" caret:imm({pt.X},{pt.Y},h={height})");
        return new Rectangle(pt.X, pt.Y, 1, height);
    }

    /// <summary>
    /// IME 창에 IMC_GET* 을 보내 <see cref="_buffer"/> 를 채우게 한다. <paramref name="result"/> 는 IME 창의 응답(성공 0).
    /// 시간 초과·실패면 -1. 로그용.
    /// </summary>
    static bool Query(IntPtr imeWnd, int what, out long result)
    {
        result = -1;
        var ok = Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, (IntPtr)what, _buffer, Native.SMTO_ABORTIFHUNG, 100, out var res);
        if (ok == IntPtr.Zero) return false;
        result = (long)res;
        return result == 0;   // IMC_GET* 은 성공하면 0. 입력 컨텍스트가 없는 창이면 실패한다
    }

    /// <summary>버퍼를 놓아준다. 프로그램을 끝낼 때 부른다.</summary>
    public static void Release()
    {
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
    }
}
