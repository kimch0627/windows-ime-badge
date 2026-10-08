using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>말풍선의 종류. 색과 기본 아이콘이 다르다(Windows 11 InfoBar 의 정보·주의 색).</summary>
enum CalloutKind { Info, Warning }

/// <summary>
/// 커서 옆 말풍선의 내용: 굵은 제목 한 줄, 제목 옆의 알약(<paramref name="Tags"/>: 지금 무엇이 문제인지 한눈에, 예: "한글 입력" "Caps Lock"),
/// 설명(두 줄까지, 없어도 됨). 왼쪽 아이콘은 <paramref name="Glyph"/>(아이콘 글꼴 글자), 없으면 종류의 기본 아이콘(정보 ⓘ, 주의 ⚠).
/// </summary>
sealed record CalloutContent(CalloutKind Kind, string Title, string? Detail = null, string? Glyph = null, string[]? Tags = null);

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
    // 불투명한 카드(밝게 = 흰색, 어둡게 = 짙은 회색)를 그림자로 띄우고, 색은 왼쪽 띠·아이콘 원·알약에만 쓴다. 예전의 옅은 노란 바탕은
    // 흰 문서·웹 페이지 위에서 묻혀 잘 보이지 않았다. 치수는 배율 1 기준 px 이고, 글자는 Windows 11 본문 크기(14px)에 맞췄다.
    const float PadX = 14, PadY = 11, Stripe = 4, IconBox = 28, IconGlyph = 15, IconGap = 11, LineGap = 4, MaxText = 340,
                Radius = 8, TailW = 14, TailH = 7, Shadow = 9;
    const float TitleSize = 14f, DetailSize = 13f, TagSize = 12f, TagPadX = 8, TagPadY = 2, TagGap = 5, TitleTagGap = 8;
    const int DetailLines = 2;

    /// <summary>
    /// 글자는 격자 맞춤 없이 안티앨리어싱만 한다. 격자에 맞추면(AntiAliasGridFit) 맑은 고딕의 글자 폭이 정수 px 로 늘어나
    /// "지 금  한 글" 처럼 글자 사이가 벌어져 읽기 어렵다.
    /// </summary>
    const TextRenderingHint TextHint = TextRenderingHint.AntiAlias;

    /// <summary>꼬리 높이(px).</summary>
    public static int TailHeight(float scale) => (int)Math.Ceiling(TailH * scale);

    /// <summary>몸통 왼쪽 끝에서 꼬리 끝까지의 최소 거리(px). 둥근 모서리와 왼쪽 띠에 꼬리가 걸리지 않게.</summary>
    public static int TailInset(float scale) => (int)Math.Ceiling((Radius + TailW / 2 + 4) * scale);

    /// <summary>제목·알약 배치: 줄바꿈 없이, 넘치면 말줄임표.</summary>
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

    sealed class CalloutFonts : IDisposable
    {
        public readonly Font Title, Detail, Tag;
        public readonly Font? Icon;
        public readonly StringFormat Format = TextFormat();
        public readonly StringFormat Wrap = DetailFormat();

        public CalloutFonts(float scale)
        {
            // 맑은 고딕: 한글과 라틴 글자가 모두 있다(영어 UI 에서도 앱 이름이 한글일 수 있다).
            Title = new Font(BadgeRenderer.FontFamily, TitleSize * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            Detail = new Font(BadgeRenderer.FontFamily, DetailSize * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            Tag = new Font(BadgeRenderer.FontFamily, TagSize * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            Icon = Theme.IconFont((int)Math.Round(IconGlyph * scale));
        }

        public void Dispose() { Title.Dispose(); Detail.Dispose(); Tag.Dispose(); Icon?.Dispose(); Format.Dispose(); Wrap.Dispose(); }
    }

    /// <summary>말풍선 색: 카드 바탕·테두리, 강조색(왼쪽 띠·아이콘 원)과 그 위의 아이콘색, 제목·설명, 알약의 바탕·테두리·글자.</summary>
    readonly record struct CalloutColors(Color Fill, Color Border, Color Accent, Color OnAccent, Color Title, Color Detail,
                                         Color TagFill, Color TagBorder, Color TagText);

    /// <summary>
    /// 카드는 종류와 상관없이 흰색(어둡게: 짙은 회색). 강조색은 주의 = Windows 주의 아이콘의 노랑(그 위 그림은 검정), 정보 = 테마 강조색.
    /// 알약은 강조색을 옅게 깐 바탕에 같은 색조의 짙은 글자. 고대비 모드면 시스템 색 그대로.
    /// </summary>
    static CalloutColors ColorsFor(CalloutKind kind, bool dark)
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Window, SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText,
                       SystemColors.WindowText, SystemColors.WindowText, SystemColors.Window, SystemColors.WindowText, SystemColors.WindowText);
        Color fill = dark ? Hex(0x2B2B2B) : Color.White, border = dark ? Hex(0x4A4A4A) : Hex(0xD2D2D2);
        Color title = dark ? Color.White : Hex(0x1A1A1A), detail = dark ? Hex(0xD6D6D6) : Hex(0x444444);
        if (kind == CalloutKind.Warning)
            return new(fill, border, Hex(0xFFB900), Hex(0x1A1A1A), title, detail,
                       dark ? Hex(0x4A3B10) : Hex(0xFFF3C4), dark ? Hex(0x7D6420) : Hex(0xEFC64A), dark ? Hex(0xFFD95A) : Hex(0x6B4300));
        var p = Theme.Palette.From(dark ? Theme.Design.Dark : Theme.Design.Light, Theme.Design.CornerRadius);
        return new(fill, border, p.Accent, p.OnAccent, title, detail,
                   Mix(p.Accent, fill, 0.86), Mix(p.Accent, fill, 0.55), dark ? Mix(p.Accent, Color.White, 0.4) : Mix(p.Accent, Color.Black, 0.25));
    }

    static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb(ColorHex.Mix(a.ToArgb() | unchecked((int)0xFF000000), b.ToArgb() | unchecked((int)0xFF000000), t));

    static string DefaultGlyph(CalloutKind kind) => kind == CalloutKind.Warning ? "\uE7BA" : "\uE946";   // Warning / Info

    /// <summary>몸통 크기와 몸통 안의 자리(몸통 왼쪽 위 기준). 잴 때(<see cref="MeasureCallout"/>)와 그릴 때 같은 계산을 쓴다.</summary>
    sealed record Arrangement(Size Body, RectangleF Icon, RectangleF Title, RectangleF[] Tags, RectangleF Detail);

    /// <summary>
    /// 왼쪽 띠 → 아이콘 원 → 글자 칸. 글자 칸의 첫 줄은 제목과 알약(다 들어가지 않으면 알약은 다음 줄), 그 아래 설명.
    /// 아이콘과 글자 칸은 몸통 높이의 가운데에 맞춘다.
    /// </summary>
    static Arrangement Arrange(Graphics g, CalloutContent c, CalloutFonts f, float s)
    {
        float maxText = MaxText * s;
        var title = g.MeasureString(c.Title, f.Title, new SizeF(maxText, 1000), f.Format);
        var tags = (c.Tags ?? Array.Empty<string>()).Where(t => t.Length > 0).Select(t =>
        {
            var m = g.MeasureString(t, f.Tag, new SizeF(maxText, 1000), f.Format);
            return new SizeF(m.Width + 2 * TagPadX * s, m.Height + 2 * TagPadY * s);
        }).ToArray();
        float tagsW = tags.Sum(t => t.Width) + Math.Max(0, tags.Length - 1) * TagGap * s;
        float tagH = tags.Length == 0 ? 0 : tags.Max(t => t.Height);
        bool sameRow = tags.Length == 0 || title.Width + TitleTagGap * s + tagsW <= maxText;

        float headW = sameRow ? title.Width + (tags.Length == 0 ? 0 : TitleTagGap * s + tagsW) : Math.Max(title.Width, tagsW);
        float firstH = sameRow ? Math.Max(title.Height, tagH) : title.Height;
        float headH = sameRow ? firstH : title.Height + LineGap * s + tagH;

        var detail = SizeF.Empty;
        if (c.Detail is { Length: > 0 } d)
            detail = g.MeasureString(d, f.Detail, new SizeF(Math.Max(maxText, headW), f.Detail.GetHeight(g) * DetailLines + 1), f.Wrap);
        float textW = Math.Max(headW, detail.Width);
        float textH = headH + (detail.IsEmpty ? 0 : LineGap * s + detail.Height);

        float icon = f.Icon is null ? 0 : IconBox * s;
        float iconX = (Stripe + PadX) * s, textX = iconX + (icon > 0 ? icon + IconGap * s : 0);
        float innerH = Math.Max(textH, icon), top = PadY * s + (innerH - textH) / 2;
        var body = new Size((int)Math.Ceiling(textX + textW + PadX * s), (int)Math.Ceiling(2 * PadY * s + innerH));

        var tagRects = new RectangleF[tags.Length];
        float tx = sameRow ? textX + title.Width + TitleTagGap * s : textX;
        float ty = sameRow ? top + (firstH - tagH) / 2 : top + title.Height + LineGap * s;
        for (int i = 0; i < tags.Length; i++)
        {
            tagRects[i] = new RectangleF(tx, ty + (tagH - tags[i].Height) / 2, tags[i].Width, tags[i].Height);
            tx += tags[i].Width + TagGap * s;
        }
        return new Arrangement(body,
            new RectangleF(iconX, PadY * s + (innerH - icon) / 2, icon, icon),
            new RectangleF(textX, top + (firstH - title.Height) / 2, title.Width, title.Height),
            tagRects,
            new RectangleF(textX, top + headH + LineGap * s, textW, detail.Height));
    }

    /// <summary>몸통(꼬리·그림자 제외) 크기. 위치(<see cref="CalloutLayout"/>)를 정한 뒤 <see cref="Callout"/> 으로 그린다.</summary>
    public static Size MeasureCallout(CalloutContent c, float scale)
    {
        using var fonts = new CalloutFonts(scale);
        using var probe = new Bitmap(1, 1);
        using var g = Graphics.FromImage(probe);
        g.TextRenderingHint = TextHint;
        return Arrange(g, c, fonts, scale).Body;
    }

    /// <summary>
    /// 말풍선 그림. <paramref name="body"/> 는 <see cref="MeasureCallout"/> 의 크기, <paramref name="below"/>·<paramref name="tailX"/> 는
    /// <see cref="CalloutLayout"/> 의 결과다. 그림 안에서 몸통의 왼쪽 위(<paramref name="bodyOffset"/>)를 함께 돌려준다:
    /// 창 위치 = 몸통 위치 − bodyOffset. <paramref name="dark"/> 를 주지 않으면 Windows 앱 모드(밝게/어둡게)를 따른다.
    /// </summary>
    public static Bitmap Callout(CalloutContent c, float scale, Size body, bool below, int tailX, out Point bodyOffset, bool? dark = null)
    {
        using var fonts = new CalloutFonts(scale);
        int pad = (int)Math.Ceiling(Shadow * scale), tail = TailHeight(scale);
        bodyOffset = new Point(pad, pad + (below ? tail : 0));
        var bmp = new Bitmap(body.Width + 2 * pad, body.Height + tail + 2 * pad, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextHint;
        g.Clear(Color.Transparent);

        var colors = ColorsFor(c.Kind, dark ?? Theme.IsDark);
        var rect = new RectangleF(bodyOffset.X + 0.5f, bodyOffset.Y + 0.5f, body.Width - 1, body.Height - 1);
        using var path = BubblePath(rect, Radius * scale, rect.X + tailX, TailW * scale, tail, tailOnTop: below);
        if (!SystemInformation.HighContrast) DrawShadow(g, path, scale);
        using (var fill = new SolidBrush(colors.Fill)) g.FillPath(fill, path);
        using (var border = new Pen(colors.Border, Math.Max(1f, scale)) { LineJoin = LineJoin.Round }) g.DrawPath(border, path);
        using (var stripe = StripePath(rect, Radius * scale, Stripe * scale))
        using (var brush = new SolidBrush(colors.Accent))
            g.FillPath(brush, stripe);

        var a = Arrange(g, c, fonts, scale);
        float ox = bodyOffset.X, oy = bodyOffset.Y;
        if (fonts.Icon is not null)
        {
            var circle = new RectangleF(ox + a.Icon.X, oy + a.Icon.Y, a.Icon.Width, a.Icon.Height);
            using (var brush = new SolidBrush(colors.Accent)) g.FillEllipse(brush, circle);
            using var glyph = InkCentered(c.Glyph ?? DefaultGlyph(c.Kind), fonts.Icon, circle);
            using var ink = new SolidBrush(colors.OnAccent);
            g.FillPath(ink, glyph);
        }
        using (var brush = new SolidBrush(colors.Title))
            g.DrawString(c.Title, fonts.Title, brush, new RectangleF(ox + a.Title.X, oy + a.Title.Y, a.Title.Width + 1, a.Title.Height + 1), fonts.Format);
        if (a.Tags.Length > 0)
        {
            using var tagFill = new SolidBrush(colors.TagFill);
            using var tagBorder = new Pen(colors.TagBorder, Math.Max(1f, scale));
            using var tagText = new SolidBrush(colors.TagText);
            using var center = (StringFormat)fonts.Format.Clone();
            center.Alignment = StringAlignment.Center;
            center.LineAlignment = StringAlignment.Center;
            var tags = c.Tags!.Where(t => t.Length > 0).ToArray();
            for (int i = 0; i < a.Tags.Length; i++)
            {
                var r = new RectangleF(ox + a.Tags[i].X, oy + a.Tags[i].Y, a.Tags[i].Width, a.Tags[i].Height);
                using var pill = Pill(RectangleF.Inflate(r, -0.5f, -0.5f));
                g.FillPath(tagFill, pill);
                g.DrawPath(tagBorder, pill);
                g.DrawString(tags[i], fonts.Tag, tagText, new RectangleF(r.X, r.Y + 0.5f * scale, r.Width + 1, r.Height), center);
            }
        }
        if (c.Detail is { Length: > 0 } detail)
        {
            using var brush = new SolidBrush(colors.Detail);
            g.DrawString(detail, fonts.Detail, brush, new RectangleF(ox + a.Detail.X, oy + a.Detail.Y, a.Detail.Width + 1, a.Detail.Height + 1), fonts.Wrap);
        }
        return bmp;
    }

    /// <summary>
    /// 옅은 그림자. GDI+ 에는 흐림이 없어, 아래로 조금 내린 윤곽을 굵기가 다른 옅은 선 여러 겹과 옅은 채움으로 그려 번진 것처럼 보이게 한다.
    /// </summary>
    static void DrawShadow(Graphics g, GraphicsPath path, float scale)
    {
        using var shadow = (GraphicsPath)path.Clone();
        using (var m = new Matrix()) { m.Translate(0, 2f * scale); shadow.Transform(m); }
        foreach (var (width, alpha) in new[] { (12f, 5), (8f, 8), (5f, 12), (2.5f, 18) })
        {
            using var pen = new Pen(Color.FromArgb(alpha, Color.Black), width * scale) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, shadow);
        }
        using var core = new SolidBrush(Color.FromArgb(24, Color.Black));
        g.FillPath(core, shadow);
    }

    /// <summary>아이콘 글꼴 글자의 윤곽을 실제 모양(잉크) 기준으로 <paramref name="box"/> 가운데에 놓는다. 글꼴의 줄 높이로 맞추면 위아래로 치우친다.</summary>
    static GraphicsPath InkCentered(string glyph, Font font, RectangleF box)
    {
        var p = new GraphicsPath();
        p.AddString(glyph, font.FontFamily, (int)FontStyle.Regular, font.Size, PointF.Empty, StringFormat.GenericTypographic);
        var ink = p.GetBounds();
        using var m = new Matrix();
        m.Translate(box.X + (box.Width - ink.Width) / 2 - ink.X, box.Y + (box.Height - ink.Height) / 2 - ink.Y);
        p.Transform(m);
        return p;
    }

    /// <summary>가로로 긴 알약 모양(양 끝이 반원). 소나의 <see cref="Capsule"/> 은 세로로 긴 모양이다.</summary>
    static GraphicsPath Pill(RectangleF r)
    {
        float d = Math.Min(r.Width, r.Height);
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    /// <summary>몸통 왼쪽 가장자리의 색 띠. 둥근 모서리를 따라 깎인다(띠가 반지름보다 좁으면 위아래 호의 일부만).</summary>
    static GraphicsPath StripePath(RectangleF r, float radius, float width)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)), rr = d / 2;
        var p = new GraphicsPath();
        if (width >= rr)
        {
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddLine(r.Left + rr, r.Top, r.Left + width, r.Top);
            p.AddLine(r.Left + width, r.Top, r.Left + width, r.Bottom);
            p.AddLine(r.Left + width, r.Bottom, r.Left + rr, r.Bottom);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        }
        else
        {
            // 위 호: 왼쪽 끝(180°)에서 띠의 오른쪽 가장자리와 만나는 곳까지, 아래 호: 그 대칭 지점에서 왼쪽 끝까지. 두 호 사이는 곧은 선으로 이어진다.
            float sweep = (float)(Math.Acos((rr - width) / rr) * 180 / Math.PI);
            p.AddArc(r.Left, r.Top, d, d, 180, sweep);
            p.AddArc(r.Left, r.Bottom - d, d, d, 180 - sweep, sweep);
        }
        p.CloseFigure();
        return p;
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
