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

    public const uint WM_IME_CONTROL        = 0x0283;
    public const int  IMC_GETCONVERSIONMODE = 0x0001;
    public const int  IMC_GETOPENSTATUS     = 0x0005;
    public const int  IME_CMODE_NATIVE      = 0x0001;
    public const uint SMTO_ABORTIFHUNG      = 0x0002;
    public const ushort LANG_KOREAN         = 0x0412;

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
// (2) TSF(Text Services Framework) 언어 표시줄 COM 인터페이스
//     윈도우 트레이의 "한/A" 표시기가 다른 프로세스의 IME 상태를 읽을 때
//     쓰는 바로 그 통로다. vtable 순서는 Windows SDK ctfutb.h와 동일해야 한다.
// ─────────────────────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct TF_LANGBARITEMINFO
{
    public Guid clsidService;
    public Guid guidItem;
    public uint dwStyle;
    public uint ulSort;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDescription;
}

[ComImport, Guid("73540d69-edeb-4ee9-96c9-23aa30b25916"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITfLangBarItem
{
    [PreserveSig] int GetInfo(out TF_LANGBARITEMINFO pInfo);
    [PreserveSig] int GetStatus(out uint pdwStatus);
    [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool fShow);
    [PreserveSig] int GetTooltipString([MarshalAs(UnmanagedType.BStr)] out string pbstrToolTip);
}

[ComImport, Guid("28c7f1d0-de25-11d2-afdd-00105a2799b5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITfLangBarItemButton
{
    // ITfLangBarItem (COM 인터페이스 상속은 C#에서 메서드를 다시 나열해야 한다)
    [PreserveSig] int GetInfo(out TF_LANGBARITEMINFO pInfo);
    [PreserveSig] int GetStatus(out uint pdwStatus);
    [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool fShow);
    [PreserveSig] int GetTooltipString([MarshalAs(UnmanagedType.BStr)] out string pbstrToolTip);
    // ITfLangBarItemButton
    [PreserveSig] int OnClick(int click, Native.POINT pt, ref Native.RECT prcArea);
    [PreserveSig] int InitMenu(IntPtr pMenu);
    [PreserveSig] int OnMenuSelect(uint wID);
    [PreserveSig] int GetIcon(out IntPtr phIcon);
    [PreserveSig] int GetText([MarshalAs(UnmanagedType.BStr)] out string pbstrText);
}

[ComImport, Guid("583f34d0-de25-11d2-afdd-00105a2799b5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEnumTfLangBarItems
{
    [PreserveSig] int Clone(out IEnumTfLangBarItems ppEnum);
    [PreserveSig] int Next(uint ulCount, out ITfLangBarItem ppItem, out uint pcFetched);
    [PreserveSig] int Reset();
    [PreserveSig] int Skip(uint ulCount);
}

[ComImport, Guid("ba468c55-9956-4fb1-a59d-52a7dd7cc6aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITfLangBarItemMgr
{
    [PreserveSig] int EnumItems(out IEnumTfLangBarItems ppEnum);
    [PreserveSig] int GetItem(ref Guid rguid, out ITfLangBarItem ppItem);
    [PreserveSig] int AddItem(ITfLangBarItem punk);
    [PreserveSig] int RemoveItem(ITfLangBarItem punk);
    [PreserveSig] int AdviseItemSink(IntPtr punk, out uint pdwCookie, ref Guid rguidItem);
    [PreserveSig] int UnadviseItemSink(uint dwCookie);
    [PreserveSig] int GetItemFloatingRect(uint dwThreadId, ref Guid rguid, out Native.RECT prc);
    [PreserveSig] int GetItemsStatus(uint ulCount, IntPtr prgguid, IntPtr pdwStatus);
    [PreserveSig] int GetItemNum(out uint pulCount);
    [PreserveSig] int GetItems(uint ulCount, IntPtr ppItem, IntPtr pInfo, IntPtr pdwStatus, out uint pcFetched);
    [PreserveSig] int AdviseItemsSink(uint ulCount, IntPtr ppunk, IntPtr pguidItem, IntPtr pdwCookie);
    [PreserveSig] int UnadviseItemsSink(uint ulCount, IntPtr pdwCookie);
}

[ComImport, Guid("87955690-e627-11d2-8ddb-00105a2799b5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ITfLangBarMgr
{
    [PreserveSig] int AdviseEventSink(IntPtr pSink, IntPtr hwnd, uint dwFlags, out uint pdwCookie);
    [PreserveSig] int UnadviseEventSink(uint dwCookie);
    [PreserveSig] int GetThreadMarshalInterface(uint dwThreadId, uint dwType, ref Guid riid,
                                                 [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);
    [PreserveSig] int GetThreadLangBarItemMgr(uint dwThreadId, out ITfLangBarItemMgr pplbie, out uint pdwThreadid);
    [PreserveSig] int GetInputProcessorProfiles(uint dwThreadId,
                                                 [MarshalAs(UnmanagedType.IUnknown)] out object ppaip, out uint pdwThreadid);
    [PreserveSig] int RestoreLastFocus(out uint dwThreadId, [MarshalAs(UnmanagedType.Bool)] bool fPrev);
    [PreserveSig] int SetModalInput(IntPtr pSink, uint dwThreadId, uint dwFlags);
    [PreserveSig] int ShowFloating(uint dwFlags);
    [PreserveSig] int GetShowFloatingStatus(out uint pdwFlags);
}

// ─────────────────────────────────────────────────────────────────
// (3) 디버그 로그 (--debug 옵션일 때만 exe 옆 imebadge.log에 기록)
// ─────────────────────────────────────────────────────────────────
static class Log
{
    public static bool Enabled;
    static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "imebadge.log");
    static string _lastLine = "";

    public static void Write(string line)
    {
        if (!Enabled) return;
        try { File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}"); } catch { }
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
// (4) TSF 언어 표시줄에서 한/영 상태 읽기 (1순위)
// ─────────────────────────────────────────────────────────────────
static class TsfReader
{
    static readonly Guid CLSID_TF_LangBarMgr = new("ebb08c45-6c4a-4fdc-ae53-4eb8c4c7db8e");
    static readonly Guid GUID_LBI_INPUTMODE  = new("2c77a81e-41cc-4178-a3a7-5f8a987568e6");
    const uint TF_LBI_STYLE_BTN_TOGGLE   = 0x00040000;
    const uint TF_LBI_STATUS_BTN_TOGGLED = 0x00010000;

    static ITfLangBarMgr? _mgr;
    static ITfLangBarItemMgr? _itemMgr;
    static uint _itemMgrTid;

    /// <summary>대상 thread의 IME 모드 항목을 읽는다. 못 읽으면 null.</summary>
    public static ImeState? Read(uint tid, StringBuilder? dump)
    {
        try
        {
            _mgr ??= (ITfLangBarMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_TF_LangBarMgr)!)!;

            if (_itemMgr is null || _itemMgrTid != tid)
            {
                _itemMgr = null;
                int hr = _mgr.GetThreadLangBarItemMgr(tid, out var im, out _);
                if (hr != 0 || im is null) { dump?.Append($" tsf:GetThreadLangBarItemMgr=0x{hr:X8}"); return null; }
                _itemMgr = im;
                _itemMgrTid = tid;
            }

            if (_itemMgr.EnumItems(out var e) != 0 || e is null) { _itemMgr = null; return null; }

            ImeState? result = null;
            while (e.Next(1, out var item, out var fetched) == 0 && fetched == 1 && item is not null)
            {
                if (item.GetInfo(out var info) != 0) continue;
                item.GetStatus(out var status);
                string text = "", tip = "";
                if (item is ITfLangBarItemButton btn)
                {
                    btn.GetText(out text);
                    btn.GetTooltipString(out tip);
                }
                dump?.Append($"\n    item {info.guidItem} style=0x{info.dwStyle:X} status=0x{status:X} desc='{info.szDescription}' text='{text}' tip='{tip}'");

                if (result is null && info.guidItem == GUID_LBI_INPUTMODE)
                    result = Classify(info.dwStyle, status, text, tip);
            }
            return result;
        }
        catch (Exception ex)
        {
            dump?.Append(" tsf:EXC " + ex.Message);
            _itemMgr = null;
            _mgr = null;
            return null;
        }
    }

    static ImeState? Classify(uint style, uint status, string text, string tip)
    {
        // 1) 버튼 텍스트가 직접 말해 주는 경우
        var byText = FromWords(text);
        if (byText is not null) return byText;
        // 2) 토글 버튼이면 눌림 상태가 곧 한글 모드
        if ((style & TF_LBI_STYLE_BTN_TOGGLE) != 0)
            return (status & TF_LBI_STATUS_BTN_TOGGLED) != 0 ? ImeState.Hangul : ImeState.English;
        // 3) 툴팁에 단서가 있는 경우
        return FromWords(tip);
    }

    static ImeState? FromWords(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t == "A" || t.Contains("영어") || t.Contains("영문") || t.Contains("English", StringComparison.OrdinalIgnoreCase))
            return ImeState.English;
        if (t == "한" || t.Contains("한글") || t.Contains("Hangul", StringComparison.OrdinalIgnoreCase)
                      || t.Contains("Korean", StringComparison.OrdinalIgnoreCase))
            return ImeState.Hangul;
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────
// (4b) UI Automation으로 caret 위치 찾기 (2순위)
//      Chrome/Edge/Electron(Claude 앱 등)은 Win32 caret을 만들지 않으므로
//      접근성(accessibility) API로 "지금 선택 영역(=커서)"의 사각형을 묻는다.
// ─────────────────────────────────────────────────────────────────
static class UiaCaret
{
    public static Point? Find(StringBuilder? dump)
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el is null) return null;

            if (el.TryGetCurrentPattern(TextPattern.Pattern, out var pat) && pat is TextPattern tp)
            {
                var sel = tp.GetSelection();
                if (sel.Length > 0)
                {
                    var r = sel[0];
                    var rects = r.GetBoundingRectangles();
                    if (rects.Length == 0)
                    {
                        // 커서만 있는(선택 없음) 범위는 넓이가 0이라 사각형이 안 나온다.
                        // 커서 자리의 글자 한 칸으로 넓혀서 다시 묻는다.
                        r = r.Clone();
                        r.ExpandToEnclosingUnit(TextUnit.Character);
                        rects = r.GetBoundingRectangles();
                        if (rects.Length == 0)
                        {
                            // 문서 맨 끝이면 앞 글자 한 칸으로
                            r.Move(TextUnit.Character, -1);
                            r.ExpandToEnclosingUnit(TextUnit.Character);
                            rects = r.GetBoundingRectangles();
                        }
                    }
                    if (rects.Length > 0)
                    {
                        var last = rects[^1];
                        dump?.Append($" uia:text({last.Right:F0},{last.Bottom:F0})");
                        return new Point((int)last.Right, (int)last.Bottom);
                    }
                }
            }

            // TextPattern이 없으면 입력 컨트롤의 왼쪽 아래 모서리로 대신한다.
            var ct = el.Current.ControlType;
            if (ct == ControlType.Edit || ct == ControlType.Document || ct == ControlType.ComboBox)
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
// (5) 현재 상태 한 번 읽기 (caret 위치 + TSF → IMM32 순서로 한/영 판정)
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

        // caret 위치: 1순위 Win32 caret, 2순위 UI Automation
        Point? caret = null;
        if (gti.hwndCaret != IntPtr.Zero)
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

        ushort lang = (ushort)((long)Native.GetKeyboardLayout(tid) & 0xFFFF);
        if (lang != Native.LANG_KOREAN) return new(ImeState.OtherLang, caret);

        // 1순위: TSF 언어 표시줄 (Win11 메모장·브라우저 같은 TSF 앱에서도 실시간)
        var tsf = TsfReader.Read(tid, dump);

        // 2순위: IMM32 (구형 앱에서는 이쪽이 정확하다)
        var focus = gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : fg;
        var imeWnd = Native.ImmGetDefaultIMEWnd(focus);
        IntPtr open = IntPtr.Zero, mode = IntPtr.Zero;
        if (imeWnd != IntPtr.Zero)
        {
            Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, Native.IMC_GETOPENSTATUS,
                                      IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 50, out open);
            Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, Native.IMC_GETCONVERSIONMODE,
                                      IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 50, out mode);
        }
        ImeState imm = imeWnd == IntPtr.Zero ? ImeState.Unknown
                     : (open != IntPtr.Zero || ((long)mode & Native.IME_CMODE_NATIVE) != 0) ? ImeState.Hangul
                     : ImeState.English;

        var state = tsf ?? imm;

        if (dump is not null)
            Log.WriteIfChanged(
                $"fg='{Native.ClassName(fg)}' focus='{Native.ClassName(focus)}' tid={tid} " +
                $"tsf={(tsf?.ToString() ?? "null")} imm={imm}(open={open},mode=0x{(long)mode:X}) => {state}{dump}");

        return new(state, caret);
    }
}

// ─────────────────────────────────────────────────────────────────
// (6) caret 옆에 뜨는 배지 창
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

        _timer.Tick += (_, _) => Apply(ImeReader.Read(Handle));
        _timer.Start();
    }

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
