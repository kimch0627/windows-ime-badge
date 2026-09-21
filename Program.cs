using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ImeBadge;

// ─────────────────────────────────────────────────────────────────
// (1) Win32 함수 선언 (P/Invoke)
// ─────────────────────────────────────────────────────────────────
static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

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

    public delegate void WinEventProc(IntPtr hHook, uint evt, IntPtr hwnd, int idObject, int idChild,
                                      uint idEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("imm32.dll")]  public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc proc,
        uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hHook);

    // z-order 확인·조정
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

    // 레이어드 창(픽셀별 투명도) 그리기
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]  public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")]  public static extern bool DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")]  public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
    [DllImport("gdi32.dll")]  public static extern bool DeleteObject(IntPtr hObj);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey,
        ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
        int x, int y, int cx, int cy, uint flags);

    // 모니터별 DPI
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);

    public const uint WM_IME_CONTROL        = 0x0283;
    public const int  IMC_GETCONVERSIONMODE = 0x0001;
    public const int  IMC_GETOPENSTATUS     = 0x0005;
    public const uint IME_CMODE_HANGUL      = 0x0001;   // == IME_CMODE_NATIVE
    public const uint SMTO_ABORTIFHUNG      = 0x0002;
    public const ushort LANG_KOREAN         = 0x0412;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_IME_CHANGE = 0x8029;
    public const uint WINEVENT_OUTOFCONTEXT   = 0x0000;

    public const uint GW_HWNDPREV  = 3;           // z-order에서 바로 위(더 앞) 창
    public const int  GWL_EXSTYLE  = -20;
    public const int  WS_EX_TOPMOST = 0x00000008;

    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW  = 0x00000080;
    public const int WS_EX_LAYERED     = 0x00080000;
    public const int WS_EX_NOACTIVATE  = 0x08000000;

    public const uint ULW_ALPHA     = 0x02;
    public const byte AC_SRC_OVER   = 0x00;
    public const byte AC_SRC_ALPHA  = 0x01;
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public static readonly IntPtr HWND_TOPMOST = new(-1);   // SetWindowPos: 최상위(topmost) 창들 중에서도 맨 위로
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public static string ClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(null)";
        var sb = new StringBuilder(128);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
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
}

// ─────────────────────────────────────────────────────────────────
// (2) 설정 (exe 옆 imebadge.settings.json)
// ─────────────────────────────────────────────────────────────────
enum BadgeStyle { Box, Pill, Dot, Underline, DotFlash }
enum BadgePlacement { AboveRight, BelowRight }

sealed class Settings
{
    public BadgeStyle Style { get; set; } = BadgeStyle.Pill;
    public BadgePlacement Placement { get; set; } = BadgePlacement.AboveRight;
    public int SizePercent { get; set; } = 100;
    public int OpacityPercent { get; set; } = 100;

    static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "imebadge.settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.Settings) ?? new Settings();
        }
        catch (Exception ex) { Log.Write("settings load failed: " + ex.Message); }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJsonContext.Default.Settings)); }
        catch (Exception ex) { Log.Write("settings save failed: " + ex.Message); }
    }
}

/// <summary>
/// JSON 직렬화 코드를 컴파일 시점에 생성(source generator)한다. 리플렉션 기반 직렬화는 트리밍(trimming)하면
/// 프로퍼티가 잘려 나가거나 아예 꺼지므로(IsReflectionEnabledByDefault=false) 쓰면 안 된다.
/// 열거형(enum)은 예전 설정 파일과 호환되도록 계속 이름 문자열("Pill")로 저장한다.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
sealed partial class SettingsJsonContext : JsonSerializerContext { }

// ─────────────────────────────────────────────────────────────────
// (3) 디버그 로그 (--debug 옵션일 때만 exe 옆 imebadge.log에 기록)
// ─────────────────────────────────────────────────────────────────
static class Log
{
    public static bool Enabled;
    static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "imebadge.log");
    static string _lastLine = "";

    public static void Write(string line)
    {
        if (!Enabled) return;
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}"); } catch { }
    }

    public static void WriteIfChanged(string line)
    {
        if (!Enabled || line == _lastLine) return;
        _lastLine = line;
        Write(line);
    }
}

