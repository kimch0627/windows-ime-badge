using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>말풍선의 종류. 색과 기본 아이콘이 다르다(Windows 11 InfoBar 의 정보·주의 색).</summary>
enum CalloutKind { Info, Warning }

/// <summary>
/// 커서 옆 말풍선의 내용: 굵은 제목 한 줄과 설명 한 줄(없어도 됨). 왼쪽 아이콘은 <paramref name="Glyph"/>(아이콘 글꼴 글자)이고,
/// <paramref name="Badge"/> 가 있으면 아이콘 대신 그 상태의 배지를 그린다(입력칸별 한/영 기억: "이 칸은 보통 [a]").
/// </summary>
sealed record CalloutContent(CalloutKind Kind, string Title, string? Detail = null, string? Glyph = null, ImeState? Badge = null);

/// <summary>
/// 실험 기능의 그림: 커서 소나(<see cref="Sonar"/>)의 한 장면과 커서 옆 말풍선. 배지처럼 미리 곱한 알파(PArgb) 비트맵이라
/// 레이어드 창(<see cref="OverlayWindow"/>)에 바로 올린다. 확인용 그림은 <see cref="RenderSheet"/> 의 10번 절.
/// </summary>
static class OverlayRenderer
{
    // ── 소나 ──
    /// <summary>원의 굵기와 원 둘레의 흰 테두리 굵기(배율 1 기준 px). 흰 테두리가 어두운 화면에서, 색 원이 밝은 화면에서 보인다.</summary>
    const float RingWidth = 3f, RingHalo = 1.5f;

    /// <summary>
    /// 소나 한 장면. <paramref name="caret"/> 은 화면 좌표이고, 그림을 놓을 화면 좌표(<paramref name="origin"/>)를 함께 돌려준다.
    /// 원은 커서 막대의 가운데로 좁혀 들고, 끝에는 커서 막대 둘레의 둥근 빛이 잠깐 켜진다.
    /// </summary>
    public static Bitmap Sonar(float t, Rectangle caret, float scale, Color color, Color halo, out Point origin)
    {
        float start = ImeBadge.Sonar.StartRadius * scale;
        float height = Math.Max(caret.Height, 14 * scale);
        float end = Math.Max(height * 0.75f, 10 * scale);
        float stroke = RingWidth * scale, haloWidth = stroke + 2 * RingHalo * scale;
        int half = (int)Math.Ceiling(start + haloWidth);
        float cx = caret.Left + caret.Width / 2f, cy = caret.Top + caret.Height / 2f;
        origin = new Point((int)Math.Floor(cx) - half, (int)Math.Floor(cy) - half);

        var bmp = new Bitmap(2 * half + 1, 2 * half + 1, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        float x = cx - origin.X, y = cy - origin.Y;

        for (int i = 0; i < ImeBadge.Sonar.Rings; i++)
        {
            var (r, a) = ImeBadge.Sonar.Ring(i, t, start, end);
            if (a <= 0f) continue;
            using var under = new Pen(Color.FromArgb((int)(220 * a), halo), haloWidth);
            using var over = new Pen(Color.FromArgb((int)(255 * a), color), stroke);
            g.DrawEllipse(under, x - r, y - r, 2 * r, 2 * r);
            g.DrawEllipse(over, x - r, y - r, 2 * r, 2 * r);
        }

        float glow = ImeBadge.Sonar.Glow(t);
        if (glow > 0f)
        {
            // 커서 막대를 감싸는 세로 캡슐. 가운데는 비쳐서 커서와 그 옆 글자가 그대로 보인다.
            float w = 8 * scale, h = height + 8 * scale;
            var rect = new RectangleF(x - w / 2, y - h / 2, w, h);
            using var path = Capsule(rect);
            using var fill = new SolidBrush(Color.FromArgb((int)(110 * glow), color));
            using var edge = new Pen(Color.FromArgb((int)(230 * glow), halo), 1.5f * scale);
            using var ring = new Pen(Color.FromArgb((int)(255 * glow), color), 1.5f * scale);
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
            using var outer = (GraphicsPath)path.Clone();
            using (var m = new Matrix())
            {
                float k = 1.5f * scale;
                m.Translate(x, y);
                m.Scale((w + 2 * k) / w, (h + 2 * k) / h);
                m.Translate(-x, -y);
                outer.Transform(m);
            }
            g.DrawPath(ring, outer);
        }
        return bmp;
    }

    static GraphicsPath Capsule(RectangleF r)
    {
        var p = new GraphicsPath();
        float d = Math.Min(r.Width, r.Height);
        p.AddArc(r.Left, r.Top, d, d, 180, 180);
        p.AddArc(r.Left, r.Bottom - d, d, d, 0, 180);
        p.CloseFigure();
        return p;
    }

    // ── 말풍선 ──
    // 치수는 배율 1 기준 px. Windows 11 InfoBar·툴팁과 비슷한 크기다.
    const float PadX = 12, PadY = 9, IconBox = 16, IconGap = 9, LineGap = 2, MaxText = 340, Radius = 7, TailW = 14, TailH = 7, Shadow = 5;
    const float TitleSize = 12.5f, DetailSize = 12f;

    /// <summary>꼬리 높이(px).</summary>
    public static int TailHeight(float scale) => (int)Math.Ceiling(TailH * scale);

    /// <summary>몸통 왼쪽 끝에서 꼬리 끝까지의 최소 거리(px). 둥근 모서리에 꼬리가 걸리지 않게.</summary>
    public static int TailInset(float scale) => (int)Math.Ceiling((Radius + TailW / 2 + 4) * scale);

    /// <summary>제목 배치: 줄바꿈 없이, 넘치면 말줄임표.</summary>
    static StringFormat TextFormat()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        f.Trimming = StringTrimming.EllipsisCharacter;
        return f;
    }

