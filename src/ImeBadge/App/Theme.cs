using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ImeBadge;

/// <summary>
/// 밝은/어두운 테마 색과 적용. .NET 8 의 WinForms 는 Windows 다크 모드를 스스로 따르지 않으므로,
/// 시스템 설정을 읽어(앱 모드) 창·컨트롤 색을 직접 입히고 제목 표시줄은 DWM 에 요청한다.
/// 비유하면 집(창)은 기본 흰 벽지로 지어지는데, 방 주인이 어두운 테마를 골랐으면 우리가 벽지를 갈아 주는 것이다.
/// 고대비 모드에서는 아무것도 손대지 않는다(시스템 색이 곧 접근성 설정이다).
/// </summary>
static class Theme
{
    /// <summary>테마 색 한 벌. Windows 11 설정 앱의 색과 비슷하게 맞췄다.</summary>
    public sealed record Palette(
        bool Dark, Color Window, Color Card, Color Input, Color Border, Color Text, Color SubtleText,
        Color Link, Color Hover, Color Accent)
    {
        public static readonly Palette Light = new(false,
            Window: Color.FromArgb(0xF3, 0xF3, 0xF3), Card: Color.White, Input: Color.White,
            Border: Color.FromArgb(0xE5, 0xE5, 0xE5), Text: Color.FromArgb(0x1B, 0x1B, 0x1B), SubtleText: Color.FromArgb(0x61, 0x61, 0x61),
            Link: Color.FromArgb(0x00, 0x5F, 0xB8), Hover: Color.FromArgb(0xEA, 0xEA, 0xEA), Accent: Color.FromArgb(0x00, 0x67, 0xC0));

        public static readonly Palette DarkTheme = new(true,
            Window: Color.FromArgb(0x20, 0x20, 0x20), Card: Color.FromArgb(0x2B, 0x2B, 0x2B), Input: Color.FromArgb(0x1F, 0x1F, 0x1F),
            Border: Color.FromArgb(0x3F, 0x3F, 0x3F), Text: Color.White, SubtleText: Color.FromArgb(0xB0, 0xB0, 0xB0),
            Link: Color.FromArgb(0x4C, 0xC2, 0xFF), Hover: Color.FromArgb(0x38, 0x38, 0x38), Accent: Color.FromArgb(0x4C, 0xC2, 0xFF));
    }

    /// <summary>Windows 설정 → 개인 설정 → 색 → "앱 모드" 가 어둡게인가. 고대비 모드면 false.</summary>
    public static bool IsDark
    {
        get
        {
            if (SystemInformation.HighContrast) return false;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }
    }

    public static Palette Current => IsDark ? Palette.DarkTheme : Palette.Light;

    /// <summary>대화상자 글꼴. 시스템 설정(한국어 Windows 는 맑은 고딕 9pt, 영어는 Segoe UI 9pt)을 따른다.</summary>
    public static Font DialogFont => SystemFonts.MessageBoxFont ?? new Font(BadgeRenderer.FontFamily, 9f);

    /// <summary>창과 그 안의 모든 컨트롤에 테마 색을 입힌다. 고대비 모드면 시스템 색을 그대로 둔다.</summary>
    public static void Apply(Form form)
    {
        if (SystemInformation.HighContrast) return;
        var p = Current;
        form.BackColor = p.Window;
        form.ForeColor = p.Text;
        foreach (Control c in form.Controls) Walk(c, p, p.Window);
    }