// ─────────────────────────────────────────────────────────────────
// (4) UI Automation으로 caret 위치 찾기 (Win32 caret이 없는 앱용)
//
// WPF의 System.Windows.Automation 대신 Windows에 내장된 COM UI Automation(UIAutomationCore.dll)을
// 직접 호출한다. 관리형 래퍼를 쓰면 csproj에 UseWPF=true가 필요해 self-contained exe에 WPF 전체
// (수십 MB)가 딸려 들어가기 때문이다.
//
// 아래 COM 인터페이스 선언은 UIAutomationClient.h의 vtable 순서를 그대로 따른다. 실제로 쓰지 않는
// 메서드는 자리만 맞추는 placeholder(_Slot*)로 두었다. 순서가 하나라도 어긋나면 엉뚱한 함수가
// 불리므로, 메서드를 추가할 때는 반드시 헤더의 순서를 확인한다.
// ─────────────────────────────────────────────────────────────────
static class Uia
{
    public const int UIA_BoundingRectanglePropertyId = 30001;
    public const int UIA_ControlTypePropertyId       = 30003;
    public const int UIA_ClassNamePropertyId         = 30012;
    public const int UIA_HasKeyboardFocusPropertyId  = 30008;
    public const int UIA_FrameworkIdPropertyId       = 30024;
    public const int UIA_ValuePatternId  = 10002;
    public const int UIA_TextPatternId   = 10014;
    public const int UIA_ComboBoxControlTypeId = 50003;
    public const int UIA_EditControlTypeId     = 50004;

