using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
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
    /// <summary>
    /// 테마 색 한 벌(디자인 토큰). 값은 Core 의 <see cref="DesignThemes"/> 에 있고(테스트 가능), 여기서는 Color 로 옮겨 쓴다.
    /// Window: 창 바탕 · Card: 카드 표면 · Input: 입력칸 · Elevated: 보조 버튼처럼 살짝 떠 있는 표면 · Border: 테두리 ·
    /// Track: 슬라이더 트랙·꺼진 토글 테두리 · Text/SubtleText: 글자 · Accent 계열: 강조색과 그 위의 글자색 · Radius: 카드 모서리.
    /// </summary>
    public sealed record Palette(
        bool Dark, Color Window, Color Card, Color Input, Color Elevated, Color Border, Color Track,
        Color Text, Color SubtleText, Color Link, Color Hover,
        Color Accent, Color AccentHover, Color AccentPressed, Color OnAccent, int Radius = 8)
    {
        public static Palette From(ThemePalette t, int radius) => new(t.Dark,
            C(t.Window), C(t.Card), C(t.Input), C(t.Elevated), C(t.Border), C(t.Track),
            C(t.Text), C(t.SubtleText), C(t.Link), C(t.Hover),
            C(t.Accent), C(t.AccentHover), C(t.AccentPressed), C(t.OnAccent), radius);

        static Color C(int argb) => Color.FromArgb(argb);

        /// <summary>클래식 테마의 밝게/어둡게. Windows 11 설정 앱의 색과 같다.</summary>
        public static readonly Palette Light = From(DesignThemes.Classic.Light, DesignThemes.Classic.CornerRadius);
        public static readonly Palette DarkTheme = From(DesignThemes.Classic.Dark, DesignThemes.Classic.CornerRadius);
    }

    /// <summary>
    /// 지금 쓰는 디자인 테마. 트레이 프로그램(BadgeForm)이 설정을 반영할 때, 설정 창이 편집 중인 테마를 바꿀 때 정한다.
    /// 창·메뉴는 만들 때 이 값으로 칠하므로, 바꾼 뒤에는 열린 창에 <see cref="Apply"/> 를 다시 불러야 한다.
    /// </summary>
    public static DesignTheme Design { get; set; } = DesignThemes.Classic;

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

    public static Palette Current => Palette.From(IsDark ? Design.Dark : Design.Light, Design.CornerRadius);

    /// <summary>대화상자 글꼴. 시스템 설정(한국어 Windows 는 맑은 고딕 9pt, 영어는 Segoe UI 9pt)을 따른다.</summary>
    public static Font DialogFont => SystemFonts.MessageBoxFont ?? new Font(BadgeRenderer.FontFamily, 9f);

    // ── 타이포 위계: 창 제목 > 카드 제목 > 본문 > 힌트 ──
    public static Font TitleFont(Font body) => new(body.FontFamily, body.Size * 1.55f, FontStyle.Bold);
    public static Font CardTitleFont(Font body) => new(body.FontFamily, body.Size * 1.1f, FontStyle.Bold);
    public static Font HintFont(Font body) => new(body.FontFamily, body.Size * 0.92f);

    /// <summary>창과 그 안의 모든 컨트롤에 테마 색을 입힌다. 고대비 모드면 시스템 색을 그대로 둔다.</summary>
    public static void Apply(Form form)
    {
        if (SystemInformation.HighContrast) return;
        var p = Current;
        form.BackColor = p.Window;
        form.ForeColor = p.Text;
        foreach (Control c in form.Controls) Walk(c, p, p.Window);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Label, Label> SubtleLabels = new();

    static void Walk(Control c, Palette p, Color bg)
    {
        switch (c)
        {
            case CardGroupBox card:
                card.Palette = p;
                card.BackColor = p.Card; card.ForeColor = p.Text;
                bg = p.Card;
                break;
            case ThemedControl themed:
                themed.Palette = p;
                themed.BackColor = bg;
                break;
            case LinkLabel link:
                link.BackColor = bg;
                link.LinkColor = p.Link; link.ActiveLinkColor = p.Accent; link.VisitedLinkColor = p.Link;
                break;
            case Label label:
                label.BackColor = bg;
                // 보조 글자(힌트·16진수)는 만들 때 GrayText 로 표시해 둔다. 테마를 바꿔 다시 칠할 때도 알아보도록 기억한다.
                if (label.ForeColor == SystemColors.GrayText) SubtleLabels.AddOrUpdate(label, label);
                label.ForeColor = SubtleLabels.TryGetValue(label, out _) ? p.SubtleText : p.Text;
                break;
            case CheckBox check:
                check.BackColor = bg; check.ForeColor = p.Text;
                if (p.Dark) check.FlatStyle = FlatStyle.Flat;   // 기본(테마) 그리기는 흰 상자를 그린다
                break;
            case Button button when button.Tag is not "color":   // 색 견본 버튼은 자기 색을 쓴다
                if (p.Dark)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.BackColor = p.Elevated; button.ForeColor = p.Text;
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

    /// <summary>
    /// 제목 표시줄을 테마에 맞춘다. 어두운 모드 요청에 더해 Windows 11 에서는 제목 표시줄 색을 창 바탕과 같게 칠해
    /// 제목 표시줄과 본문의 이음새가 사라진다(설정 앱과 같은 모양). 창 핸들이 만들어진 뒤(OnHandleCreated)에 불러야 한다.
    /// </summary>
    public static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        int dark = IsDark ? 1 : 0;
        try
        {
            if (Native.DwmSetWindowAttribute(form.Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(form.Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
            if (SystemInformation.HighContrast) return;
            var p = Current;
            int caption = Native.ColorRef(p.Window), text = Native.ColorRef(p.Text), border = Native.ColorRef(p.Border);
            // Windows 10 은 이 속성을 모른다(오류 반환). 조용히 무시한다.
            Native.DwmSetWindowAttribute(form.Handle, Native.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            Native.DwmSetWindowAttribute(form.Handle, Native.DWMWA_TEXT_COLOR, ref text, sizeof(int));
            Native.DwmSetWindowAttribute(form.Handle, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch (Exception ex) { Log.Error("title bar theme failed", ex); }
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
/// Windows 11 "카드" 모양의 그룹 상자. 제목(굵게)과 한 줄 설명(보조색)을 위에 쓰고 그 아래에 둥근 테두리 상자를 그린다.
/// 기본 GroupBox 는 테마 엔진이 밝은 회색 선을 그려 어두운 배경에서 어색하고, 제목이 선 위에 걸쳐 있는 옛 모양이다.
/// 자식 컨트롤은 <see cref="ContentOrigin"/> 에서 시작하도록 놓는다.
/// </summary>
sealed class CardGroupBox : GroupBox
{
    public Theme.Palette Palette { get; set; } = Theme.Palette.Light;
    public string Description { get; set; } = "";

    const int Inset = 16;

    /// <summary>제목(과 설명)이 차지하는 높이. 테두리 상자는 이 아래에서 시작한다.</summary>
    int HeaderHeight => Description.Length > 0 ? 44 : 26;

    /// <summary>자식 컨트롤이 시작해야 하는 위치(상자 안쪽 여백 포함).</summary>
    public Point ContentOrigin => new(Inset, HeaderHeight + Inset);

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
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var titleFont = Theme.CardTitleFont(Font);
        TextRenderer.DrawText(g, Text, titleFont, new Rectangle(2, 0, Width - 4, 24), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Description.Length > 0)
        {
            using var hintFont = Theme.HintFont(Font);
            TextRenderer.DrawText(g, Description, hintFont, new Rectangle(2, 22, Width - 4, 20), p.SubtleText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        var box = new Rectangle(0, HeaderHeight, Width - 1, Height - HeaderHeight - 1);
        if (box.Width <= 0 || box.Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(box, p.Radius);
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

    /// <summary>하위 메뉴 안 묶음 제목(누를 수 없는 라벨)의 Tag. 보조 글자색으로 그린다.</summary>
    public const string HeaderTag = "menu-header";

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled && e.Item.Tag is not HeaderTag ? _p.Text : _p.SubtleText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? _p.SubtleText : _p.Text;
        base.OnRenderArrow(e);
    }

    /// <summary>선택 배경을 둥근 사각형으로(Windows 11 메뉴).</summary>
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var r = new Rectangle(2, 0, e.Item.Width - 5, e.Item.Height - 1);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(r, 4);
        using var brush = new SolidBrush(_p.Hover);
        g.FillPath(brush, path);
    }

    /// <summary>
    /// 기본 렌더러는 검은 체크 그림을 쓰므로 어두운 배경에서는 안 보인다. 글자색으로 직접 그린다.
    /// 아이콘이 있는 항목(일시 중지)은 체크 표시 대신 아이콘 뒤에 강조색 상자를 깔아 "켜짐"을 나타낸다.
    /// </summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (e.Item.Image is not null)
        {
            var box = r; box.Inflate(3, 3);
            using var boxPath = RoundedRect(box, 4);
            using var boxBrush = new SolidBrush(Color.FromArgb(_p.Dark ? 70 : 45, _p.Accent));
            g.FillPath(boxBrush, boxPath);
            return;
        }
        using var pen = new Pen(e.Item.Enabled ? _p.Text : _p.SubtleText, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = r.Left + r.Width * 0.22f, y = r.Top + r.Height * 0.52f;
        g.DrawLines(pen, new[]
        {
            new PointF(x, y),
            new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
            new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
        });
    }

    static GraphicsPath RoundedRect(Rectangle r, int radius)
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

/// <summary>
/// 메뉴 아이콘: Segoe Fluent Icons(Windows 11) 또는 Segoe MDL2 Assets(Windows 10) 글리프를 테마 글자색으로 그린 작은 비트맵.
/// 두 글꼴은 같은 코드 포인트를 쓴다. 둘 다 없으면(드묾) 아이콘 없이 메뉴만 보인다.
/// </summary>
static class MenuIcons
{
    /// <summary>테마(팔레트) 아이콘. 두 글꼴 모두 같은 코드 포인트(E790, Color).</summary>
    public const string Theme = "\uE790";
    public const string Pause = "", Play = "", Settings = "", Shape = "", Place = "", Size = "",
        Opacity = "", Autostart = "", Update = "", Info = "", Exit = "";

    static readonly string? FontName = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");

    static string? Pick(params string[] names)
    {
        foreach (var n in names)
        {
            try { using var f = new Font(n, 10f); if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return n; }
            catch { }
        }
        return null;
    }

    public static Bitmap? Glyph(string glyph, Color color, Size size)
    {
        if (FontName is null) return null;
        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        using var font = new Font(FontName, size.Height * 0.72f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, size.Width, size.Height), sf);
        return bmp;
    }
}
