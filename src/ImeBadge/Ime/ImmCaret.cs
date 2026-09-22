using System;
using System.Drawing;
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
/// 결과를 받을 버퍼가 상대 프로세스의 주소 공간에 있어야 하므로(SendMessage 는 포인터를 옮겨 주지 않는다) 그 프로세스에
/// 작은 버퍼를 빌려(VirtualAllocEx) IME 창이 채우게 하고 읽어 온다(ReadProcessMemory). 상대 메모리에 쓰지는 않는다.
/// 관리자 권한 프로세스처럼 열 수 없는 프로세스는 건너뛴다.
/// </summary>
static class ImmCaret
{
    const int BufferSize = 256;             // COMPOSITIONFORM(28 바이트) 과 LOGFONTW(92 바이트) 모두 넉넉히
    const int FallbackLineHeight = 16;      // 조합 글꼴 높이를 못 얻었을 때(px, 배율 1 기준)

    // 상대 프로세스에 빌린 버퍼. pid 가 같으면 재사용하고(100ms 마다 할당·해제하지 않게) 바뀌면 돌려준다.
    static uint _pid, _deniedPid;
    static IntPtr _process, _remote;
    static readonly byte[] _local = new byte[BufferSize];

    public static Rectangle? Find(IntPtr hwndFocus, uint pid, StringBuilder? dump)
    {
        if (hwndFocus == IntPtr.Zero || pid == 0) return null;
        var imeWnd = Native.ImmGetDefaultIMEWnd(hwndFocus);
        if (imeWnd == IntPtr.Zero) return null;
        if (!EnsureBuffer(pid)) { dump?.Append(" imm:noaccess"); return null; }

        // COMPOSITIONFORM { DWORD dwStyle; POINT ptCurrentPos; RECT rcArea; }
        if (!Query(imeWnd, Native.IMC_GETCOMPOSITIONWINDOW, 28, out long r)) { dump?.Append($" imm:comp-none(r={r})"); return null; }
        uint style = BitConverter.ToUInt32(_local, 0);
        var pt = new Native.POINT(BitConverter.ToInt32(_local, 4), BitConverter.ToInt32(_local, 8));
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
        int height = Query(imeWnd, Native.IMC_GETCOMPOSITIONFONT, 92, out _) ? Math.Abs(BitConverter.ToInt32(_local, 0)) : 0;
        Native.ClientToScreen(hwndFocus, ref pt);
        if (height <= 0 || height > 200) height = (int)Math.Round(FallbackLineHeight * Native.DpiScaleAt(new Point(pt.X, pt.Y)));

        dump?.Append($" caret:imm({pt.X},{pt.Y},h={height})");
        return new Rectangle(pt.X, pt.Y, 1, height);
    }

    /// <summary>
    /// IME 창에 IMC_GET* 을 보내 빌린 버퍼를 채우게 하고 <paramref name="size"/> 바이트를 <see cref="_local"/> 로 읽어 온다.
    /// <paramref name="result"/> 는 IME 창의 응답(성공 0). 시간 초과면 -1. 로그용.
    /// </summary>
    static bool Query(IntPtr imeWnd, int what, int size, out long result)
    {
        result = -1;
        var ok = Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, (IntPtr)what, _remote, Native.SMTO_ABORTIFHUNG, 100, out var res);
        if (ok == IntPtr.Zero) return false;
        result = (long)res;
        if (result != 0) return false;   // IMC_GET* 은 성공하면 0. 입력 컨텍스트가 없는 창이면 실패한다
        return Native.ReadProcessMemory(_process, _remote, _local, (UIntPtr)(uint)size, out var read) && (ulong)read >= (ulong)size;
    }

    static bool EnsureBuffer(uint pid)
    {
        if (pid == _pid && _remote != IntPtr.Zero)
        {
            // 그 프로세스가 끝나고 pid 가 다른 프로세스에 재사용됐을 수 있다. 죽은 프로세스의 주소를 새 프로세스의 IME 창에 넘기면 안 된다.
            if (Native.WaitForSingleObject(_process, 0) == Native.WAIT_TIMEOUT) return true;
            Release();
        }
        if (pid == _deniedPid) return false;
        Release();

        var h = Native.OpenProcess(Native.PROCESS_VM_OPERATION | Native.PROCESS_VM_READ, false, pid);
        if (h == IntPtr.Zero) { _deniedPid = pid; return false; }
        var mem = Native.VirtualAllocEx(h, IntPtr.Zero, (UIntPtr)BufferSize, Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
        if (mem == IntPtr.Zero) { Native.CloseHandle(h); _deniedPid = pid; return false; }
        _pid = pid; _process = h; _remote = mem;
        return true;
    }

    /// <summary>빌린 버퍼와 프로세스 핸들을 돌려준다. 프로세스가 바뀔 때와 프로그램을 끝낼 때 부른다.</summary>
    public static void Release()
    {
        if (_remote != IntPtr.Zero) Native.VirtualFreeEx(_process, _remote, UIntPtr.Zero, Native.MEM_RELEASE);
        if (_process != IntPtr.Zero) Native.CloseHandle(_process);
        _pid = 0; _process = IntPtr.Zero; _remote = IntPtr.Zero;
    }
}