    public enum TextPatternRangeEndpoint { Start = 0, End = 1 }
    public enum TextUnit { Character = 0, Format, Word, Line, Paragraph, Page, Document }

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e"), ClassInterface(ClassInterfaceType.None)]
    public class CUIAutomation { }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomation
    {
        void _Slot_CompareElements();
        void _Slot_CompareRuntimeIds();
        void _Slot_GetRootElement();
        void _Slot_ElementFromHandle();
        void _Slot_ElementFromPoint();
        IUIAutomationElement GetFocusedElement();
        // 이후 메서드는 쓰지 않으므로 생략
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationElement
    {
        void _Slot_SetFocus();
        void _Slot_GetRuntimeId();
        void _Slot_FindFirst();
        void _Slot_FindAll();
        void _Slot_FindFirstBuildCache();
        void _Slot_FindAllBuildCache();
        void _Slot_BuildUpdatedCache();
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);
        void _Slot_GetCurrentPropertyValueEx();
        void _Slot_GetCachedPropertyValue();
        void _Slot_GetCachedPropertyValueEx();
        void _Slot_GetCurrentPatternAs();
        void _Slot_GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)] object? GetCurrentPattern(int patternId);
        // 이후 메서드는 쓰지 않으므로 생략
    }

    [ComImport, Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationValuePattern
    {
        void _Slot_SetValue();
        void _Slot_get_CurrentValue();
        [return: MarshalAs(UnmanagedType.Bool)] bool get_CurrentIsReadOnly();
        // 이후 메서드는 쓰지 않으므로 생략
    }

    [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTextPattern
    {
        void _Slot_RangeFromPoint();
        void _Slot_RangeFromChild();
        IUIAutomationTextRangeArray GetSelection();
        // 이후 메서드는 쓰지 않으므로 생략
    }

    [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTextRangeArray
    {
        int get_Length();
        IUIAutomationTextRange GetElement(int index);
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUIAutomationTextRange
    {
        void _Slot_Clone();
        void _Slot_Compare();
        void _Slot_CompareEndpoints();
        void ExpandToEnclosingUnit(TextUnit unit);
        void _Slot_FindAttribute();
        void _Slot_FindText();
        void _Slot_GetAttributeValue();
        /// <summary>사각형마다 (left, top, width, height) 4개씩 이어진 double 배열.</summary>
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] double[] GetBoundingRectangles();
        void _Slot_GetEnclosingElement();
        void _Slot_GetText();
        void _Slot_Move();
        void _Slot_MoveEndpointByUnit();
        void MoveEndpointByRange(TextPatternRangeEndpoint srcEndPoint, IUIAutomationTextRange range, TextPatternRangeEndpoint targetEndPoint);
        // 이후 메서드는 쓰지 않으므로 생략
    }

    static IUIAutomation? _automation;

    /// <summary>프로세스에 하나만 만들어 재사용하는 UIA 클라이언트 객체.</summary>
    public static IUIAutomation Client => _automation ??= (IUIAutomation)new CUIAutomation();

    /// <summary>COM 객체를 즉시 놓아준다. 100ms마다 만드는 객체가 GC까지 쌓이지 않게.</summary>
    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
            try { Marshal.ReleaseComObject(com); } catch { }
    }
}

static class UiaCaret
{
    /// <summary>caret 사각형(화면 좌표). 정확한 caret을 못 찾으면 입력창 왼쪽 아래 1x1(높이 0)로 근사.</summary>
    public static Rectangle? Find(StringBuilder? dump)
    {
        Uia.IUIAutomationElement? el = null;
        object? valuePat = null, textPat = null;
        Uia.IUIAutomationTextRangeArray? sel = null;
        Uia.IUIAutomationTextRange? r = null;
        try
        {
            el = Uia.Client.GetFocusedElement();
            if (el is null) return null;

            valuePat = el.GetCurrentPattern(Uia.UIA_ValuePatternId);
            if (valuePat is Uia.IUIAutomationValuePattern v && v.get_CurrentIsReadOnly())
            {
                dump?.Append(" uia:readonly");
                return null;
            }

            textPat = el.GetCurrentPattern(Uia.UIA_TextPatternId);
            if (textPat is Uia.IUIAutomationTextPattern tp)
            {
                sel = tp.GetSelection();
                if (sel is not null && sel.get_Length() > 0)
                {
                    r = sel.GetElement(0);
                    // 크롬 주소창 등은 "문서 처음~커서" 범위를 준다. 시작점을 끝점으로 옮겨 커서 한 점으로 접는다.
                    r.MoveEndpointByRange(Uia.TextPatternRangeEndpoint.Start, r, Uia.TextPatternRangeEndpoint.End);
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        var rects = r.GetBoundingRectangles();   // [l, t, w, h, l, t, w, h, ...]
                        if (rects.Length >= 4 && rects[3] > 0)
                        {
                            double left = rects[0], top = rects[1], height = rects[3];
                            dump?.Append($" uia:text({left:F0},{top + height:F0})");
                            return new Rectangle((int)left, (int)top, 1, (int)height);
                        }
                        r.ExpandToEnclosingUnit(Uia.TextUnit.Character);   // 넓이 0인 커서 범위 → 글자 한 칸으로
                    }
                }
            }

            int ct = el.GetCurrentPropertyValue(Uia.UIA_ControlTypePropertyId) is int i ? i : 0;
            if (ct == Uia.UIA_EditControlTypeId || ct == Uia.UIA_ComboBoxControlTypeId)
            {
                // BoundingRectangle 속성은 (left, top, width, height) double 4개
                if (el.GetCurrentPropertyValue(Uia.UIA_BoundingRectanglePropertyId) is double[] b && b.Length >= 4 && b[2] > 0)
                {
                    double left = b[0], bottom = b[1] + b[3];
                    dump?.Append($" uia:elem({left:F0},{bottom:F0})");
                    return new Rectangle((int)left, (int)bottom, 1, 0);   // 높이 0 = 근사 위치
                }
            }
            if (dump is not null)
            {
                // 어떤 컨트롤이라서 못 찾았는지 남긴다. (컨트롤 종류 ID, 클래스, UI 프레임워크, TextPattern 유무)
                string cls = el.GetCurrentPropertyValue(Uia.UIA_ClassNamePropertyId) as string ?? "";
                string fw  = el.GetCurrentPropertyValue(Uia.UIA_FrameworkIdPropertyId) as string ?? "";
                bool focus = el.GetCurrentPropertyValue(Uia.UIA_HasKeyboardFocusPropertyId) is bool f && f;
                dump.Append($" uia:none(ct={ct} class='{cls}' fw={fw} text={(textPat is not null ? 1 : 0)} focus={(focus ? 1 : 0)})");
            }
        }
        catch (Exception ex) { dump?.Append($" uia:EXC {ex.GetType().Name} 0x{ex.HResult:X8}"); }
        finally
        {
            Uia.Release(r); Uia.Release(sel); Uia.Release(textPat); Uia.Release(valuePat); Uia.Release(el);
        }
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────
// (5) 현재 상태 한 번 읽기
// ─────────────────────────────────────────────────────────────────
enum ImeState { Unknown, Hangul, English, OtherLang }

readonly record struct Snapshot(ImeState State, Rectangle? Caret, IntPtr Foreground = default);

static class ImeReader
{
    public static Snapshot Read(IntPtr selfHandle)
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == selfHandle) return new(ImeState.Unknown, null);

        uint tid = Native.GetWindowThreadProcessId(fg, IntPtr.Zero);
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
            Log.WriteIfChanged($"fg='{Native.ClassName(fg)}' focus='{Native.ClassName(gti.hwndFocus)}' tid={tid} => {state}{dump}");
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
}

// ─────────────────────────────────────────────────────────────────
// (6) 배지 그리기 (GDI+로 투명 비트맵 생성)
// ─────────────────────────────────────────────────────────────────
static class BadgeRenderer
{
    public static (string text, Color color) Look(ImeState s) => s switch
    {
        ImeState.Hangul  => ("한", Color.FromArgb(0, 120, 215)),
        ImeState.English => ("A",  Color.FromArgb(60, 60, 60)),
        _                => ("?",  Color.DarkOrange),
    };

    public static Bitmap Render(ImeState state, BadgeStyle style, float scale)
    {
        var (text, color) = Look(state);
        return style switch
        {
            BadgeStyle.Dot       => RenderDot(color, scale),
            BadgeStyle.Underline => RenderUnderline(color, scale),
            BadgeStyle.Box       => RenderText(text, color, scale, rounded: false),
            _                    => RenderText(text, color, scale, rounded: true),   // Pill, DotFlash(글자 단계)
        };
    }

    static Bitmap NewCanvas(int w, int h, out Graphics g)
    {
        var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppPArgb);
        g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        return bmp;
    }

    static Bitmap RenderDot(Color color, float scale)
    {
        int d = (int)Math.Round(9 * scale);
        var bmp = NewCanvas(d + 2, d + 2, out var g);
        using (g)
        {
            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.FromArgb(160, Color.White), Math.Max(1f, scale));
            g.FillEllipse(brush, 1, 1, d, d);
            g.DrawEllipse(pen, 1, 1, d, d);
        }
        return bmp;
    }

    static Bitmap RenderUnderline(Color color, float scale)
    {
        int w = (int)Math.Round(16 * scale), h = (int)Math.Round(3 * scale);
        var bmp = NewCanvas(w, h, out var g);
        using (g)
        {
            using var brush = new SolidBrush(color);
            using var path = RoundedRect(new RectangleF(0, 0, w, h), h / 2f);
            g.FillPath(brush, path);
        }
        return bmp;
    }

    static Bitmap RenderText(string text, Color color, float scale, bool rounded)
    {
        float fontPx = 13 * scale;
        using var font = new Font("Malgun Gothic", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        SizeF ts;
        using (var probeBmp = new Bitmap(1, 1))
        using (var probe = Graphics.FromImage(probeBmp))
            ts = probe.MeasureString(text, font);

        int h = (int)Math.Round(20 * scale);
        int w = (int)Math.Round(Math.Max(h, ts.Width + 10 * scale));
        var bmp = NewCanvas(w, h, out var g);
        using (g)
        {
            var rect = new RectangleF(0.5f, 0.5f, w - 1, h - 1);
            using var path = RoundedRect(rect, rounded ? (h - 1) / 2f : 3 * scale);
            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.FromArgb(110, Color.White), 1f);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, w, h), sf);
        }
        return bmp;
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// ─────────────────────────────────────────────────────────────────
// (7) caret 옆에 뜨는 배지 창 (레이어드 창 + 트레이 메뉴)
// ─────────────────────────────────────────────────────────────────
sealed class BadgeForm : Form
{
    readonly Settings _settings;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    readonly NotifyIcon _tray;
    readonly Native.WinEventProc _imeChangeProc;   // GC 회수 방지용 필드
    IntPtr _hook, _fgHook;

