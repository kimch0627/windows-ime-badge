using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ImeBadge;

/// <summary>Win32 함수 선언(P/Invoke)과 상수. 이 프로그램이 OS 에 직접 묻는 것들은 전부 여기를 거친다.</summary>
static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; public SIZE(int w, int h) { cx = w; cy = h; } }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    public delegate void WinEventProc(IntPtr hHook, uint evt, IntPtr hwnd, int idObject, int idChild,
                                      uint idEventThread, uint dwmsEventTime);

    // ── 창·스레드 ──
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vKey);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr hWnd);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    /// <summary>핸들을 기다린다. WaitHandle.WaitOne 과 달리 STA(UI 스레드)에서도 메시지를 펌프하지 않아, 기다리는 동안 다른 처리가 끼어들지 않는다.</summary>
    [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    public const uint WAIT_OBJECT_0 = 0;
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    /// <summary>낮은 권한(UIPI) 프로세스가 이 창에 <paramref name="msg"/> 를 보낼 수 있게 한다(<see cref="MSGFLT_ALLOW"/>).</summary>
    [DllImport("user32.dll")] public static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr changeFilterStruct);
    public const uint MSGFLT_ALLOW = 1;
    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc proc,
        uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hHook);

    // ── z-order 확인·조정 ──
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr hWnd);
    public const uint GA_ROOT = 2;
    public const int GWL_STYLE = -16;
    public const long WS_CHILD = 0x40000000;
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

    // ── 레이어드 창(픽셀별 투명도) 그리기 ──
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int w, int h);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObj);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey,
        ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
        int x, int y, int cx, int cy, uint flags);

    // ── 접근성: 애니메이션 효과 설정 ──
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);
    const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    /// <summary>Windows 설정 → 접근성 → 시각 효과 → "애니메이션 효과" 가 켜져 있는가. 못 읽으면 켜진 것으로 본다.</summary>
    public static bool AnimationsEnabled()
    {
        bool on = true;
        try { if (!SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0)) return true; }
        catch { return true; }
        return on;
    }

    // ── 창 꾸밈(DWM): 어두운 제목 표시줄, 둥근 모서리 ──
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    /// <summary>창의 비주얼 스타일을 바꾼다. "DarkMode_Explorer" 를 주면 Windows 10 1809 이상에서 스크롤바가 어두운 모양으로 그려진다.</summary>
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;          // Windows 10 20H1+ (그 전 빌드는 19)
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;         // Windows 11
    public const int DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;
    public const int DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;   // Windows 11 22000+, COLORREF(0x00BBGGRR)

    /// <summary>System.Drawing.Color → Win32 COLORREF(0x00BBGGRR).</summary>
    public static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    // ── 모니터 ──
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hmon, ref MONITORINFO info);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);

    // ── 전역 단축키·프로세스 간 메시지 ──
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ── 프로세스 이름 ──
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder name, ref int size);

    public const uint WM_IME_CONTROL = 0x0283;
    public const uint WM_HOTKEY = 0x0312;
    public const int IMC_GETCONVERSIONMODE = 0x0001;
    public const int IMC_SETCONVERSIONMODE = 0x0002;
    public const int IMC_GETOPENSTATUS = 0x0005;
    public const int IMC_SETOPENSTATUS = 0x0006;
    public const uint IME_CMODE_HANGUL = 0x0001;   // == IME_CMODE_NATIVE
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const ushort LANG_KOREAN = 0x0412;
    public const int VK_CAPITAL = 0x14;

    /// <summary>Caps Lock 이 켜져 있는가. 토글 키는 GetKeyState 의 최하위 비트가 켜짐 상태다(스레드에 상관없이 전역 값).</summary>
    public static bool IsCapsLockOn() => (GetKeyState(VK_CAPITAL) & 0x0001) != 0;

    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    /// <summary>
    /// 지금 Shift 가 Ctrl·Alt·Win 없이 눌려 있는가. GetKeyState 는 우리 스레드 입력 큐의 상태라 다른 앱에서 누른 키를 못 보므로
    /// GetAsyncKeyState(실제 키 상태)를 쓴다. Ctrl+Shift·Alt+Shift(언어 전환)·Win+Shift 같은 단축키 조합은 입력이 아니라 제외한다.
    /// </summary>
    public static bool IsShiftAloneDown() =>
        IsKeyDown(VK_SHIFT) && !IsKeyDown(VK_CONTROL) && !IsKeyDown(VK_MENU) && !IsKeyDown(VK_LWIN) && !IsKeyDown(VK_RWIN);

    public const int VK_INSERT = 0x2D;

    /// <summary>
    /// Insert 토글 비트. Insert 를 누를 때마다 Windows 가 뒤집는 값이라 Caps Lock 처럼 GetKeyState 로 다른 앱에서 누른 것도 보인다.
    /// 앱의 실제 겹쳐 쓰기 상태와는 다를 수 있어 그대로 보여 주지 않고 <see cref="InsertToggle"/> 이 바뀐 순간만 센다.
    /// </summary>
    public static bool IsInsertToggled() => (GetKeyState(VK_INSERT) & 0x0001) != 0;

    /// <summary>지금 Ctrl·Shift·Alt·Win 중 하나라도 눌려 있는가(실제 키 상태). Ctrl+Insert·Shift+Insert 같은 단축키를 가려낸다.</summary>
    public static bool IsModifierDown() =>
        IsKeyDown(VK_SHIFT) || IsKeyDown(VK_CONTROL) || IsKeyDown(VK_MENU) || IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);

    public const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;

    /// <summary>
    /// 지금 마우스 버튼이 눌려 있는가(드래그·텍스트 선택 중). GetAsyncKeyState 의 최상위 비트가 "눌림"이며 스레드와 무관한 실제 상태다.
    /// 이때 다른 스레드의 입력 큐에 붙으면(AttachThreadInput) 진행 중인 드래그가 끊길 수 있다.
    /// </summary>
    public static bool IsMouseButtonDown() =>
        (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
        || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;

    /// <summary>지금 Alt·Win·Ctrl 중 하나라도 눌려 있는가(실제 키 상태). 창을 바꾸는 단축키(Alt+Tab, Win+숫자 등)를 쓰는 중인지 가린다.</summary>
    public static bool IsSwitchKeyDown() =>
        IsKeyDown(VK_MENU) || IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN) || IsKeyDown(VK_CONTROL);

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>
    /// 이 세션의 마지막 입력(키보드·마우스) 시각을 Environment.TickCount64 기준 ms 로. 어떤 키인지는 알 수 없고 시각만 있다.
    /// 못 읽으면 <see cref="long.MinValue"/>. (GetLastInputInfo 의 시각은 49.7일마다 돌아오는 32비트 GetTickCount 라 차이로 바꾼다.)
    /// </summary>
    public static long LastInputMs()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return long.MinValue;
        uint idle = unchecked((uint)Environment.TickCount - info.dwTime);
        return Environment.TickCount64 - idle;
    }

    // ── 키 입력 보내기(실험 기능 "모든 프로그램에서 한/영 유지" 의 마지막 방법) ──
    public const int VK_HANGUL = 0x15;   // 한/영 키

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    /// <summary>INPUT 의 공용체. 가장 큰 MOUSEINPUT 을 함께 두어야 Win32 가 기대하는 크기(cbSize)가 된다.</summary>
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002, MAPVK_VK_TO_VSC = 0;

    /// <summary>
    /// 키 하나를 눌렀다 뗀 것처럼 보낸다. 지금 활성 창(의 입력 큐)으로 간다. 관리자 권한으로 실행된 창에는 Windows(UIPI)가 막아 전달되지 않는다.
    /// 둘 다 들어갔으면 true.
    /// </summary>
    public static bool TapKey(int vk)
    {
        ushort scan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = scan };
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = scan, dwFlags = KEYEVENTF_KEYUP };
        return SendInput(2, inputs, Marshal.SizeOf<INPUT>()) == 2;
    }

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_FOCUS = 0x8005;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;      // 창·caret·마우스 포인터 등의 위치 변경. idObject 로 걸러 써야 한다
    public const uint EVENT_OBJECT_TEXTSELECTIONCHANGED = 0x8014; // UIA 텍스트 컨트롤(Chrome/Electron)의 caret·선택 이동
    public const uint EVENT_OBJECT_IME_CHANGE = 0x8029;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const int OBJID_WINDOW = 0;
    public const int OBJID_CARET = -8;
    public const int OBJID_CURSOR = -9;

    public const uint GW_HWNDPREV = 3;           // z-order에서 바로 위(더 앞) 창
    public const uint GW_OWNER = 4;              // 소유자 창(대화상자·도구 창이면 보통 본 창)
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOPMOST = 0x00000008;

    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const uint ULW_ALPHA = 0x02;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public static readonly IntPtr HWND_TOPMOST = new(-1);   // SetWindowPos: 최상위(topmost) 창들 중에서도 맨 위로
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static string ClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(null)";
        var sb = new StringBuilder(128);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// UWP 앱(설정, 스토어 앱 등)의 활성 창은 껍데기 프로세스(ApplicationFrameHost)의 ApplicationFrameWindow 이고,
    /// 실제 앱과 IME 상태는 그 안의 Windows.UI.Core.CoreWindow(앱 프로세스 소유)에 있다. 그 자식 창을 돌려준다. UWP 가 아니면 0.
    /// 비유하면 액자(FrameWindow)가 아니라 그 안의 그림(CoreWindow)에게 물어봐야 한다.
    /// </summary>
    public static IntPtr UwpCoreWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || ClassName(hwnd) != "ApplicationFrameWindow") return IntPtr.Zero;
        return FindWindowEx(hwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
    }

    public static bool IsTopmost(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && ((long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    /// <summary>z-order에서 <paramref name="a"/>가 <paramref name="b"/>보다 위(앞)에 있는가. b에서 위로 올라가며 찾는다.</summary>
    public static bool IsAbove(IntPtr a, IntPtr b)
    {
        for (var h = GetWindow(b, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
            if (h == a) return true;
        return false;
    }

    /// <summary>해당 지점이 속한 모니터의 DPI 배율 (96 DPI = 1.0).</summary>
    public static float DpiScaleAt(Point p)
    {
        try
        {
            var hmon = MonitorFromPoint(new POINT(p.X, p.Y), MONITOR_DEFAULTTONEAREST);
            if (hmon != IntPtr.Zero && GetDpiForMonitor(hmon, 0, out var dx, out _) == 0 && dx > 0)
                return dx / 96f;
        }
        catch { }
        return 1f;
    }

    /// <summary>
    /// 창이 자기 모니터 전체(작업 표시줄 포함)를 덮고 있는가. 게임·전체 화면 동영상·F11 브라우저가 해당한다.
    /// 최대화한 일반 창은 작업 영역(rcWork)까지만 덮으므로 여기에 걸리지 않는다.
    /// </summary>
    public static bool IsFullscreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return false;
        var hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hmon == IntPtr.Zero) return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hmon, ref mi)) return false;
        var m = mi.rcMonitor;
        if (m.Right - m.Left <= 0 || m.Bottom - m.Top <= 0) return false;
        // 바탕화면 자체(Progman/WorkerW)도 모니터 전체 크기라 제외한다.
        if (r.Left > m.Left || r.Top > m.Top || r.Right < m.Right || r.Bottom < m.Bottom) return false;
        string cls = ClassName(hwnd);
        return cls != "Progman" && cls != "WorkerW";
    }

    /// <summary>프로세스 ID 로 실행 파일 경로를 얻는다. 권한이 없거나 실패하면 null.</summary>
    public static string? ProcessImagePath(uint pid)
    {
        if (pid == 0) return null;
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }
}
