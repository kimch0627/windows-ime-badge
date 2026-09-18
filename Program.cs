using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
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
    public struct POINT { public int X, Y; }

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

    public const uint WM_IME_CONTROL        = 0x0283;
    public const int  IMC_GETCONVERSIONMODE = 0x0001;
    public const int  IMC_GETOPENSTATUS     = 0x0005;
    public const uint IME_CMODE_HANGUL      = 0x0001;   // == IME_CMODE_NATIVE
    public const uint SMTO_ABORTIFHUNG      = 0x0002;
    public const ushort LANG_KOREAN         = 0x0412;

    public const uint EVENT_OBJECT_IME_CHANGE = 0x8029;  // IME 상태가 바뀔 때 OS가 쏘는 이벤트
    public const uint WINEVENT_OUTOFCONTEXT   = 0x0000;

    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW  = 0x00000080;
    public const int WS_EX_LAYERED     = 0x00080000;
    public const int WS_EX_NOACTIVATE  = 0x08000000;

    public static string ClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(null)";
        var sb = new StringBuilder(128);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}

// ─────────────────────────────────────────────────────────────────
// (2) 디버그 로그 (--debug 옵션일 때만 exe 옆 imebadge.log에 기록)
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

    /// <summary>같은 내용이 연속으로 오면 한 번만 기록한다.</summary>
    public static void WriteIfChanged(string line)
    {
        if (!Enabled || line == _lastLine) return;
        _lastLine = line;
        Write(line);
    }
}

// ─────────────────────────────────────────────────────────────────
// (3) UI Automation으로 caret 위치 찾기 (Win32 caret이 없는 앱용)
//     Chrome/Edge/Electron(Claude 앱 등)은 Win32 caret을 만들지 않으므로
//     접근성(accessibility) API로 "지금 선택 영역(=커서)"의 사각형을 묻는다.
// ─────────────────────────────────────────────────────────────────
static class UiaCaret
{
    public static Point? Find(StringBuilder? dump)
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el is null) return null;

            // 읽기 전용이라고 스스로 밝힌 요소(읽기 전용 문서 등)에는 배지를 띄우지 않는다.
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) && vp is ValuePattern v && v.Current.IsReadOnly)
            {
                dump?.Append(" uia:readonly");
                return null;
            }

            if (el.TryGetCurrentPattern(TextPattern.Pattern, out var pat) && pat is TextPattern tp)
            {
                var sel = tp.GetSelection();
                if (sel.Length > 0)
                {
                    var r = sel[0].Clone();
                    // 일부 컨트롤(크롬 주소창 등)은 "문서 처음~커서" 범위를 돌려준다.
                    // 시작점을 끝점으로 옮겨 커서 한 점으로 접는다. 이미 한 점이면 무해.
                    r.MoveEndpointByRange(TextPatternRangeEndpoint.Start, r, TextPatternRangeEndpoint.End);

                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        var rects = r.GetBoundingRectangles();
                        if (rects.Length > 0 && rects[0].Height > 0)
                        {
                            var b = rects[0];
                            dump?.Append($" uia:text({b.Left:F0},{b.Bottom:F0})");
                            return new Point((int)b.Left, (int)b.Bottom);
                        }
                        // 커서만 있는 범위는 넓이가 0이라 사각형이 안 나온다. 글자 한 칸으로 넓혀 재시도.
                        r.ExpandToEnclosingUnit(TextUnit.Character);
                    }
                }
            }

            // TextPattern이 없으면 입력 컨트롤의 왼쪽 아래 모서리로 대신한다.
            var ct = el.Current.ControlType;
            if (ct == ControlType.Edit || ct == ControlType.ComboBox)
            {
                var b = el.Current.BoundingRectangle;
                if (!b.IsEmpty && b.Width > 0)
                {
                    dump?.Append($" uia:elem({b.Left:F0},{b.Bottom:F0})");
                    return new Point((int)b.Left, (int)b.Bottom);
                }
            }
            dump?.Append($" uia:none(ct={ct.ProgrammaticName})");
        }
        catch (Exception ex)
        {
            dump?.Append(" uia:EXC " + ex.GetType().Name);
        }
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────
// (4) 현재 상태 한 번 읽기
// ─────────────────────────────────────────────────────────────────
enum ImeState { Unknown, Hangul, English, OtherLang }