    ImeState _lastState = ImeState.Unknown;
    DateTime _flashUntil = DateTime.MinValue;       // DotFlash: 변경 직후 글자를 보여 주는 시한
    (ImeState state, BadgeStyle style, float scale, int opacity) _renderKey = (ImeState.Unknown, (BadgeStyle)(-1), 0, -1);
    Size _bitmapSize;
    Point _lastPos = new(int.MinValue, int.MinValue);
    IntPtr _lastFg;                                 // 마지막으로 배지를 띄웠을 때의 활성 창
    bool _allowShow;

    public BadgeForm(Settings settings)
    {
        _settings = settings;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;   // DPI 변경 시 WinForms가 창 크기를 건드리지 않게
        Size = new Size(1, 1);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ImeBadge (한/영 배지)" + (Log.Enabled ? " [debug]" : ""),
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };

        _timer.Tick += (_, _) => Poll();
        _timer.Start();

        _imeChangeProc = (_, _, _, _, _, _, _) => Poll();
        _hook = Native.SetWinEventHook(Native.EVENT_OBJECT_IME_CHANGE, Native.EVENT_OBJECT_IME_CHANGE,
                                       IntPtr.Zero, _imeChangeProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        Log.Write(_hook == IntPtr.Zero ? "IME change hook FAILED" : "IME change hook registered");
        // 활성 창이 바뀌면 100ms 타이머를 기다리지 않고 바로 다시 읽는다. (최상위 창 위로 배지를 올리는 지연을 줄임)
        _fgHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                                         IntPtr.Zero, _imeChangeProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
    }

