using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ImeBadge;

/// <summary>
/// 배지 색·질감 조합. 설정의 "#RRGGBB" 문자열과 디자인 테마에서 만든다.
/// <paramref name="Character"/> 가 비어 있지 않으면 둥근 배지 대신 그 캐릭터 모양(고양이 등)으로 그린다.
/// </summary>
readonly record struct BadgeTheme(Color Hangul, Color English, Color Other,
    BadgeFinish Finish = BadgeFinish.Flat, float Gloss = 0f, string Character = "", bool ShowCapsLock = true)
{
    public static BadgeTheme From(Settings s)
    {
        var design = DesignThemes.Get(s.Theme);
        return new(
            ParseOr(s.HangulColor, Settings.DefaultHangulColor),
            ParseOr(s.EnglishColor, Settings.DefaultEnglishColor),
            Color.DarkOrange,
            design.Finish, design.Gloss, BadgeCharacters.Normalize(s.Character), s.ShowCapsLock);
    }

    /// <summary>테마 기본색으로 만든 조합(설정 창의 테마 타일, 트레이 메뉴의 테마 항목 미리보기용).</summary>
    public static BadgeTheme Of(DesignTheme d) => new(
        ParseOr(d.HangulColor, Settings.DefaultHangulColor), ParseOr(d.EnglishColor, Settings.DefaultEnglishColor), Color.DarkOrange,
        d.Finish, d.Gloss);

    static Color ParseOr(string text, string fallback) =>
        Color.FromArgb(ColorHex.TryParse(text, out int a) ? a : (ColorHex.TryParse(fallback, out int b) ? b : unchecked((int)0xFF000000)));
}

/// <summary>
/// 배지 그리기 (GDI+로 투명 비트맵 생성). 결과는 미리 곱한 알파(PArgb)라 레이어드 창에 바로 올릴 수 있다.
/// 불투명도는 배경(채움)에만 적용하고 글자는 항상 또렷하게 둔다. 글자색은 배경 밝기에 따라 고른다.
/// 클래식(Flat)은 예전과 같은 경로로 그리고, 새 테마(Soft)는 색조 글자·색조 그림자·위쪽 광택을 더한다.
/// </summary>
static class BadgeRenderer
{
    public const string FontFamily = "Malgun Gothic";

    /// <summary>배지 글자와 색. Caps Lock 표시 규칙(한/꺆, a/A, 밑줄)은 <see cref="BadgeText"/> 가 정한다.</summary>
    public static (string text, Color color, bool capsBar) Look(ImeState s, in BadgeTheme theme, bool capsLock = false)
    {
        if (s is not (ImeState.Hangul or ImeState.English)) return ("?", theme.Other, false);
        var (text, bar) = BadgeText.For(s == ImeState.Hangul, capsLock, theme.ShowCapsLock);
        return (text, s == ImeState.Hangul ? theme.Hangul : theme.English, bar);
    }

    /// <summary>트레이 아이콘용 글자와 색. Caps Lock 과 상관없이 항상 "한"/"A"(16px 에서도 읽히는 글자).</summary>
    public static (string text, Color color) TrayLook(ImeState s, in BadgeTheme theme) => s switch
    {
        ImeState.Hangul => (BadgeText.Hangul, theme.Hangul),
        ImeState.English => (BadgeText.EnglishUpper, theme.English),
        _ => ("?", theme.Other),
    };

    /// <summary>배경색 위에서 더 잘 읽히는 글자색. 클래식은 흰/검, 부드러운 테마는 흰색 또는 배지 색을 진하게 만든 색.</summary>
    public static Color TextColorOn(Color background, BadgeFinish finish = BadgeFinish.Flat)
    {
        int argb = background.ToArgb() | unchecked((int)0xFF000000);
        if (finish == BadgeFinish.Soft) return Color.FromArgb(ColorHex.SoftTextOn(argb));
        return ColorHex.PrefersWhiteText(argb) ? Color.White : Color.Black;
    }

