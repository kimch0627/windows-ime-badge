using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ImeBadge;

/// <summary>배지 색 조합. 설정의 "#RRGGBB" 문자열에서 만든다.</summary>
readonly record struct BadgeTheme(Color Hangul, Color English, Color Other)
{
    public static BadgeTheme From(Settings s) => new(
        ParseOr(s.HangulColor, Settings.DefaultHangulColor),
        ParseOr(s.EnglishColor, Settings.DefaultEnglishColor),
        Color.DarkOrange);

    static Color ParseOr(string text, string fallback) =>
        Color.FromArgb(ColorHex.TryParse(text, out int a) ? a : (ColorHex.TryParse(fallback, out int b) ? b : unchecked((int)0xFF000000)));
}

/// <summary>
/// 배지 그리기 (GDI+로 투명 비트맵 생성). 결과는 미리 곱한 알파(PArgb)라 레이어드 창에 바로 올릴 수 있다.
/// 불투명도는 배경(채움)에만 적용하고 글자는 항상 또렷하게 둔다. 글자색은 배경 밝기에 따라 흰색/검은색을 고른다.
/// </summary>
static class BadgeRenderer
{
    public const string FontFamily = "Malgun Gothic";

    public static (string text, Color color) Look(ImeState s, in BadgeTheme theme) => s switch
    {
        ImeState.Hangul => ("한", theme.Hangul),
        ImeState.English => ("A", theme.English),
        _ => ("?", theme.Other),
    };

    /// <summary>배경색 위에서 더 잘 읽히는 글자색(흰/검).</summary>
    public static Color TextColorOn(Color background) =>
        ColorHex.PrefersWhiteText(background.ToArgb()) ? Color.White : Color.Black;

    public static Bitmap Render(ImeState state, BadgeStyle style, float scale, in BadgeTheme theme, int opacityPercent = 100)
    {
        var (text, color) = Look(state, theme);
        var fill = Color.FromArgb(Math.Clamp(255 * opacityPercent / 100, 30, 255), color);
        return style switch
        {
            BadgeStyle.Dot => RenderDot(fill, scale),
            BadgeStyle.Underline => RenderUnderline(fill, scale),
            BadgeStyle.Box => RenderText(text, fill, scale, rounded: false),
            _ => RenderText(text, fill, scale, rounded: true),   // Pill, DotFlash(글자 단계)
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

    /// <summary>채움색과 대비되는 얇은 테두리. 배경과 같은 색 위에 놓여도 윤곽이 남는다.</summary>
    static Pen OutlinePen(Color fill, float width) =>
        new(Color.FromArgb(110, TextColorOn(fill)), width);

    /// <summary>그림자 여백(px, 배율 1 기준). 그림자는 아래로 1px 떨어지고 가장자리가 약간 번진다.</summary>
    static int ShadowPad(float scale) => (int)Math.Ceiling(2 * scale);

    /// <summary>
    /// 배지 아래에 옅은 그림자. 배지가 같은 색 배경(파란 선택 영역 위의 파란 배지) 위에 놓여도 떠 보인다.
    /// 흐림(blur)은 GDI+ 에 없으므로 굵고 옅은 선 + 조금 진한 채움 두 겹으로 흉내 낸다.
    /// </summary>
    static void DrawShadow(Graphics g, GraphicsPath path, float scale)
    {
        using var shadow = (GraphicsPath)path.Clone();
        using var m = new Matrix();
        m.Translate(0, Math.Max(1f, scale));
        shadow.Transform(m);
        using var soft = new Pen(Color.FromArgb(26, Color.Black), Math.Max(1.5f, 1.5f * scale)) { LineJoin = LineJoin.Round };
        using var core = new SolidBrush(Color.FromArgb(48, Color.Black));
        g.DrawPath(soft, shadow);
        g.FillPath(core, shadow);
    }

    static Bitmap RenderDot(Color color, float scale)
    {
        int d = (int)Math.Round(9 * scale);
        int pad = ShadowPad(scale);
        var bmp = NewCanvas(d + 2 * pad, d + 2 * pad + pad, out var g);
        using (g)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(pad, pad, d, d);
            DrawShadow(g, path, scale);
            using var brush = new SolidBrush(color);
            using var pen = OutlinePen(color, Math.Max(1f, scale));
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

    static Bitmap RenderText(string text, Color color, float scale, bool rounded)
    {
        float fontPx = 13 * scale;
        using var font = new Font(FontFamily, fontPx, FontStyle.Bold, GraphicsUnit.Pixel);   // 없으면 GDI+ 가 기본 글꼴로 대체
        SizeF ts;
        using (var probeBmp = new Bitmap(1, 1))
        using (var probe = Graphics.FromImage(probeBmp))
            ts = probe.MeasureString(text, font);

        int h = (int)Math.Round(20 * scale);
        int w = (int)Math.Round(Math.Max(h, ts.Width + 10 * scale));
        int pad = ShadowPad(scale);   // 사방 여백 + 아래쪽에 그림자가 떨어질 자리
        var bmp = NewCanvas(w + 2 * pad, h + 2 * pad + pad, out var g);
        using (g)
        {
            var rect = new RectangleF(pad + 0.5f, pad + 0.5f, w - 1, h - 1);
            using var path = RoundedRect(rect, rounded ? (h - 1) / 2f : 3 * scale);
            DrawShadow(g, path, scale);
            using var brush = new SolidBrush(color);
            using var pen = OutlinePen(color, 1f);
            using var textBrush = new SolidBrush(TextColorOn(color));
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, new RectangleF(pad, pad, w, h), sf);
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