    // ── 트레이 메뉴 ──
    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var shape = new ToolStripMenuItem("모양(&S)");
        AddRadio(shape, "사각 배지  [한]", () => _settings.Style == BadgeStyle.Box,       () => _settings.Style = BadgeStyle.Box);
        AddRadio(shape, "둥근 배지  (한)", () => _settings.Style == BadgeStyle.Pill,      () => _settings.Style = BadgeStyle.Pill);
        AddRadio(shape, "점  ●",            () => _settings.Style == BadgeStyle.Dot,       () => _settings.Style = BadgeStyle.Dot);
        AddRadio(shape, "밑줄  ▬",          () => _settings.Style == BadgeStyle.Underline, () => _settings.Style = BadgeStyle.Underline);
        AddRadio(shape, "점 + 바뀔 때만 글자", () => _settings.Style == BadgeStyle.DotFlash, () => _settings.Style = BadgeStyle.DotFlash);
        menu.Items.Add(shape);

        var place = new ToolStripMenuItem("위치(&P)");
        AddRadio(place, "커서 오른쪽 위",   () => _settings.Placement == BadgePlacement.AboveRight, () => _settings.Placement = BadgePlacement.AboveRight);
        AddRadio(place, "커서 오른쪽 아래", () => _settings.Placement == BadgePlacement.BelowRight, () => _settings.Placement = BadgePlacement.BelowRight);
        menu.Items.Add(place);

        var size = new ToolStripMenuItem("크기(&Z)");
        foreach (var (label, pct) in new[] { ("작게 (80%)", 80), ("보통 (100%)", 100), ("크게 (130%)", 130), ("아주 크게 (160%)", 160) })
            AddRadio(size, label, () => _settings.SizePercent == pct, () => _settings.SizePercent = pct);
        menu.Items.Add(size);

        var opacity = new ToolStripMenuItem("투명도(&O)");
        foreach (var (label, pct) in new[] { ("불투명 (100%)", 100), ("살짝 비침 (85%)", 85), ("반투명 (70%)", 70), ("많이 비침 (50%)", 50) })
            AddRadio(opacity, label, () => _settings.OpacityPercent == pct, () => _settings.OpacityPercent = pct);
        menu.Items.Add(opacity);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료(&X)", null, (_, _) => Application.Exit());

