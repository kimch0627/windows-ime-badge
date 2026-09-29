using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;

namespace ImeBadge;

/// <summary>
/// README 맨 위의 소개 그림(docs/images/hero.png). <c>ImeBadge.exe --render-hero [경로]</c> 로 실행하면 창 없이 그림만 저장하고 끝낸다.
/// 배지는 앱과 같은 렌더러(<see cref="BadgeRenderer"/>)로 그리고 caret 옆 자리도 앱과 같은 계산(<see cref="BadgeLayout"/>)으로 정해서,
/// 손으로 그린 예시 그림과 달리 실제 모습 그대로다. 모습이 바뀌면 이 스위치로 다시 만든다(CI 가 롤링 사전 릴리스에 hero.png 로 붙인다).
/// 다시 그릴 때 diff 가 생기지 않도록 버전·날짜처럼 매번 바뀌는 글자는 넣지 않는다.
/// </summary>
static class RenderHero
{
    public const string Switch = "--render-hero";

    public static bool TryRun(string[] args) => RenderSheet.Run(args, Switch, "hero.png", Draw);

    /// <summary>그림 배율. README 는 절반 크기로 보여 주므로 고해상도 화면에서도 또렷하다.</summary>
    const float K = 2f;

    const float Margin = 48 * K, CardGap = 15 * K, CardH = 172 * K, Radius = 12 * K;
    const int Width = 1200 * (int)K;

    static readonly Color Page = Color.FromArgb(0xFA, 0xFA, 0xFC), Ink = Color.FromArgb(0x1B, 0x1B, 0x1F),
        Subtle = Color.FromArgb(0x8A, 0x8A, 0x99), Body = Color.FromArgb(0x5F, 0x60, 0x68), Line = Color.FromArgb(0xEC, 0xEC, 0xF0);

    /// <summary>아래 줄 한 칸: 입력 중인 글자, 배지(캐릭터·Caps Lock·Shift·밑줄), 그 아래 짧은 설명.</summary>
    readonly record struct Sample(string Typed, ImeState State, DesignTheme Theme, string Caption,
        string Character = "", bool Caps = false, bool Shift = false, BadgeStyle Style = BadgeStyle.Pill);

    static readonly Sample[] Samples =
    {
        new("가나다", ImeState.Hangul, DesignThemes.Blossom, "고양이", Character: BadgeCharacters.Cat),
        new("abc", ImeState.English, DesignThemes.Mint, "강아지", Character: BadgeCharacters.Dog),
        new("가나다", ImeState.Hangul, DesignThemes.Candy, "하트", Character: BadgeCharacters.Heart),
        new("abc", ImeState.English, DesignThemes.Blossom, "구름 · Caps Lock", Character: BadgeCharacters.Cloud, Caps: true),
        new("가나다", ImeState.Hangul, DesignThemes.Midnight, "별 · Caps Lock", Character: BadgeCharacters.Star, Caps: true),
        new("abc", ImeState.English, DesignThemes.Classic, "Shift 누름", Shift: true),
        new("한글", ImeState.Hangul, DesignThemes.Classic, "밑줄 모양", Style: BadgeStyle.Underline),
    };

    static readonly (string Id, string Name)[] Themes =
    {
        (DesignThemes.ClassicId, "클래식"), ("blossom", "벚꽃"), ("candy", "캔디"), ("mint", "민트초코"), ("midnight", "미드나잇"),
    };