    public static Bitmap Render(ImeState state, BadgeStyle style, float scale, in BadgeTheme theme, int opacityPercent = 100, bool capsLock = false)
    {
        var (text, color, bar) = Look(state, theme, capsLock);
        var fill = Color.FromArgb(Math.Clamp(255 * opacityPercent / 100, 30, 255), color);
        var ink = TextColorOn(color, theme.Finish);
        if (theme.Character.Length > 0 && style is BadgeStyle.Pill or BadgeStyle.Box)
            return RenderCharacter(theme.Character, text, bar, fill, ink, scale, theme);
        return style switch
        {
            BadgeStyle.Dot => RenderDot(fill, scale, theme),
            BadgeStyle.Underline => RenderUnderline(fill, scale),
            BadgeStyle.Box => RenderText(text, bar, fill, ink, scale, rounded: false, theme),
            _ => RenderText(text, bar, fill, ink, scale, rounded: true, theme),   // Pill, DotFlash(글자 단계)
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

    /// <summary>채움색과 대비되는 얇은 테두리. 배경과 같은 색 위에 놓여도 윤곽이 남는다. 부드러운 테마는 더 옅게.</summary>
    static Pen OutlinePen(Color fill, Color ink, float width, in BadgeTheme theme) =>
        theme.Finish == BadgeFinish.Soft ? new(Color.FromArgb(44, ink), width) : new(Color.FromArgb(110, TextColorOn(fill)), width);

    /// <summary>채움 붓. 광택이 있으면 위쪽이 밝은 세로 그라데이션(젤리·사탕 같은 느낌).</summary>
    static Brush FillBrush(Color fill, RectangleF bounds, in BadgeTheme theme)
    {
        if (theme.Gloss <= 0f || bounds.Height <= 0) return new SolidBrush(fill);
        var top = Color.FromArgb(fill.A, Color.FromArgb(ColorHex.Mix(fill.ToArgb(), unchecked((int)0xFFFFFFFF), theme.Gloss)));
        var b = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2), top, fill, LinearGradientMode.Vertical);
        b.InterpolationColors = new ColorBlend { Colors = new[] { top, fill, fill }, Positions = new[] { 0f, 0.62f, 1f } };
        return b;
    }

    /// <summary>그림자 여백(px, 배율 1 기준). 그림자는 아래로 1px 떨어지고 가장자리가 약간 번진다.</summary>
    static int ShadowPad(float scale) => (int)Math.Ceiling(2 * scale);

    /// <summary>
    /// 배지 아래에 옅은 그림자. 배지가 같은 색 배경(파란 선택 영역 위의 파란 배지) 위에 놓여도 떠 보인다.
    /// 흐림(blur)은 GDI+ 에 없으므로 굵고 옅은 선 + 조금 진한 채움 두 겹으로 흉내 낸다. 부드러운 테마는 검정 대신 배지 색조.
    /// </summary>
    static void DrawShadow(Graphics g, GraphicsPath path, float scale, Color fill, in BadgeTheme theme, float penScale = 1f)
    {
        using var shadow = (GraphicsPath)path.Clone();
        using var m = new Matrix();
        m.Translate(0, Math.Max(1f, scale) / penScale);
        shadow.Transform(m);
        bool tint = theme.Finish == BadgeFinish.Soft;
        var baseColor = tint ? Color.FromArgb(ColorHex.Darken(fill.ToArgb() | unchecked((int)0xFF000000), 0.45)) : Color.Black;
        using var soft = new Pen(Color.FromArgb(tint ? 40 : 26, baseColor), Math.Max(1.5f, 1.5f * scale) / penScale) { LineJoin = LineJoin.Round };
        using var core = new SolidBrush(Color.FromArgb(tint ? 64 : 48, baseColor));
        g.DrawPath(soft, shadow);
        g.FillPath(core, shadow);
    }