    /// <summary>설명 배치: 낱말 단위로 줄바꿈해 <see cref="DetailLines"/> 줄까지, 넘치면 마지막 줄 끝에 말줄임표.</summary>
    static StringFormat DetailFormat()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.LineLimit;
        f.Trimming = StringTrimming.EllipsisWord;
        return f;
    }

    const int DetailLines = 2;

    sealed class CalloutFonts : IDisposable
    {
        public readonly Font Title, Detail;
        public readonly Font? Icon;
        public readonly StringFormat Format = TextFormat();
        public readonly StringFormat Wrap = DetailFormat();

        public CalloutFonts(float scale)
        {
            // 맑은 고딕: 한글과 라틴 글자가 모두 있다(영어 UI 에서도 앱 이름이 한글일 수 있다).
            Title = new Font(BadgeRenderer.FontFamily, TitleSize * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            Detail = new Font(BadgeRenderer.FontFamily, DetailSize * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            Icon = Theme.IconFont((int)Math.Round(IconBox * scale));
        }

        public void Dispose() { Title.Dispose(); Detail.Dispose(); Icon?.Dispose(); Format.Dispose(); Wrap.Dispose(); }
    }

    /// <summary>말풍선 색: 바탕, 테두리, 제목, 설명, 아이콘.</summary>
    readonly record struct CalloutColors(Color Fill, Color Border, Color Title, Color Detail, Color Icon);

    /// <summary>
    /// 정보는 테마의 카드색과 강조색, 주의는 Windows 11 InfoBar 의 주의(Caution) 색. 고대비 모드면 시스템 색 그대로.
    /// </summary>
    static CalloutColors ColorsFor(CalloutKind kind, bool dark)
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Window, SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText, SystemColors.Highlight);
        var p = Theme.Palette.From(dark ? Theme.Design.Dark : Theme.Design.Light, Theme.Design.CornerRadius);
        Color fill, title, detail, icon;
        if (kind == CalloutKind.Warning)
        {
            fill = dark ? Hex(0x433519) : Hex(0xFFF4CE);
            title = dark ? Color.White : Hex(0x1A1A1A);
            detail = dark ? Hex(0xE3DCC6) : Hex(0x4A4A4A);
            icon = dark ? Hex(0xFCE100) : Hex(0x9D5D00);
        }
        else
        {
            fill = p.Card; title = p.Text; detail = p.SubtleText; icon = p.Accent;
        }
        // 테두리: 어두운 바탕에서는 조금 밝게, 밝은 바탕에서는 조금 어둡게(문서 위에 떠 있는 것이 보이게).
        var border = Color.FromArgb(ColorHex.Mix(fill.ToArgb() | unchecked((int)0xFF000000), dark ? unchecked((int)0xFFFFFFFF) : unchecked((int)0xFF000000), dark ? 0.16 : 0.14));
        return new(fill, border, title, detail, icon);
    }

    static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    static string DefaultGlyph(CalloutKind kind) => kind == CalloutKind.Warning ? "\uE7BA" : "\uE946";   // Warning / Info

    /// <summary>아이콘 자리의 크기. 배지를 그리면 그 배지 그림의 크기, 아니면 아이콘 글꼴 한 칸.</summary>
    static SizeF IconSize(CalloutContent c, float scale, in BadgeTheme theme, CalloutFonts fonts)
    {
        if (c.Badge is ImeState st)
        {
            using var b = BadgeRenderer.Render(st, BadgeStyle.Pill, BadgeScale(scale), theme);
            return new SizeF(b.Width, b.Height);
        }
        return fonts.Icon is null ? SizeF.Empty : new SizeF(IconBox * scale, IconBox * scale);
    }

    static float BadgeScale(float scale) => 0.9f * scale;

    static (SizeF Title, SizeF Detail) TextSizes(Graphics g, CalloutContent c, CalloutFonts f, float scale)
    {
        var max = new SizeF(MaxText * scale, 1000);
        var title = g.MeasureString(c.Title, f.Title, max, f.Format);
        var detailMax = new SizeF(Math.Max(MaxText * scale, title.Width), f.Detail.GetHeight(g) * DetailLines + 1);
        var detail = c.Detail is { Length: > 0 } d ? g.MeasureString(d, f.Detail, detailMax, f.Wrap) : SizeF.Empty;
        return (title, detail);
    }

    /// <summary>몸통(꼬리·그림자 제외) 크기. 위치(<see cref="CalloutLayout"/>)를 정한 뒤 <see cref="Callout"/> 으로 그린다.</summary>
    public static Size MeasureCallout(CalloutContent c, float scale, in BadgeTheme theme)
    {
        using var fonts = new CalloutFonts(scale);
        using var probe = new Bitmap(1, 1);
        using var g = Graphics.FromImage(probe);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var (title, detail) = TextSizes(g, c, fonts, scale);
        var icon = IconSize(c, scale, theme, fonts);
        float textW = Math.Max(title.Width, detail.Width);
        float textH = title.Height + (detail.IsEmpty ? 0 : LineGap * scale + detail.Height);
        float w = PadX * scale + (icon.IsEmpty ? 0 : icon.Width + IconGap * scale) + textW + PadX * scale;
        float h = 2 * PadY * scale + Math.Max(textH, icon.Height);
        return new Size((int)Math.Ceiling(w), (int)Math.Ceiling(h));
    }

    /// <summary>
    /// 말풍선 그림. <paramref name="body"/> 는 <see cref="MeasureCallout"/> 의 크기, <paramref name="below"/>·<paramref name="tailX"/> 는
    /// <see cref="CalloutLayout"/> 의 결과다. 그림 안에서 몸통의 왼쪽 위(<paramref name="bodyOffset"/>)를 함께 돌려준다:
    /// 창 위치 = 몸통 위치 − bodyOffset. <paramref name="dark"/> 를 주지 않으면 Windows 앱 모드(밝게/어둡게)를 따른다.
    /// </summary>
    public static Bitmap Callout(CalloutContent c, float scale, Size body, bool below, int tailX, in BadgeTheme theme, out Point bodyOffset, bool? dark = null)
    {
        using var fonts = new CalloutFonts(scale);
        int pad = (int)Math.Ceiling(Shadow * scale), tail = TailHeight(scale);
        bodyOffset = new Point(pad, pad + (below ? tail : 0));
        var bmp = new Bitmap(body.Width + 2 * pad, body.Height + tail + 2 * pad, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var colors = ColorsFor(c.Kind, dark ?? Theme.IsDark);
        var rect = new RectangleF(bodyOffset.X + 0.5f, bodyOffset.Y + 0.5f, body.Width - 1, body.Height - 1);
        using var path = BubblePath(rect, Radius * scale, rect.X + tailX, TailW * scale, tail, tailOnTop: below);

        if (!SystemInformation.HighContrast)
        {
            // 옅은 그림자: GDI+ 에는 흐림이 없어 굵고 옅은 선 + 조금 진한 채움 두 겹(배지의 그림자와 같은 방식).
            using var shadow = (GraphicsPath)path.Clone();
            using (var m = new Matrix()) { m.Translate(0, 1.5f * scale); shadow.Transform(m); }
            using var soft = new Pen(Color.FromArgb(28, Color.Black), 4 * scale) { LineJoin = LineJoin.Round };
            using var core = new SolidBrush(Color.FromArgb(40, Color.Black));
            g.DrawPath(soft, shadow);
            g.FillPath(core, shadow);
        }
        using (var fill = new SolidBrush(colors.Fill)) g.FillPath(fill, path);
        using (var border = new Pen(colors.Border, Math.Max(1f, scale)) { LineJoin = LineJoin.Round }) g.DrawPath(border, path);

        var (title, detail) = TextSizes(g, c, fonts, scale);
        var icon = IconSize(c, scale, theme, fonts);
        float x = rect.X + PadX * scale, cy = rect.Y + rect.Height / 2;
        if (c.Badge is ImeState st)
        {
            using var b = BadgeRenderer.Render(st, BadgeStyle.Pill, BadgeScale(scale), theme);
            g.DrawImage(b, new Rectangle((int)Math.Round(x), (int)Math.Round(cy - b.Height / 2f), b.Width, b.Height));
        }
        else if (fonts.Icon is not null)
        {
            using var brush = new SolidBrush(colors.Icon);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(c.Glyph ?? DefaultGlyph(c.Kind), fonts.Icon, brush, new RectangleF(x, cy - icon.Height / 2, icon.Width, icon.Height), center);
        }
        if (!icon.IsEmpty) x += icon.Width + IconGap * scale;

        float textH = title.Height + (detail.IsEmpty ? 0 : LineGap * scale + detail.Height);
        float y = cy - textH / 2;
        float maxW = rect.Right - PadX * scale - x;
        using (var brush = new SolidBrush(colors.Title))
            g.DrawString(c.Title, fonts.Title, brush, new RectangleF(x, y, maxW + 1, title.Height + 1), fonts.Format);
        if (!detail.IsEmpty)
        {
            using var brush = new SolidBrush(colors.Detail);
            g.DrawString(c.Detail!, fonts.Detail, brush, new RectangleF(x, y + title.Height + LineGap * scale, maxW + 1, detail.Height + 1), fonts.Wrap);
        }
        return bmp;
    }

    /// <summary>둥근 사각형 몸통에 커서를 가리키는 꼬리(삼각형)를 붙인 윤곽 하나. 꼬리는 위(말풍선이 커서 아래) 또는 아래.</summary>
    static GraphicsPath BubblePath(RectangleF r, float radius, float tipX, float tailW, float tailH, bool tailOnTop)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)), half = tailW / 2;
        tipX = Math.Clamp(tipX, r.Left + d / 2 + half, Math.Max(r.Left + d / 2 + half, r.Right - d / 2 - half));
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        if (tailOnTop)
        {
            p.AddLine(tipX - half, r.Top, tipX, r.Top - tailH);
            p.AddLine(tipX, r.Top - tailH, tipX + half, r.Top);
        }
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        if (!tailOnTop)
        {
            p.AddLine(tipX + half, r.Bottom, tipX, r.Bottom + tailH);
            p.AddLine(tipX, r.Bottom + tailH, tipX - half, r.Bottom);
        }
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
