using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace ImeBadge;

/// <summary>
/// README 맨 위 소개 그림(docs/images/hero.png). <c>ImeBadge.exe --render-hero [경로]</c> 로 실행하면 창·트레이 없이 그림만 저장하고 끝낸다.
/// 배지는 앱과 같은 <see cref="BadgeRenderer"/> 로 그리고 자리도 <see cref="BadgeLayout"/> 가 정하므로, 배지 모습이 바뀌어도
/// 다시 실행하기만 하면 그림이 따라온다. CI 의 Windows 러너가 롤링 사전 릴리스에 <c>hero.png</c> 로 붙인다(build.yml).
/// 레이아웃은 논리 px(가로 1200)로 적고 <see cref="K"/> 배로 그린다(GitHub 고해상도 화면에서도 또렷하게).
/// </summary>
static class RenderHero
{
    public const string Switch = "--render-hero";

    /// <summary>그림 배율. 배지도 이 배율 × <see cref="BadgeScale"/> 로 그린다.</summary>
    const float K = 2f, BadgeScale = 1.25f;

    const float Width = 1200, Margin = 40, CardGap = 12;

    static readonly Color Page = Color.FromArgb(0xF9, 0xF9, 0xFB), Ink = Color.FromArgb(0x1B, 0x1B, 0x1B),
        Subtle = Color.FromArgb(0x6B, 0x6B, 0x73), CaretInk = Color.FromArgb(0x1B, 0x1B, 0x1B);