    static Bitmap RenderDot(Color color, float scale, in BadgeTheme theme)
    {
        int d = (int)Math.Round(9 * scale);
        int pad = ShadowPad(scale);
        var bmp = NewCanvas(d + 2 * pad, d + 2 * pad + pad, out var g);
        using (g)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(pad, pad, d, d);
            DrawShadow(g, path, scale, color, theme);
            using var brush = FillBrush(color, new RectangleF(pad, pad, d, d), theme);
            using var pen = OutlinePen(color, TextColorOn(color, theme.Finish), Math.Max(1f, scale), theme);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
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

    static SizeF Measure(string text, Font font)
    {
        using var probeBmp = new Bitmap(1, 1);
        using var probe = Graphics.FromImage(probeBmp);
        return probe.MeasureString(text, font);
    }

    static Bitmap RenderText(string text, bool capsBar, Color color, Color ink, float scale, bool rounded, in BadgeTheme theme)
    {
        float fontPx = 13 * scale;
        using var font = new Font(FontFamily, fontPx, FontStyle.Bold, GraphicsUnit.Pixel);   // 없으면 GDI+ 가 기본 글꼴로 대체
        SizeF ts = Measure(text, font);

        int h = (int)Math.Round(20 * scale);
        int w = (int)Math.Round(Math.Max(h, ts.Width + 10 * scale));
        int pad = ShadowPad(scale);   // 사방 여백 + 아래쪽에 그림자가 떨어질 자리
        var bmp = NewCanvas(w + 2 * pad, h + 2 * pad + pad, out var g);
        using (g)
        {
            var rect = new RectangleF(pad + 0.5f, pad + 0.5f, w - 1, h - 1);
            using var path = RoundedRect(rect, rounded ? (h - 1) / 2f : 3 * scale);
            DrawShadow(g, path, scale, color, theme);
            using var brush = FillBrush(color, rect, theme);
            using var pen = OutlinePen(color, ink, 1f, theme);
            using var textBrush = new SolidBrush(ink);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var textArea = new RectangleF(pad, pad, w, h);
            if (capsBar)
            {
                // Caps Lock: 글자를 조금 올리고 그 아래에 짧은 밑줄(키보드의 Caps Lock 표시등을 닮은 모양).
                float lift = 1.5f * scale, barH = Math.Max(1f, 1.5f * scale), barW = Math.Max(6 * scale, Math.Min(ts.Width * 0.8f, w - 8 * scale));
                textArea.Offset(0, -lift);
                g.FillRectangle(textBrush, pad + (w - barW) / 2, pad + h - 4.5f * scale, barW, barH);
            }
            g.DrawString(text, font, textBrush, textArea, sf);
        }
        return bmp;
    }

    // ── 캐릭터 모양 ──
    // 모든 좌표는 24 단위 상자(배율 1 에서 1 단위 = 1px). 글자가 들어가는 몸통의 높이가 약 20 으로 둥근 배지와 같고, 귀는 그 위로 나온다.
    // 비트맵이 조금 커지지만 위치 계산(BadgeLayout)은 비트맵 크기로 하므로 커서 옆 정렬은 그대로다.

    /// <summary>캐릭터 한 종류의 틀: 전체 크기, 글자 중심, 글자 크기(단위).</summary>
    readonly record struct Figure(float Width, float Height, float TextX, float TextY, float FontSize);

    static Figure FigureOf(string character) => character switch
    {
        BadgeCharacters.Cat => new(24, 25, 12, 16.2f, 12.5f),
        BadgeCharacters.Dog => new(26, 23, 13, 13.6f, 12.5f),
        BadgeCharacters.Heart => new(24, 22, 12, 10.6f, 11.5f),
        BadgeCharacters.Cloud => new(27, 21, 13.5f, 14.2f, 12f),
        _ => new(26, 25, 13, 14.6f, 10.5f),   // Star
    };

    /// <summary>2차 베지어(SVG 의 Q)를 GDI+ 의 3차 베지어로.</summary>
    static void AddQuad(GraphicsPath p, PointF p0, PointF c, PointF p2) =>
        p.AddBezier(p0, new PointF(p0.X + 2f / 3 * (c.X - p0.X), p0.Y + 2f / 3 * (c.Y - p0.Y)),
            new PointF(p2.X + 2f / 3 * (c.X - p2.X), p2.Y + 2f / 3 * (c.Y - p2.Y)), p2);

    /// <summary>몸통 윤곽(글자가 들어가는 부분 + 귀처럼 같은 색으로 이어진 부분).</summary>
    static GraphicsPath BodyPath(string character)
    {
        var p = new GraphicsPath(FillMode.Winding);
        switch (character)
        {
            case BadgeCharacters.Cat:
                // 뾰족 귀 두 개 + 아래가 둥근 얼굴
                p.AddLine(2.2f, 11.5f, 3.4f, 1.6f);
                AddQuad(p, new(3.4f, 1.6f), new(3.7f, 0.4f), new(4.8f, 1.1f));
                p.AddLine(4.8f, 1.1f, 10.2f, 5.2f);
                p.AddLine(10.2f, 5.2f, 13.8f, 5.2f);
                p.AddLine(13.8f, 5.2f, 19.2f, 1.1f);
                AddQuad(p, new(19.2f, 1.1f), new(20.3f, 0.4f), new(20.6f, 1.6f));
                p.AddLine(20.6f, 1.6f, 21.8f, 11.5f);
                AddQuad(p, new(21.8f, 11.5f), new(23f, 14f), new(23f, 16f));
                p.AddArc(1f, 6.1f, 22f, 19.8f, 0, 180);
                AddQuad(p, new(1f, 16f), new(1f, 14f), new(2.2f, 11.5f));
                p.CloseFigure();
                break;
            case BadgeCharacters.Dog:
                p.AddPath(RoundedRect(new RectangleF(4, 3, 18, 20), 9), false);
                break;
            case BadgeCharacters.Heart:
                p.AddBezier(12f, 21.2f, 5f, 16.4f, 0.8f, 12.6f, 0.8f, 7.6f);
                p.AddArc(0.6f, 0.3f, 11.6f, 11.6f, 165, 180);
                p.AddArc(11.8f, 0.3f, 11.6f, 11.6f, 195, 180);
                p.AddBezier(23.2f, 7.6f, 23.2f, 12.6f, 19f, 16.4f, 12f, 21.2f);
                p.CloseFigure();
                break;
            case BadgeCharacters.Cloud:
                // 둥근 밑판 + 크기가 다른 원 세 개(Winding 이라 겹친 곳도 한 덩어리로 칠해진다)
                p.AddPath(RoundedRect(new RectangleF(2, 11.5f, 23, 9), 4.5f), false);
                p.AddEllipse(3f, 8f, 10f, 10f);
                p.AddEllipse(8f, 3.5f, 13f, 13f);
                p.AddEllipse(15.5f, 9f, 9f, 9f);
                break;
            default:   // Star: 꼭짓점을 둥글게 하려고 같은 색의 굵은 둥근 선을 함께 그린다(PaintBody)
                p.AddPolygon(new PointF[]
                {
                    new(13, 1.6f), new(16.3f, 8.5f), new(23.8f, 9.4f), new(18.3f, 14.6f), new(19.7f, 22.1f),
                    new(13, 18.4f), new(6.3f, 22.1f), new(7.7f, 14.6f), new(2.2f, 9.4f), new(9.7f, 8.5f),
                });
                break;
        }
        return p;
    }

    static Bitmap RenderCharacter(string character, string text, bool capsBar, Color color, Color ink, float scale, in BadgeTheme theme)
    {
        var fig = FigureOf(character);
        int pad = ShadowPad(scale);
        int w = (int)Math.Ceiling(fig.Width * scale), h = (int)Math.Ceiling(fig.Height * scale);
        var bmp = NewCanvas(w + 2 * pad, h + 2 * pad + pad, out var g);
        using (g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAlias;   // 확대 변환과 함께 쓰면 GridFit 은 글자가 흔들린다
            g.TranslateTransform(pad, pad);
            g.ScaleTransform(scale, scale);
            bool star = character == BadgeCharacters.Star;
            using var body = BodyPath(character);
            var bounds = new RectangleF(0, 0, fig.Width, fig.Height);
            using var brush = FillBrush(color, bounds, theme);

            DrawShadow(g, body, scale, color, theme, penScale: scale);
            // 테두리를 먼저 굵게 그리고 그 위를 채우면 바깥 절반만 남아, 겹친 원(구름)의 안쪽 선이 보이지 않는다.
            using (var edge = OutlinePen(color, ink, 1.6f, theme)) { edge.LineJoin = LineJoin.Round; if (star) edge.Width = 4f; g.DrawPath(edge, body); }
            if (star) { using var round = new Pen(brush, 2.4f) { LineJoin = LineJoin.Round }; g.DrawPath(round, body); }
            g.FillPath(brush, body);

            // 귀 장식: 고양이는 안쪽 귀(밝게), 강아지는 늘어진 귀(조금 진하게).
            if (character == BadgeCharacters.Cat)
            {
                using var inner = new SolidBrush(Color.FromArgb(color.A, Color.FromArgb(ColorHex.Mix(color.ToArgb(), unchecked((int)0xFFFFFFFF), 0.45))));
                g.FillPolygon(inner, new PointF[] { new(4.6f, 4f), new(8.4f, 6.6f), new(5.2f, 8.6f) });
                g.FillPolygon(inner, new PointF[] { new(19.4f, 4f), new(15.6f, 6.6f), new(18.8f, 8.6f) });
            }
            else if (character == BadgeCharacters.Dog)
            {
                using var ear = new SolidBrush(Color.FromArgb(color.A, Color.FromArgb(ColorHex.Mix(color.ToArgb(), unchecked((int)0xFF000000), 0.16))));
                foreach (var (cx, angle) in new[] { (4.2f, 18f), (21.8f, -18f) })
                {
                    var state = g.Save();
                    g.TranslateTransform(cx, 9.5f);
                    g.RotateTransform(angle);
                    g.FillEllipse(ear, -3.6f, -7f, 7.2f, 14f);
                    g.Restore(state);
                }
            }

            using var font = new Font(FontFamily, fig.FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(ink);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            float ty = fig.TextY;
            if (capsBar)
            {
                ty -= 1.4f;
                float barW = fig.FontSize * 0.75f;
                g.FillRectangle(textBrush, fig.TextX - barW / 2, ty + fig.FontSize * 0.5f + 0.6f, barW, 1.4f);
            }
            g.DrawString(text, font, textBrush, new RectangleF(fig.TextX - fig.Width / 2, ty - fig.FontSize, fig.Width, fig.FontSize * 2), sf);
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