    static void Walk(Control c, Palette p, Color bg)
    {
        switch (c)
        {
            case CardGroupBox card:
                card.Palette = p;
                card.BackColor = p.Card; card.ForeColor = p.Text;
                bg = p.Card;
                break;
            case LinkLabel link:
                link.BackColor = bg;
                link.LinkColor = p.Link; link.ActiveLinkColor = p.Accent; link.VisitedLinkColor = p.Link;
                break;
            case Label label:
                label.BackColor = bg;
                label.ForeColor = label.ForeColor == SystemColors.GrayText ? p.SubtleText : p.Text;
                break;
            case CheckBox check:
                check.BackColor = bg; check.ForeColor = p.Text;
                if (p.Dark) check.FlatStyle = FlatStyle.Flat;   // 기본(테마) 그리기는 흰 상자를 그린다
                break;
            case Button button when button.Tag is not "color":   // 색 견본 버튼은 자기 색을 쓴다
                if (p.Dark)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.BackColor = p.Card == bg ? p.Hover : p.Card; button.ForeColor = p.Text;
                    button.FlatAppearance.BorderColor = p.Border;
                    button.FlatAppearance.MouseOverBackColor = p.Hover;
                }
                break;
            case ComboBox combo:
                if (p.Dark) { combo.FlatStyle = FlatStyle.Flat; combo.BackColor = p.Input; combo.ForeColor = p.Text; }
                break;
            case TextBox text:
                if (p.Dark) { text.BackColor = p.Input; text.ForeColor = p.Text; text.BorderStyle = BorderStyle.FixedSingle; }
                break;
            case NumericUpDown num:
                if (p.Dark) { num.BackColor = p.Input; num.ForeColor = p.Text; num.BorderStyle = BorderStyle.FixedSingle; }
                break;
            case ListBox list:
                if (p.Dark) { list.BackColor = p.Input; list.ForeColor = p.Text; list.BorderStyle = BorderStyle.FixedSingle; }
                break;
            case TrackBar or PictureBox:
                c.BackColor = bg;
                break;
            case Panel:   // TableLayoutPanel, FlowLayoutPanel 포함
                if (c.Tag is not "custom-paint") c.BackColor = bg;
                break;
        }
        foreach (Control child in c.Controls) Walk(child, p, bg);
    }

    /// <summary>제목 표시줄을 테마에 맞춘다. 창 핸들이 만들어진 뒤(OnHandleCreated)에 불러야 한다.</summary>
    public static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        int dark = IsDark ? 1 : 0;
        try
        {
            if (Native.DwmSetWindowAttribute(form.Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(form.Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
        }
        catch (Exception ex) { Log.Error("dark title bar failed", ex); }
    }

    /// <summary>Windows 11 에서 팝업(메뉴)의 모서리를 둥글게. 다른 버전에서는 조용히 무시된다.</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        int pref = Native.DWMWCP_ROUNDSMALL;
        try { Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
        catch { }
    }

    /// <summary>트레이 메뉴 렌더러. 그라데이션 없는 평면(Windows 11 풍)이며 테마 색을 따른다.</summary>
    public static ToolStripRenderer CreateMenuRenderer() =>
        SystemInformation.HighContrast ? new ToolStripSystemRenderer() : new FlatMenuRenderer(Current);
}

/// <summary>
/// Windows 11 "카드" 모양의 그룹 상자. 제목을 위에 쓰고 그 아래에 둥근 테두리 상자를 그린다.
/// 기본 GroupBox 는 테마 엔진이 밝은 회색 선을 그려 어두운 배경에서 어색하고, 제목이 선 위에 걸쳐 있는 옛 모양이다.
/// 자식 컨트롤 배치(ContentOrigin = (12, 26))는 그대로라 SettingsForm 의 레이아웃 계산이 바뀌지 않는다.
/// </summary>
sealed class CardGroupBox : GroupBox
{
    public Theme.Palette Palette { get; set; } = Theme.Palette.Light;

    /// <summary>제목이 차지하는 높이. 테두리 상자는 이 아래에서 시작한다.</summary>
    const int TitleHeight = 20;
    const int Radius = 6;

    public CardGroupBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Palette;
        var outside = Parent?.BackColor ?? p.Window;
        g.Clear(outside);

        TextRenderer.DrawText(g, Text, Font, new Rectangle(4, 0, Width - 8, TitleHeight), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var box = new Rectangle(0, TitleHeight, Width - 1, Height - TitleHeight - 1);
        if (box.Width <= 0 || box.Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(box, Radius);
        using var fill = new SolidBrush(BackColor);
        // 고대비 모드에서는 테마 색을 입히지 않으므로 테두리도 시스템 색으로.
        using var pen = new Pen(SystemInformation.HighContrast ? SystemColors.ControlDark : p.Border);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>평면 메뉴 렌더러. 색 표(<see cref="FlatColorTable"/>)로 배경·선택·구분선을 정하고, 글자·화살표·체크 표시 색을 테마에 맞춘다.</summary>
sealed class FlatMenuRenderer : ToolStripProfessionalRenderer
{
    readonly Theme.Palette _p;

    public FlatMenuRenderer(Theme.Palette p) : base(new FlatColorTable(p))
    {
        _p = p;
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _p.Text : _p.SubtleText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? _p.SubtleText : _p.Text;
        base.OnRenderArrow(e);
    }

    /// <summary>기본 렌더러는 검은 체크 그림을 쓰므로 어두운 배경에서는 안 보인다. 글자색으로 직접 그린다.</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(e.Item.Enabled ? _p.Text : _p.SubtleText, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = r.Left + r.Width * 0.22f, y = r.Top + r.Height * 0.52f;
        g.DrawLines(pen, new[]
        {
            new PointF(x, y),
            new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
            new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
        });
    }
}

/// <summary>그라데이션 없는 색 표. ProfessionalColorTable 의 같은 이름 색을 모두 평면 색으로 덮는다.</summary>
sealed class FlatColorTable : ProfessionalColorTable
{
    readonly Theme.Palette _p;
    public FlatColorTable(Theme.Palette p) { _p = p; UseSystemColors = false; }

    public override Color ToolStripDropDownBackground => _p.Card;
    public override Color MenuBorder => _p.Border;
    public override Color MenuItemBorder => _p.Hover;
    public override Color MenuItemSelected => _p.Hover;
    public override Color MenuItemSelectedGradientBegin => _p.Hover;
    public override Color MenuItemSelectedGradientEnd => _p.Hover;
    public override Color MenuItemPressedGradientBegin => _p.Hover;
    public override Color MenuItemPressedGradientMiddle => _p.Hover;
    public override Color MenuItemPressedGradientEnd => _p.Hover;
    public override Color ImageMarginGradientBegin => _p.Card;
    public override Color ImageMarginGradientMiddle => _p.Card;
    public override Color ImageMarginGradientEnd => _p.Card;
    public override Color SeparatorDark => _p.Border;
    public override Color SeparatorLight => _p.Card;
    public override Color CheckBackground => _p.Card;
    public override Color CheckSelectedBackground => _p.Hover;
    public override Color CheckPressedBackground => _p.Hover;
    public override Color ToolStripBorder => _p.Border;
}