    static Bitmap Draw()
    {
        float cardsTop = 145 * K, rowTop = cardsTop + CardH + 22 * K, rowH = 120 * K;
        int height = (int)(rowTop + rowH + 40 * K);
        var bmp = new Bitmap(Width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;   // ClearType 는 PNG 에서 색 번짐이 남는다
        g.Clear(Page);

        // 제목
        using (var title = new Font(BadgeFonts.For("ImeBadge"), 32 * K, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var sub = new Font(BadgeRenderer.FontFamily, 18 * K, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var body = new Font(BadgeRenderer.FontFamily, 14 * K, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            float w = Text(g, "ImeBadge", title, Ink, Margin, 38 * K);
            Text(g, "한/영 입력 상태 배지", sub, Subtle, Margin + w + 12 * K, 50 * K);
            Text(g, "글자를 치기 전에, 커서 옆 작은 배지로 지금 한글인지 영문인지 알려 줘요. 테마와 캐릭터로 내 취향대로.", body, Body, Margin, 92 * K);
        }

        // 테마 카드 5개: 한글·영문 줄마다 caret 과 앱과 같은 자리의 배지
        float cardW = (Width - 2 * Margin - (Themes.Length - 1) * CardGap) / Themes.Length;
        using (var name = new Font(BadgeRenderer.FontFamily, 13 * K, FontStyle.Bold, GraphicsUnit.Pixel))
            for (int i = 0; i < Themes.Length; i++)
            {
                var design = DesignThemes.Get(Themes[i].Id);
                var card = new RectangleF(Margin + i * (cardW + CardGap), cardsTop, cardW, CardH);
                var p = design.Light;
                Card(g, card, Color.FromArgb(p.Window), Color.FromArgb(p.Border));
                var theme = BadgeTheme.Of(design);
                var text = Color.FromArgb(p.Text);
                Typed(g, "안녕하세요", card.X + 16 * K, card.Y + 40 * K, text, ImeState.Hangul, theme);
                Typed(g, "hello", card.X + 16 * K, card.Y + 98 * K, text, ImeState.English, theme);   // 윗줄과 배지 한 개 높이만큼 띄운다
                Text(g, Themes[i].Name, name, text, card.X + 16 * K, card.Bottom - 30 * K);
            }

        // 아래 줄: 캐릭터 · Caps Lock · Shift · 밑줄
        var row = new RectangleF(Margin, rowTop, Width - 2 * Margin, rowH);
        Card(g, row, Color.White, Line);
        using (var label = new Font(BadgeRenderer.FontFamily, 13 * K, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var caption = new Font(BadgeRenderer.FontFamily, 10 * K, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            Text(g, "캐릭터 · Caps Lock · Shift", label, Body, row.X + 22 * K, row.Y + row.Height / 2 - 9 * K);
            float x0 = row.X + 205 * K, slot = (row.Right - 16 * K - x0) / Samples.Length;
            for (int i = 0; i < Samples.Length; i++)
            {
                var s = Samples[i];
                float x = x0 + i * slot;
                var theme = BadgeTheme.Of(s.Theme) with { Character = s.Character };
                Typed(g, s.Typed, x, row.Y + 44 * K, Ink, s.State, theme, s.Style, s.Caps, s.Shift);
                Text(g, s.Caption, caption, Subtle, x, row.Bottom - 26 * K);
            }
        }

        using (var foot = new Font(BadgeRenderer.FontFamily, 10 * K, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            const string note = "앱의 배지 렌더러로 그린 그림 · 배율 200%";
            var size = g.MeasureString(note, foot, PointF.Empty, StringFormat.GenericTypographic);
            Text(g, note, foot, Subtle, Width - Margin - size.Width, height - 26 * K);
        }
        return bmp;
    }

    /// <summary>
    /// 입력 중인 글자 + 글자 끝의 caret + 배지. 배지는 앱처럼 caret 오른쪽 위(<see cref="BadgePlacement.AboveRight"/>)에,
    /// 밑줄 모양은 caret 바로 아래에 <see cref="BadgeLayout.Compute"/> 로 놓는다.
    /// </summary>
    static void Typed(Graphics g, string typed, float x, float y, Color ink, ImeState state, in BadgeTheme theme,
        BadgeStyle style = BadgeStyle.Pill, bool caps = false, bool shift = false)
    {
        using var font = new Font(typed.Any(c => c >= 'ᄀ') ? BadgeRenderer.FontFamily : BadgeFonts.For(typed), 17 * K,
            FontStyle.Regular, GraphicsUnit.Pixel);
        float w = Text(g, typed, font, ink, x, y);
        var caret = new Rectangle((int)Math.Round(x + w + 2 * K), (int)Math.Round(y - 1 * K), (int)K, (int)Math.Round(23 * K));
        using (var brush = new SolidBrush(ink)) g.FillRectangle(brush, caret);

        using var badge = BadgeRenderer.Render(state, style, K, theme, 100, caps, shift);
        var pos = BadgeLayout.Compute(new LayoutInput(caret, badge.Size, style, BadgePlacement.AboveRight, K,
            new Rectangle(0, 0, Width, int.MaxValue / 2)));
        g.DrawImage(badge, new Rectangle(pos, badge.Size));
    }

    static void Card(Graphics g, RectangleF r, Color fill, Color border)
    {
        using var path = new GraphicsPath();
        float d = 2 * Radius;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border, K);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }

    /// <summary>글자를 그리고 그 폭을 돌려준다(여백 없는 GenericTypographic 기준).</summary>
    static float Text(Graphics g, string text, Font font, Color color, float x, float y)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic);
        return g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width;
    }
}