        // 메뉴를 열 때마다 체크 표시를 현재 설정에 맞춘다.
        menu.Opening += (_, _) => RefreshChecks(menu.Items);
        return menu;
    }

    static void RefreshChecks(ToolStripItemCollection items)
    {
        foreach (ToolStripItem it in items)
        {
            if (it is not ToolStripMenuItem mi) continue;
            if (mi.Tag is Func<bool> isOn) mi.Checked = isOn();
            RefreshChecks(mi.DropDownItems);
        }
    }

    void AddRadio(ToolStripMenuItem parent, string text, Func<bool> isOn, Action apply)
    {
        var item = new ToolStripMenuItem(text) { Tag = isOn };
        item.Click += (_, _) =>
        {
            apply();
            _settings.Save();
            _renderKey = (ImeState.Unknown, (BadgeStyle)(-1), 0, -1);   // 다음 틱에 강제로 다시 그림
            _flashUntil = DateTime.Now.AddMilliseconds(1500);       // DotFlash면 바로 글자를 한 번 보여 준다
            Poll();
        };
        parent.DropDownItems.Add(item);
    }

    // ── 창 속성 ──
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW
                        | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            return cp;
        }
    }
    protected override bool ShowWithoutActivation => true;
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(_allowShow && value);
    protected override void OnPaintBackground(PaintEventArgs e) { }   // 레이어드 창은 직접 그린다

    // ── 갱신 ──
    void Poll() => Apply(ImeReader.Read(Handle));

    void Apply(Snapshot s)
    {
        if (s.Caret is null || s.State == ImeState.Unknown)
        {
            if (Visible) Hide();
            return;
        }

        var caret = s.Caret.Value;
        if (s.State != _lastState)
        {
            _lastState = s.State;
            _flashUntil = DateTime.Now.AddMilliseconds(1500);
        }

        // DotFlash: 변경 직후 1.5초는 둥근 배지, 그 뒤는 점
        var style = _settings.Style;
        if (style == BadgeStyle.DotFlash)
            style = DateTime.Now < _flashUntil ? BadgeStyle.Pill : BadgeStyle.Dot;

        float scale = Native.DpiScaleAt(caret.Location) * _settings.SizePercent / 100f;

        var key = (s.State, style, scale, _settings.OpacityPercent);
        bool needRender = key != _renderKey;

        // 배지 위치 계산
        int gap = (int)Math.Round(3 * scale);
        Size bs = needRender ? Size.Empty : _bitmapSize;
        Bitmap? bmp = null;
        if (needRender)
        {
            bmp = BadgeRenderer.Render(s.State, style, scale);
            bs = bmp.Size;
        }

        Point pos;
        bool approx = caret.Height == 0;   // UIA가 입력창 모서리로 근사한 경우: 항상 아래쪽
        if (style == BadgeStyle.Underline)
            pos = new Point(caret.Left + caret.Width / 2 - bs.Width / 2, caret.Bottom + 1);
        else if (_settings.Placement == BadgePlacement.AboveRight && !approx)
            pos = new Point(caret.Right + gap, caret.Top - bs.Height - gap);
        else
            pos = new Point(caret.Right + gap, caret.Bottom + gap);

        var area = Screen.FromPoint(caret.Location).WorkingArea;
        if (pos.Y < area.Top) pos.Y = caret.Bottom + gap;                // 위에 자리가 없으면 아래로
        pos.X = Math.Max(area.Left, Math.Min(pos.X, area.Right - bs.Width));
        pos.Y = Math.Max(area.Top, Math.Min(pos.Y, area.Bottom - bs.Height));

        // FlowLauncher처럼 자기도 최상위(TopMost)인 창은 나중에 뜬 쪽이 위에 온다. 배지가 처음 보일 때,
        // 활성 창이 바뀌었을 때, 위치가 바뀌었을 때마다 최상위 창들 중에서도 맨 위로 다시 올린다.
        bool raise = !Visible || s.Foreground != _lastFg || _lastPos != pos;

        _allowShow = true;
        if (!Visible) Show();

        if (bmp is not null)
        {
            using (bmp) Present(bmp, pos);
            _renderKey = key;
            _bitmapSize = bs;
            if (raise) RaiseToTop();
        }
        else if (_lastPos != pos)
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, pos.X, pos.Y, 0, 0,
                                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);   // 이동과 동시에 맨 위로
        }
        else if (raise) RaiseToTop();

        // 활성 창이 자기도 최상위(TopMost)라면(FlowLauncher 등) 매 틱 실제 z-order를 확인한다. 활성 창이 뒤늦게
        // 활성화되며 다시 위로 올라오거나, 백그라운드 프로세스의 z-order 변경이 활성 창 아래로 제한되는 경우가 있다.
        if (Native.IsTopmost(s.Foreground) && Native.IsAbove(s.Foreground, Handle))
            RaiseAbove(s.Foreground);

        _lastPos = pos;
        _lastFg = s.Foreground;
    }

    /// <summary>위치·크기는 그대로 두고 z-order만 최상위 창들 중 맨 위로 올린다. 포커스는 건드리지 않는다.</summary>
    void RaiseToTop() =>
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    /// <summary>
    /// 활성 창 <paramref name="fg"/>보다 위로 올린다. 보통의 SetWindowPos로 안 되면(Windows는 마지막 입력을 받지 않은
    /// 프로세스가 활성 창 위로 창을 올리는 것을 막는다) 활성 창 스레드의 입력 큐에 잠깐 붙어 그 권한을 빌린 뒤 바로 떼어 낸다.
    /// </summary>
    void RaiseAbove(IntPtr fg)
    {
        RaiseToTop();
        if (!Native.IsAbove(fg, Handle)) return;

        uint fgTid = Native.GetWindowThreadProcessId(fg, IntPtr.Zero), me = Native.GetCurrentThreadId();
        if (fgTid == 0 || fgTid == me) return;
        if (!Native.AttachThreadInput(me, fgTid, true)) { Log.WriteIfChanged($"raise: AttachThreadInput failed fg='{Native.ClassName(fg)}'"); return; }
        try { RaiseToTop(); }
        finally { Native.AttachThreadInput(me, fgTid, false); }
        Log.WriteIfChanged($"raise: via AttachThreadInput fg='{Native.ClassName(fg)}' ok={!Native.IsAbove(fg, Handle)}");
    }

    /// <summary>비트맵을 픽셀별 알파로 창에 올리면서 위치·크기도 함께 지정한다.</summary>
    void Present(Bitmap bmp, Point pos)
    {
        if (Size != bmp.Size) Size = bmp.Size;   // WinForms가 아는 크기와 실제 창 크기를 일치시킨다
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = Native.SelectObject(memDc, hBmp);
            var size = new Native.SIZE(bmp.Width, bmp.Height);
            var src = new Native.POINT(0, 0);
            var dst = new Native.POINT(pos.X, pos.Y);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER, BlendFlags = 0,
                SourceConstantAlpha = (byte)Math.Clamp(255 * _settings.OpacityPercent / 100, 30, 255),
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
            if (hBmp != IntPtr.Zero) Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_hook != IntPtr.Zero) { Native.UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
            if (_fgHook != IntPtr.Zero) { Native.UnhookWinEvent(_fgHook); _fgHook = IntPtr.Zero; }
            _timer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // 창 없는 프로그램이라 예외가 나면 조용히 죽는다. --debug 여부와 상관없이 exe 옆 imebadge-crash.log 에
        // 남기고 메시지 상자로 알린다. (트리밍 빌드에서 잘려 나간 코드를 찾을 때 특히 필요)
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Log.Enabled = args.Contains("--debug");
        Log.Write("=== ImeBadge start ===");
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new BadgeForm(Settings.Load()));
        }
        catch (Exception ex) { ReportCrash(ex); }
    }

    static void ReportCrash(Exception? ex)
    {
        if (ex is null) return;
        string text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ImeBadge crashed{Environment.NewLine}{ex}{Environment.NewLine}";
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "imebadge-crash.log"), text); } catch { }
        try { MessageBox.Show(ex.ToString(), "ImeBadge 오류", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
        Environment.Exit(1);
    }
}