readonly record struct Snapshot(ImeState State, Point? Caret);

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

        // ── caret 위치: 1순위 Win32 caret, 2순위 UI Automation ──
        Point? caret = null;
        if (gti.hwndCaret != IntPtr.Zero && gti.rcCaret.Bottom > gti.rcCaret.Top)
        {
            var pt = new Native.POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Bottom };
            Native.ClientToScreen(gti.hwndCaret, ref pt);
            caret = new Point(pt.X, pt.Y);
            dump?.Append(" caret:win32");
        }
        else
        {
            caret = UiaCaret.Find(dump);
        }

        // ── 한/영 상태 ──
        var state = ReadImeState(fg, gti.hwndFocus, tid, dump);

        if (dump is not null)
            Log.WriteIfChanged($"fg='{Native.ClassName(fg)}' focus='{Native.ClassName(gti.hwndFocus)}' tid={tid} => {state}{dump}");

        return new(state, caret);
    }

    /// <summary>
    /// 한국어 IME의 한/영 판정.
    /// 핵심: 한/영 키는 IME의 "열림(open)"이 아니라 "변환 모드(conversion mode)"의
    /// 한글 비트(IME_CMODE_HANGUL)를 바꾼다. 열림 상태는 IME가 한 번 켜진 뒤로는 계속 1이므로
    /// 열림 상태로 한글을 판정하면 첫 전환 이후 영원히 "한"에 묶인다.
    /// </summary>
    static ImeState ReadImeState(IntPtr fg, IntPtr hwndFocus, uint tid, StringBuilder? dump)
    {
        // 키보드 레이아웃이 한국어가 아니면(ENG 레이아웃 등) 한/영 개념이 없다.
        ushort lang = (ushort)((long)Native.GetKeyboardLayout(tid) & 0xFFFF);
        if (lang != Native.LANG_KOREAN) { dump?.Append($" lang=0x{lang:X4}"); return ImeState.OtherLang; }

        // UWP·콘솔처럼 hwndFocus가 0인 창은 최상위 창으로 대신 묻는다.
        var target = hwndFocus != IntPtr.Zero ? hwndFocus : fg;
        var imeWnd = Native.ImmGetDefaultIMEWnd(target);
        if (imeWnd == IntPtr.Zero) { dump?.Append(" imeWnd=0"); return ImeState.Unknown; }

        var ok = Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, Native.IMC_GETOPENSTATUS,
                                           IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out var open);
        if (ok == IntPtr.Zero) { dump?.Append(" open:timeout"); return ImeState.Unknown; }

        // IME가 닫혀 있으면(아직 한 번도 한글을 안 쓴 창) 영문 입력이다.
        if (open == IntPtr.Zero) { dump?.Append(" open=0"); return ImeState.English; }

        ok = Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, Native.IMC_GETCONVERSIONMODE,
                                       IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out var mode);
        if (ok == IntPtr.Zero) { dump?.Append(" mode:timeout"); return ImeState.Unknown; }

        dump?.Append($" open=1 mode=0x{(long)mode:X}");
        return (((uint)(long)mode) & Native.IME_CMODE_HANGUL) != 0 ? ImeState.Hangul : ImeState.English;
    }
}

// ─────────────────────────────────────────────────────────────────
// (5) caret 옆에 뜨는 배지 창
// ─────────────────────────────────────────────────────────────────
sealed class BadgeForm : Form
{
    readonly Label _label = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Malgun Gothic", 10, FontStyle.Bold),
        ForeColor = Color.White,
    };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    readonly NotifyIcon _tray;
    ImeState _lastState = ImeState.Unknown;
    bool _allowShow;

    // WinEvent 훅: OS가 "IME 상태 바뀜"을 알려 주면 100ms 폴링을 기다리지 않고 즉시 갱신한다.
    // 델리게이트를 필드에 붙잡아 두지 않으면 GC가 회수해 네이티브 쪽에서 크래시가 난다.
    readonly Native.WinEventProc _imeChangeProc;
    IntPtr _hook;

    public BadgeForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(28, 22);
        Opacity = 0.9;
        Controls.Add(_label);

        var menu = new ContextMenuStrip();
        menu.Items.Add("종료(&X)", null, (_, _) => Application.Exit());
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ImeBadge (한/영 배지)" + (Log.Enabled ? " [debug]" : ""),
            ContextMenuStrip = menu,
            Visible = true,
        };

        _timer.Tick += (_, _) => Poll();
        _timer.Start();

        _imeChangeProc = (_, _, _, _, _, _, _) => Poll();
        _hook = Native.SetWinEventHook(Native.EVENT_OBJECT_IME_CHANGE, Native.EVENT_OBJECT_IME_CHANGE,
                                       IntPtr.Zero, _imeChangeProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        Log.Write(_hook == IntPtr.Zero ? "IME change hook FAILED" : "IME change hook registered");
    }

    void Poll() => Apply(ImeReader.Read(Handle));

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

    void Apply(Snapshot s)
    {
        if (s.Caret is null || s.State == ImeState.Unknown)
        {
            if (Visible) Hide();
            return;
        }

        if (s.State != _lastState)
        {
            (_label.Text, BackColor) = s.State switch
            {
                ImeState.Hangul  => ("한", Color.FromArgb(0, 120, 215)),
                ImeState.English => ("A",  Color.FromArgb(60, 60, 60)),
                _                => ("?",  Color.DarkOrange),
            };
            _lastState = s.State;
        }

        var p = s.Caret.Value;
        var pos = new Point(p.X + 4, p.Y + 4);
        var area = Screen.FromPoint(p).WorkingArea;
        pos.X = Math.Min(pos.X, area.Right - Width);
        pos.Y = Math.Min(pos.Y, area.Bottom - Height);
        if (Location != pos) Location = pos;

        _allowShow = true;
        if (!Visible) Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_hook != IntPtr.Zero) { Native.UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
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
        Log.Enabled = args.Contains("--debug");
        Log.Write("=== ImeBadge start ===");
        ApplicationConfiguration.Initialize();
        Application.Run(new BadgeForm());
    }
}
