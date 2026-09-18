using System.Runtime.InteropServices;

namespace ImeBadge;

// ─────────────────────────────────────────────────────────────────
// (1) Win32 함수 선언 (P/Invoke)
//     윈도우가 제공하는 C 함수를 C#에서 부르기 위한 "명함"들이다.
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
}

// ─────────────────────────────────────────────────────────────────
// (2) 현재 상태 한 번 읽기
//     "지금 앞에 있는 창의 커서는 어디고, 한/영 스위치는 어느 쪽인가?"
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

        // caret(텍스트 커서)의 오른쪽 아래 모서리를 화면 좌표로 변환
        Point? caret = null;
        if (gti.hwndCaret != IntPtr.Zero)
        {
            var pt = new Native.POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Bottom };
            Native.ClientToScreen(gti.hwndCaret, ref pt);
            caret = new Point(pt.X, pt.Y);
        }

        // 키보드 레이아웃 언어. 한국어(0x0412)가 아니면 ENG 등 다른 레이아웃
        ushort lang = (ushort)((long)Native.GetKeyboardLayout(tid) & 0xFFFF);
        if (lang != Native.LANG_KOREAN) return new(ImeState.OtherLang, caret);

        // 한/영 스위치 상태: IME 창에 "열려 있니?"라고 묻는다 (0이 아니면 한글)
        var focus = gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : fg;
        var imeWnd = Native.ImmGetDefaultIMEWnd(focus);
        if (imeWnd == IntPtr.Zero) return new(ImeState.Unknown, caret);

        Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, Native.IMC_GETOPENSTATUS,
                                  IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 50, out var open);
        if (open != IntPtr.Zero) return new(ImeState.Hangul, caret);

        // 일부 앱은 OpenStatus 대신 변환 모드(conversion mode)의 NATIVE 비트로만 알려준다
        Native.SendMessageTimeout(imeWnd, Native.WM_IME_CONTROL, Native.IMC_GETCONVERSIONMODE,
                                  IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 50, out var mode);
        bool native = ((long)mode & Native.IME_CMODE_NATIVE) != 0;
        return new(native ? ImeState.Hangul : ImeState.English, caret);
    }
}

// ─────────────────────────────────────────────────────────────────
// (3) caret 옆에 뜨는 배지 창
//     포커스를 뺏지 않고, 클릭이 통과되며, 작업 표시줄에 보이지 않는다.
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
    bool _allowShow;   // 첫 판정 전에는 창을 절대 보이지 않는다 (시작 시 깜빡임 방지)

    public BadgeForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(28, 22);
        Opacity = 0.9;
        Controls.Add(_label);

        // 트레이 아이콘은 표시 용도가 아니라 "종료" 메뉴를 위한 최소 장치다.
        var menu = new ContextMenuStrip();
        menu.Items.Add("종료(&X)", null, (_, _) => Application.Exit());
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ImeBadge (한/영 배지)",
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

        // caret 오른쪽 아래 4px에 배치. 화면 밖으로 나가지 않게 보정.
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
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BadgeForm());
    }
}
