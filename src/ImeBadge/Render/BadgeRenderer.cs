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

/// <summary>배지 그리기 (GDI+로 투명 비트맵 생성). 결과는 미리 곱한 알파(PArgb)라 레이어드 창에 바로 올릴 수 있다.</summary>
static class BadgeRenderer
{
    public const string FontFamily = "Malgun Gothic";

    public static (string text, Color color) Look(ImeState s, in BadgeTheme theme) => s switch
    {
        ImeState.Hangul => ("한", theme.Hangul),
        ImeState.English => ("A", theme.English),
        _ => ("?", theme.Other),
    };

    public static Bitmap Render(ImeState state, BadgeStyle style, float scale, in BadgeTheme theme)
    {
        var (text, color) = Look(state, theme);
        return style switch
        {
            BadgeStyle.Dot => RenderDot(color, scale),
            BadgeStyle.Underline => RenderUnderline(color, scale),
            BadgeStyle.Box => RenderText(text, color, scale, rounded: false),
            _ => RenderText(text, color, scale, rounded: true),   // Pill, DotFlash(글자 단계)
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

    static Bitmap RenderDot(Color color, float scale)
    {
        int d = (int)Math.Round(9 * scale);
        var bmp = NewCanvas(d + 2, d + 2, out var g);
        using (g)
        {
            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.FromArgb(160, Color.White), Math.Max(1f, scale));
            g.FillEllipse(brush, 1, 1, d, d);
            g.DrawEllipse(pen, 1, 1, d, d);
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
        var bmp = NewCanvas(w, h, out var g);
        using (g)
        {
            var rect = new RectangleF(0.5f, 0.5f, w - 1, h - 1);
            using var path = RoundedRect(rect, rounded ? (h - 1) / 2f : 3 * scale);
            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.FromArgb(110, Color.White), 1f);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, w, h), sf);
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