    /// <summary><see cref="Switch"/> 가 있으면 그림을 저장하고 true. 실패하면 로그를 남기고 종료 코드 1.</summary>
    public static bool TryRun(string[] args)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        string path = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : "hero.png";
        try
        {
            path = Path.GetFullPath(path);
            using var hero = Draw();
            hero.Save(path, ImageFormat.Png);
            Log.Write($"render-hero: {path} ({hero.Width}x{hero.Height})");
        }
        catch (Exception ex)
        {
            Log.Error("render-hero failed", ex);
            Environment.ExitCode = 1;
        }
        return true;
    }

    static readonly (string Id, string Name)[] Themes =
    {
        (DesignThemes.ClassicId, "클래식"), ("blossom", "벚꽃"), ("candy", "캔디"), ("mint", "민트초코"), ("midnight", "미드나잇"),
    };

    /// <summary>아래 줄의 캐릭터 배지: 앞 글, 테마, 캐릭터, 상태, Caps Lock.</summary>
    static readonly (string Before, string Theme, string Character, ImeState State, bool Caps)[] Characters =
    {
        ("가나다", "blossom", BadgeCharacters.Cat, ImeState.Hangul, false),
        ("abc", "mint", BadgeCharacters.Dog, ImeState.English, false),
        ("가나다", "candy", BadgeCharacters.Heart, ImeState.Hangul, false),
        ("ABC", "blossom", BadgeCharacters.Cloud, ImeState.English, true),
        ("가나다", "midnight", BadgeCharacters.Star, ImeState.Hangul, true),
    };

    static Bitmap Draw()
    {
        const float Height = 400;
        var bmp = new Bitmap((int)(Width * K), (int)(Height * K), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Page);
        var area = new Rectangle(0, 0, bmp.Width, bmp.Height);

        string latin = BadgeFonts.IsInstalled(BadgeFonts.Latin) ? BadgeFonts.Latin : BadgeRenderer.FontFamily;
        using var title = Px(latin, 40, FontStyle.Bold);
        using var subtitle = Px(BadgeRenderer.FontFamily, 20, FontStyle.Regular);
        using var tagline = Px(BadgeRenderer.FontFamily, 14, FontStyle.Regular);
        using var body = Px(BadgeRenderer.FontFamily, 16, FontStyle.Regular);
        using var label = Px(BadgeRenderer.FontFamily, 13, FontStyle.Bold);
        using var note = Px(BadgeRenderer.FontFamily, 11, FontStyle.Regular);

        // 제목
        float y = 34;
        Text(g, "ImeBadge", title, Ink, Margin, y);
        float titleW = Measure(g, "ImeBadge", title).Width;
        Text(g, "한/영 입력 상태 배지", subtitle, Subtle, Margin + titleW + 14, y + 17);
        Text(g, "글자를 치기 전에, 커서 옆 작은 배지로 지금 한글인지 영문인지 알려 줘요. 테마와 캐릭터로 내 취향대로.", tagline, Subtle, Margin, y + 60);

        // 테마 카드 다섯 장: 한글 줄과 영문 줄, 배지는 caret 위-오른쪽(기본 위치)
        float cardY = 128, cardH = 128, cardW = (Width - 2 * Margin - 4 * CardGap) / 5;
        for (int t = 0; t < Themes.Length; t++)
        {
            var design = DesignThemes.Get(Themes[t].Id);
            var theme = BadgeTheme.Of(design);
            float x = Margin + t * (cardW + CardGap);
            Card(g, new RectangleF(x, cardY, cardW, cardH), Color.FromArgb(design.Light.Window), Color.FromArgb(design.Light.Border));
            Line(g, "안녕하세요", body, x + 16, cardY + 34, ImeState.Hangul, theme, false, area);
            Line(g, "hello", body, x + 16, cardY + 76, ImeState.English, theme, false, area);
            Text(g, Themes[t].Name, label, Color.FromArgb(design.Light.Text), x + 16, cardY + cardH - 26);
        }

        // 캐릭터 · Caps Lock 줄
        float rowY = cardY + cardH + 18, rowH = 104;
        Card(g, new RectangleF(Margin, rowY, Width - 2 * Margin, rowH), Color.White, Color.FromArgb(0xE6, 0xE6, 0xEA));
        Text(g, "캐릭터 · Caps Lock", label, Subtle, Margin + 24, rowY + rowH / 2 - 9);
        float cx = Margin + 220, step = (Width - 2 * Margin - 220 - 24) / Characters.Length;
        foreach (var (before, themeId, character, state, caps) in Characters)
        {
            var theme = BadgeTheme.Of(DesignThemes.Get(themeId)) with { Character = character };
            Line(g, before, body, cx, rowY + rowH / 2 + 4, state, theme, caps, area);
            cx += step;
        }

        Text(g, "배지는 앱의 렌더러로 그린 실제 모습입니다 (배율 125%, 불투명도 100%)", note, Subtle, Margin, rowY + rowH + 16);
        return bmp;
    }

    /// <summary>글 한 줄 + caret + 그 caret 에 붙은 배지. <paramref name="baselineTop"/> 은 글 윗줄(논리 px).</summary>
    static void Line(Graphics g, string text, Font font, float x, float baselineTop, ImeState state, BadgeTheme theme, bool caps, Rectangle area)
    {
        Text(g, text, font, Ink, x, baselineTop);
        float w = Measure(g, text, font).Width;
        var caret = new Rectangle((int)Math.Round((x + w + 2) * K), (int)Math.Round((baselineTop + 1) * K), (int)Math.Round(1.5f * K), (int)Math.Round(20 * K));
        using (var brush = new SolidBrush(CaretInk)) g.FillRectangle(brush, caret);
        float scale = K * BadgeScale;
        using var badge = BadgeRenderer.Render(state, BadgeStyle.Pill, scale, theme, 100, capsLock: caps);
        var pos = BadgeLayout.Compute(new LayoutInput(caret, badge.Size, BadgeStyle.Pill, BadgePlacement.AboveRight, scale, area));
        g.DrawImage(badge, new Rectangle(pos, badge.Size));
    }

    static void Card(Graphics g, RectangleF r, Color fill, Color border)
    {
        var d = new RectangleF(r.X * K, r.Y * K, r.Width * K, r.Height * K);
        using var path = Round(d, 14 * K);
        using (var shadow = new SolidBrush(Color.FromArgb(10, 0, 0, 0)))
        using (var m = new Matrix())
        using (var s = (GraphicsPath)path.Clone())
        {
            m.Translate(0, 2 * K);
            s.Transform(m);
            g.FillPath(shadow, s);
        }
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using var pen = new Pen(border, K);
        g.DrawPath(pen, path);
    }

    static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>논리 px 크기의 글꼴(그릴 때는 <see cref="K"/> 배).</summary>
    static Font Px(string family, float size, FontStyle style) => new(family, size * K, style, GraphicsUnit.Pixel);

    /// <summary>글자 크기(논리 px). 여백 없는 형식으로 잰다.</summary>
    static SizeF Measure(Graphics g, string text, Font font)
    {
        var s = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        return new SizeF(s.Width / K, s.Height / K);
    }

    static void Text(Graphics g, string text, Font font, Color color, float x, float y)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x * K, y * K, StringFormat.GenericTypographic);
    }
}
