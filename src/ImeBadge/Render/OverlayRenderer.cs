using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 커서 옆 말풍선의 내용: 굵은 제목 한 줄과 설명(두 줄까지, 없어도 됨). 왼쪽 아이콘은 <paramref name="Glyph"/>(아이콘 글꼴 글자),
/// 없으면 종류의 기본 아이콘(정보 ⓘ, 주의 ⚠).
/// </summary>
sealed record CalloutContent(CalloutKind Kind, string Title, string? Detail = null, string? Glyph = null);

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
    // 치수는 배율 1 기준 px. Windows 11 InfoBar·툴팁과 비슷한 크기다. 글자 크기는 CalloutStyle(Core, 테스트 있음).
    const float PadX = 12, PadY = 9, IconBox = 16, IconGap = 9, LineGap = 2, MaxText = 360, Radius = 7, TailW = 14, TailH = 7, Shadow = 5;

    /// <summary>색 띠가 있으면 띠 뒤의 왼쪽 여백(배율 1 기준 px). 띠(4px) + 이만큼이 띠 없는 여백(12px)보다 조금 넓다.</summary>
    const float PadAfterStripe = 10;

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

    /// <summary>
    /// 글자를 ClearType 으로 그릴까. 말풍선 몸통은 불투명이라 그 안쪽 글자는 ClearType 을 쓸 수 있다(레이어드 창이라도 바탕이 비치지 않는다).
    /// Windows 에서 ClearType 을 껐거나 고대비면 회색 안티앨리어싱 그대로.
    /// </summary>
    static bool ClearTypeText => !SystemInformation.HighContrast && SystemInformation.IsFontSmoothingEnabled && SystemInformation.FontSmoothingType == 2;

    static TextRenderingHint TextHint => ClearTypeText ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAliasGridFit;

    sealed class CalloutFonts : IDisposable
    {
        public readonly float Scale;
        public readonly Font Title, Detail;
        public readonly Font? Icon;
        public readonly StringFormat Format = TextFormat();
        public readonly StringFormat Wrap = DetailFormat();

        public CalloutFonts(float scale)
        {
            Scale = scale;
            // 맑은 고딕: 한글과 라틴 글자가 모두 있다(영어 UI 에서도 앱 이름이 한글일 수 있다). 크기는 정수 px(CalloutStyle.FontPx).
            Title = new Font(BadgeRenderer.FontFamily, CalloutStyle.FontPx(CalloutStyle.TitleSize, scale), FontStyle.Bold, GraphicsUnit.Pixel);
            Detail = new Font(BadgeRenderer.FontFamily, CalloutStyle.FontPx(CalloutStyle.DetailSize, scale), FontStyle.Regular, GraphicsUnit.Pixel);
            Icon = Theme.IconFont((int)Math.Round(IconBox * scale));
        }

        public void Dispose() { Title.Dispose(); Detail.Dispose(); Icon?.Dispose(); Format.Dispose(); Wrap.Dispose(); }
    }

    /// <summary>마지막으로 쓴 배율의 글꼴. 재기(<see cref="MeasureCallout"/>)와 그리기(<see cref="Callout"/>)가 같은 글꼴을 쓴다(UI 스레드에서만 부른다).</summary>
    static CalloutFonts? _fonts;

    static CalloutFonts FontsFor(float scale)
    {
        if (_fonts is { } f && f.Scale == scale) return f;
        _fonts?.Dispose();
        return _fonts = new CalloutFonts(scale);
    }

    /// <summary>말풍선 색: 바탕, 테두리, 왼쪽 색 띠(없으면 null), 제목, 설명, 아이콘.</summary>
    readonly record struct Paint(Color Fill, Color Border, Color? Stripe, Color Title, Color Detail, Color Icon);

    /// <summary>
    /// 테마 팔레트로 정한 색(<see cref="CalloutStyle.For"/>): 정보는 카드색에 강조색을 조금 섞은 바탕과 강조색 띠, 주의는 Windows 11 InfoBar 의
    /// 주의(Caution) 색. 테두리는 흰 문서(밝게)·어두운 편집기(어둡게) 위에서 3:1. 고대비 모드면 시스템 색 그대로, 띠 없음.
    /// </summary>
    static Paint PaintFor(CalloutKind kind, bool dark)
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Window, SystemColors.WindowText, null, SystemColors.WindowText, SystemColors.WindowText, SystemColors.Highlight);
        var c = CalloutStyle.For(kind, dark ? Theme.Design.Dark : Theme.Design.Light);
        return new(Color.FromArgb(c.Fill), Color.FromArgb(c.Border), Color.FromArgb(c.Stripe), Color.FromArgb(c.Title), Color.FromArgb(c.Detail),
            Color.FromArgb(c.Icon));
    }

    static string DefaultGlyph(CalloutKind kind) => kind == CalloutKind.Warning ? "\uE7BA" : "\uE946";   // Warning / Info

    /// <summary>아이콘 자리의 크기: 아이콘 글꼴 한 칸. 아이콘 글꼴이 없으면(드묾) 비운다.</summary>
    static SizeF IconSize(float scale, CalloutFonts fonts) => fonts.Icon is null ? SizeF.Empty : new SizeF(IconBox * scale, IconBox * scale);

    /// <summary>몸통 왼쪽 끝에서 아이콘(없으면 글자)까지. 고대비가 아니면 색 띠가 있어 띠 폭만큼 더 들어간다.</summary>
    static float Lead(float scale) =>
        SystemInformation.HighContrast ? PadX * scale : CalloutStyle.StripeWidth(scale) + PadAfterStripe * scale;

    static (SizeF Title, SizeF Detail) TextSizes(Graphics g, CalloutContent c, CalloutFonts f, float scale)
    {
        var max = new SizeF(MaxText * scale, 1000);
        var title = g.MeasureString(c.Title, f.Title, max, f.Format);
        var detailMax = new SizeF(Math.Max(MaxText * scale, title.Width), f.Detail.GetHeight(g) * DetailLines + 1);
        var detail = c.Detail is { Length: > 0 } d ? g.MeasureString(d, f.Detail, detailMax, f.Wrap) : SizeF.Empty;
        return (title, detail);
    }

    /// <summary>몸통(꼬리·그림자 제외) 크기. 위치(<see cref="CalloutLayout"/>)를 정한 뒤 <see cref="Callout"/> 으로 그린다.</summary>
    public static Size MeasureCallout(CalloutContent c, float scale)
    {
        var fonts = FontsFor(scale);
        using var probe = new Bitmap(1, 1);
        using var g = Graphics.FromImage(probe);
        g.TextRenderingHint = TextHint;   // 그릴 때와 같은 힌트로 재야 말줄임표가 엉뚱하게 붙지 않는다
        var (title, detail) = TextSizes(g, c, fonts, scale);
        var icon = IconSize(scale, fonts);
        float textW = Math.Max(title.Width, detail.Width);
        float textH = title.Height + (detail.IsEmpty ? 0 : LineGap * scale + detail.Height);
        float w = Lead(scale) + (icon.IsEmpty ? 0 : icon.Width + IconGap * scale) + textW + PadX * scale;
        float h = 2 * PadY * scale + Math.Max(textH, icon.Height);
        return new Size((int)Math.Ceiling(w), (int)Math.Ceiling(h));
    }

    /// <summary>
    /// 말풍선 그림. <paramref name="body"/> 는 <see cref="MeasureCallout"/> 의 크기, <paramref name="below"/>·<paramref name="tailX"/> 는
    /// <see cref="CalloutLayout"/> 의 결과다. 그림 안에서 몸통의 왼쪽 위(<paramref name="bodyOffset"/>)를 함께 돌려준다:
    /// 창 위치 = 몸통 위치 − bodyOffset. <paramref name="dark"/> 를 주지 않으면 Windows 앱 모드(밝게/어둡게)를 따른다.
    /// </summary>
    public static Bitmap Callout(CalloutContent c, float scale, Size body, bool below, int tailX, out Point bodyOffset, bool? dark = null)
    {
        var fonts = FontsFor(scale);
        bool hc = SystemInformation.HighContrast;
        int pad = (int)Math.Ceiling(Shadow * scale), tail = TailHeight(scale);
        bodyOffset = new Point(pad, pad + (below ? tail : 0));
        var bmp = new Bitmap(body.Width + 2 * pad, body.Height + tail + 2 * pad, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var paint = PaintFor(c.Kind, dark ?? Theme.IsDark);
        // 테두리 굵기는 정수 px, 선의 가운데가 몸통 가장자리 안쪽 절반에 오게(1px 이면 +0.5, 2px 이면 +1) 해서 픽셀에 딱 맞는다.
        int border = hc ? 1 : CalloutStyle.BorderWidth(scale);
        float half = border / 2f;
        var rect = new RectangleF(bodyOffset.X + half, bodyOffset.Y + half, body.Width - border, body.Height - border);
        using var path = BubblePath(rect, Radius * scale, bodyOffset.X + tailX, TailW * scale, tail, tailOnTop: below);

        if (!hc)
        {
            // 옅은 그림자: GDI+ 에는 흐림이 없어 굵고 옅은 선 + 조금 진한 채움 두 겹(배지의 그림자와 같은 방식). 테두리가 윤곽을 맡으므로 옅게.
            using var shadow = (GraphicsPath)path.Clone();
            using (var m = new Matrix()) { m.Translate(0, 1.5f * scale); shadow.Transform(m); }
            using var soft = new Pen(Color.FromArgb(20, Color.Black), 4 * scale) { LineJoin = LineJoin.Round };
            using var core = new SolidBrush(Color.FromArgb(28, Color.Black));
            g.DrawPath(soft, shadow);
            g.FillPath(core, shadow);
        }
        using (var fill = new SolidBrush(paint.Fill)) g.FillPath(fill, path);
        if (paint.Stripe is { } stripeColor)
        {
            // 왼쪽 색 띠(Windows 11 InfoBar): 몸통 윤곽을 정수 픽셀 폭의 사각형으로 잘라 칠한다. 둥근 모서리는 윤곽(안티앨리어싱)을 따른다.
            var state = g.Save();
            g.SetClip(new Rectangle(bodyOffset.X, bodyOffset.Y, CalloutStyle.StripeWidth(scale), body.Height));
            using var stripe = new SolidBrush(stripeColor);
            g.FillPath(stripe, path);
            g.Restore(state);
        }
        using (var pen = new Pen(paint.Border, border) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);

        // 글자·아이콘 자리. 원점을 정수 픽셀에 맞춘다(소수 자리면 힌팅한 획이 두 픽셀에 걸쳐 흐려진다).
        g.TextRenderingHint = TextHint;
        var (title, detail) = TextSizes(g, c, fonts, scale);
        var icon = IconSize(scale, fonts);
        float cy = bodyOffset.Y + body.Height / 2f;
        float x = MathF.Round(bodyOffset.X + Lead(scale));
        var iconRect = new RectangleF(x, MathF.Round(cy - icon.Height / 2), icon.Width, icon.Height);
        if (!icon.IsEmpty) x = MathF.Round(x + icon.Width + IconGap * scale);
        float textH = title.Height + (detail.IsEmpty ? 0 : LineGap * scale + detail.Height);
        float y = MathF.Round(cy - textH / 2);
        float detailY = MathF.Round(y + title.Height + LineGap * scale);
        float maxW = bodyOffset.X + body.Width - PadX * scale - x;

        void DrawContent(Graphics t)
        {
            if (fonts.Icon is not null)
            {
                var hint = t.TextRenderingHint;
                t.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;   // 아이콘 글리프는 색 번짐 없이 회색 안티앨리어싱
                using var brush = new SolidBrush(paint.Icon);
                using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                t.DrawString(c.Glyph ?? DefaultGlyph(c.Kind), fonts.Icon, brush, iconRect, center);
                t.TextRenderingHint = hint;
            }
            using (var brush = new SolidBrush(paint.Title))
                t.DrawString(c.Title, fonts.Title, brush, new RectangleF(x, y, maxW + 1, title.Height + 1), fonts.Format);
            if (!detail.IsEmpty)
            {
                using var brush = new SolidBrush(paint.Detail);
                t.DrawString(c.Detail!, fonts.Detail, brush, new RectangleF(x, detailY, maxW + 1, detail.Height + 1), fonts.Wrap);
            }
        }

        if (!ClearTypeText)
        {
            DrawContent(g);
            return bmp;
        }
        // ClearType 은 불투명한 바탕 위에서만 맞게 섞인다. 글자·아이콘 자리를 몸통 색으로 칠한 불투명 그림에 그린 뒤 그 자리에 그대로 옮긴다.
        // 자리는 몸통 안쪽 여백 속이라 둥근 모서리·테두리·색 띠에 닿지 않는다.
        int left = (int)Math.Floor(iconRect.Left) - 2, top = (int)Math.Floor(Math.Min(icon.IsEmpty ? y : iconRect.Top, y)) - 2;
        int right = (int)Math.Ceiling(bodyOffset.X + body.Width - PadX * scale) + 2;
        int bottom = (int)Math.Ceiling(Math.Max(iconRect.Bottom, (detail.IsEmpty ? y + title.Height : detailY + detail.Height))) + 2;
        var area = Rectangle.FromLTRB(left, top, right, bottom);
        area.Intersect(new Rectangle(bodyOffset.X + border, bodyOffset.Y + border, body.Width - 2 * border, body.Height - 2 * border));
        using (var layer = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), PixelFormat.Format32bppRgb))
        {
            using (var lg = Graphics.FromImage(layer))
            {
                lg.Clear(paint.Fill);
                lg.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                lg.TranslateTransform(-area.X, -area.Y);
                DrawContent(lg);
            }
            var state = g.Save();
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(layer, area, 0, 0, area.Width, area.Height, GraphicsUnit.Pixel);
            g.Restore(state);
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
