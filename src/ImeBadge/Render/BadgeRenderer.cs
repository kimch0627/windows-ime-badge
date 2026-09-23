using System;
using System.Collections.Generic;
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
/// 시안 비교용 렌더링 선택. 기본값(모두 끔)이 지금까지의 모습이다. <see cref="RenderSheet"/> 가 항목별로 켜서 비교 그림을 만든다.
/// 시안이 확정되면 고른 것만 기본 동작으로 옮기고 이 선택은 없앤다.
/// </summary>
/// <param name="FluentGloss">광택을 약한 그라데이션 + 안쪽 위 가장자리의 밝은 림(rim light)으로(Windows 11 Fluent 풍).</param>
/// <param name="DualBorder">테두리를 흰/검 반투명 대신 배지색을 같은 색조로 어둡게 한 선으로(<see cref="ColorHex.EdgeOn"/>).</param>
/// <param name="InkLayout">글자 위치와 배지 폭을 글꼴의 줄 높이 대신 실제 글자 모양(잉크) 경계로 맞춘다.</param>
/// <param name="LatinFont">영문 글자("a", "A", "?")를 그릴 글꼴. null 이면 한글과 같은 맑은 고딕.</param>
/// <param name="TextHalo">불투명도가 100% 미만이면 글자 둘레에 불투명한 배지색을 얇게 깔아, 뒤의 문서가 비쳐도 글자가 읽히게.</param>
readonly record struct RenderOptions(bool FluentGloss = false, bool DualBorder = false, bool InkLayout = false,
    string? LatinFont = null, bool TextHalo = false)
{
    public static readonly RenderOptions Legacy = default;
    public static readonly RenderOptions Improved = new(true, true, true, BadgeFonts.SegoeUI, true);
}

/// <summary>배지 글꼴 고르기. 한글이 든 글자는 맑은 고딕, 영문은 고른 글꼴(설치돼 있지 않으면 맑은 고딕).</summary>
static class BadgeFonts
{
    public const string SegoeUI = "Segoe UI", SegoeUISemibold = "Segoe UI Semibold";

    static readonly Dictionary<string, bool> _installed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 글자에 쓸 글꼴 이름과 굵기. "Segoe UI Semibold" 처럼 굵기가 이름에 든 글꼴은 Regular 로 쓴다(Bold 를 더하면 가짜로 두껍게 그린다).
    /// </summary>
    public static (string Family, FontStyle Style) For(string text, string? latin)
    {
        if (latin is null || HasHangul(text) || !IsInstalled(latin)) return (BadgeRenderer.FontFamily, FontStyle.Bold);
        return (latin, latin.EndsWith("Semibold", StringComparison.OrdinalIgnoreCase) ? FontStyle.Regular : FontStyle.Bold);
    }

    /// <summary>이 이름의 글꼴이 설치돼 있는가. 없으면 GDI+ 가 조용히 다른 글꼴로 바꿔 그리므로 미리 확인한다.</summary>
    public static bool IsInstalled(string name)
    {
        if (!_installed.TryGetValue(name, out bool ok))
        {
            try { using var f = new FontFamily(name); ok = true; }
            catch (ArgumentException) { ok = false; }
            _installed[name] = ok;
        }
        return ok;
    }

    static bool HasHangul(string text)
    {
        foreach (char c in text)
            if (c >= '\u1100') return true;
        return false;
    }
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

    /// <param name="options">시안 비교용(<see cref="RenderSheet"/>). 앱은 넘기지 않아 지금까지의 모습으로 그린다.</param>
    public static Bitmap Render(ImeState state, BadgeStyle style, float scale, in BadgeTheme theme, int opacityPercent = 100, bool capsLock = false,
        RenderOptions options = default)
    {
        var (text, color, bar) = Look(state, theme, capsLock);
        var fill = Color.FromArgb(Math.Clamp(255 * opacityPercent / 100, 30, 255), color);
        var ink = TextColorOn(color, theme.Finish);
        if (theme.Character.Length > 0 && style is BadgeStyle.Pill or BadgeStyle.Box)
            return RenderCharacter(theme.Character, text, bar, fill, ink, scale, theme, options);
        return style switch
        {
            BadgeStyle.Dot => RenderDot(fill, scale, theme, options),
            BadgeStyle.Underline => RenderUnderline(fill, scale),
            BadgeStyle.Box => RenderText(text, bar, fill, ink, scale, rounded: false, theme, options),
            _ => RenderText(text, bar, fill, ink, scale, rounded: true, theme, options),   // Pill, DotFlash(글자 단계)
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

    /// <summary>
    /// 채움색과 대비되는 얇은 테두리. 배경과 같은 색 위에 놓여도 윤곽이 남는다. 부드러운 테마는 더 옅게.
    /// 개선안(<see cref="RenderOptions.DualBorder"/>)은 배지색을 같은 색조로 어둡게 한 선. 불투명도를 낮춰도 윤곽은 어느 정도 남긴다.
    /// </summary>
    static Pen OutlinePen(Color fill, Color ink, float width, in BadgeTheme theme, in RenderOptions o)
    {
        if (o.DualBorder)
        {
            var edge = Color.FromArgb(ColorHex.EdgeOn(fill.ToArgb(), theme.Finish == BadgeFinish.Soft));
            return new(Color.FromArgb(Math.Max((int)fill.A, EdgeMinAlpha), edge), width);
        }
        return theme.Finish == BadgeFinish.Soft ? new(Color.FromArgb(44, ink), width) : new(Color.FromArgb(110, TextColorOn(fill)), width);
    }

    /// <summary>불투명도를 낮췄을 때 개선안 테두리의 최소 알파. 배지가 옅어져도 어디 있는지는 보이게.</summary>
    const int EdgeMinAlpha = 160;

    /// <summary>개선안(<see cref="RenderOptions.FluentGloss"/>)은 테마 광택 그라데이션을 이만큼 약하게 하고 대신 림을 더한다.</summary>
    const float FluentGlossFactor = 0.5f;

    /// <summary>채움 붓. 광택이 있으면 위쪽이 밝은 세로 그라데이션(젤리·사탕 같은 느낌).</summary>
    static Brush FillBrush(Color fill, RectangleF bounds, in BadgeTheme theme, in RenderOptions o)
    {
        float gloss = o.FluentGloss ? theme.Gloss * FluentGlossFactor : theme.Gloss;
        if (gloss <= 0f || bounds.Height <= 0) return new SolidBrush(fill);
        var top = Color.FromArgb(fill.A, Color.FromArgb(ColorHex.Mix(fill.ToArgb(), unchecked((int)0xFFFFFFFF), gloss)));
        var b = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2), top, fill, LinearGradientMode.Vertical);
        b.InterpolationColors = new ColorBlend { Colors = new[] { top, fill, fill }, Positions = new[] { 0f, 0.62f, 1f } };
        return b;
    }

    /// <summary>
    /// 안쪽 위 가장자리의 밝은 림(Fluent 의 윗면 하이라이트). 유리컵 윗면에 형광등이 한 줄 비친 것처럼, 배지 모양을 가운데 쪽으로
    /// <paramref name="inset"/> 만큼 줄인 윤곽을 위에서 아래로 사라지는 흰색으로 긋는다. 흐린 그라데이션보다 20px 배지에서 또렷하다.
    /// </summary>
    static void DrawRim(Graphics g, GraphicsPath path, RectangleF bounds, float inset, float width, Color fill, in BadgeTheme theme)
    {
        int alpha = (theme.Finish == BadgeFinish.Soft ? 96 : 72) * fill.A / 255;
        if (alpha <= 0 || bounds.Width <= 2 * inset || bounds.Height <= 2 * inset) return;
        using var rim = (GraphicsPath)path.Clone();
        using (var m = new Matrix())
        {
            float cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2;
            m.Translate(cx, cy);
            m.Scale((bounds.Width - 2 * inset) / bounds.Width, (bounds.Height - 2 * inset) / bounds.Height);
            m.Translate(-cx, -cy);
            rim.Transform(m);
        }
        var clear = Color.FromArgb(0, Color.White);
        using var brush = new LinearGradientBrush(new RectangleF(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2), Color.White, clear, LinearGradientMode.Vertical);
        brush.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(alpha, Color.White), clear, clear }, Positions = new[] { 0f, 0.55f, 1f } };
        using var pen = new Pen(brush, width) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, rim);
    }

    /// <summary>잉크 경계를 재는 글자 배치 형식. 여백을 덧붙이지 않는다. 앱이 끝날 때까지 쓰므로 해제하지 않는다.</summary>
    static readonly StringFormat Typographic = (StringFormat)StringFormat.GenericTypographic.Clone();

    /// <summary>원점(0,0)에 놓은 글자의 윤곽. 경계(GetBounds)가 곧 잉크 경계다.</summary>
    static GraphicsPath TextPath(string text, Font font)
    {
        var p = new GraphicsPath();
        p.AddString(text, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, Typographic);
        return p;
    }

    /// <summary>
    /// 개선안 글자 그리기: 잉크 중심을 (<paramref name="cx"/>, <paramref name="cy"/>)에 맞춘다. 윤곽은 위치를 재는 데만 쓰고
    /// 그리기는 DrawString 이라 힌팅(격자 맞춤)의 선명함이 그대로다. <paramref name="halo"/> 가 있으면 글자(와 밑줄) 둘레를 그 붓으로 먼저 굵게 긋는다.
    /// </summary>
    static void DrawInkText(Graphics g, string text, Font font, GraphicsPath glyph, float cx, float cy, bool snap,
        Brush textBrush, RectangleF? bar, Brush? halo, float haloWidth)
    {
        var ink = glyph.GetBounds();
        var origin = new PointF(cx - (ink.X + ink.Width / 2), cy - (ink.Y + ink.Height / 2));
        if (snap) origin = new PointF(MathF.Round(origin.X), MathF.Round(origin.Y));
        if (halo is not null)
        {
            using var outline = (GraphicsPath)glyph.Clone();
            using (var m = new Matrix()) { m.Translate(origin.X, origin.Y); outline.Transform(m); }
            if (bar is { } b) outline.AddRectangle(b);
            using var pen = new Pen(halo, haloWidth) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, outline);
        }
        if (bar is { } r) g.FillRectangle(textBrush, r);
        g.DrawString(text, font, textBrush, origin, Typographic);
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

    static Bitmap RenderDot(Color color, float scale, in BadgeTheme theme, in RenderOptions o)
    {
        int d = (int)Math.Round(9 * scale);
        int pad = ShadowPad(scale);
        var bmp = NewCanvas(d + 2 * pad, d + 2 * pad + pad, out var g);
        using (g)
        {
            var rect = new RectangleF(pad, pad, d, d);
            using var path = new GraphicsPath();
            path.AddEllipse(rect);
            DrawShadow(g, path, scale, color, theme);
            using var brush = FillBrush(color, rect, theme, o);
            float edgeWidth = Math.Max(1f, scale);
            using var pen = OutlinePen(color, TextColorOn(color, theme.Finish), edgeWidth, theme, o);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
            if (o.FluentGloss)
            {
                float rimWidth = Math.Max(1f, scale);
                DrawRim(g, path, rect, edgeWidth / 2 + rimWidth / 2, rimWidth, color, theme);
            }
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

    /// <summary>개선안 배지 폭: 잉크 폭 + 좌우 여백(배율 1 기준 px). 폭이 높이보다 이만큼도 크지 않으면 동그라미로 맞춘다.</summary>
    const float InkSidePad = 7f, CircleSnap = 3f;

    /// <summary>개선안 헤일로 굵기(배율 1 기준 px). 글자 획 양쪽으로 절반씩 나온다.</summary>
    const float HaloWidth = 2.4f;

    static Bitmap RenderText(string text, bool capsBar, Color color, Color ink, float scale, bool rounded, in BadgeTheme theme, in RenderOptions o)
    {
        float fontPx = 13 * scale;
        var (family, fontStyle) = BadgeFonts.For(text, o.LatinFont);
        using var font = new Font(family, fontPx, fontStyle, GraphicsUnit.Pixel);   // 없으면 GDI+ 가 기본 글꼴로 대체
        using var glyph = o.InkLayout ? TextPath(text, font) : null;

        int h = (int)Math.Round(20 * scale);
        int w;
        SizeF ts = default;
        if (glyph is not null)
        {
            float inkW = glyph.GetBounds().Width;
            w = (int)Math.Round(Math.Max(h, inkW + 2 * InkSidePad * scale));
            if (w < h + CircleSnap * scale) w = h;
        }
        else
        {
            ts = Measure(text, font);
            w = (int)Math.Round(Math.Max(h, ts.Width + 10 * scale));
        }
        int pad = ShadowPad(scale);   // 사방 여백 + 아래쪽에 그림자가 떨어질 자리
        var bmp = NewCanvas(w + 2 * pad, h + 2 * pad + pad, out var g);
        using (g)
        {
            var rect = new RectangleF(pad + 0.5f, pad + 0.5f, w - 1, h - 1);
            using var path = RoundedRect(rect, rounded ? (h - 1) / 2f : 3 * scale);
            DrawShadow(g, path, scale, color, theme);
            using var brush = FillBrush(color, rect, theme, o);
            using var pen = OutlinePen(color, ink, 1f, theme, o);
            using var textBrush = new SolidBrush(ink);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
            if (o.FluentGloss)
            {
                float rimWidth = Math.Max(1f, scale);
                DrawRim(g, path, rect, 0.5f + rimWidth / 2, rimWidth, color, theme);
            }

            if (glyph is not null)
            {
                // 잉크 기준: 글자(와 Caps Lock 밑줄)를 한 덩어리로 보고 배지 가운데에 둔다.
                var ib = glyph.GetBounds();
                float cx = pad + w / 2f, cy = pad + h / 2f;
                RectangleF? bar = null;
                if (capsBar)
                {
                    float gap = 1.5f * scale, barH = Math.Max(1f, 1.5f * scale), barW = Math.Max(6 * scale, Math.Min(ib.Width, w - 8 * scale));
                    cy -= (gap + barH) / 2;
                    bar = new RectangleF(cx - barW / 2, MathF.Round(cy + ib.Height / 2 + gap), barW, barH);
                }
                using var halo = HaloBrush(color, rect, theme, o);
                DrawInkText(g, text, font, glyph, cx, cy, snap: true, textBrush, bar, halo, HaloWidth * scale);
                return bmp;
            }

            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var textArea = new RectangleF(pad, pad, w, h);
            RectangleF? capsRect = null;
            if (capsBar)
            {
                // Caps Lock: 글자를 조금 올리고 그 아래에 짧은 밑줄(키보드의 Caps Lock 표시등을 닮은 모양).
                float lift = 1.5f * scale, barH = Math.Max(1f, 1.5f * scale), barW = Math.Max(6 * scale, Math.Min(ts.Width * 0.8f, w - 8 * scale));
                textArea.Offset(0, -lift);
                capsRect = new RectangleF(pad + (w - barW) / 2, pad + h - 4.5f * scale, barW, barH);
            }
            using (var halo = HaloBrush(color, rect, theme, o))
                if (halo is not null) DrawHalo(g, text, font, textArea, sf, capsRect, halo, HaloWidth * scale);
            if (capsRect is { } cr) g.FillRectangle(textBrush, cr);
            g.DrawString(text, font, textBrush, textArea, sf);
        }
        return bmp;
    }

    /// <summary>헤일로 붓: 불투명도를 뺀 배지 채움(광택 그라데이션 포함). 불투명도 100% 면 바탕과 같아 보이지 않으므로 null.</summary>
    static Brush? HaloBrush(Color fill, RectangleF bounds, in BadgeTheme theme, in RenderOptions o) =>
        o.TextHalo && fill.A < 255 ? FillBrush(Color.FromArgb(255, fill), bounds, theme, o) : null;

    /// <summary>줄 높이 기준(지금까지의) 배치에 헤일로만 더할 때. DrawString 과 같은 칸·형식으로 윤곽을 만들어 굵게 긋는다.</summary>
    static void DrawHalo(Graphics g, string text, Font font, RectangleF area, StringFormat sf, RectangleF? bar, Brush halo, float width)
    {
        using var outline = new GraphicsPath();
        outline.AddString(text, font.FontFamily, (int)font.Style, font.Size, area, sf);
        if (bar is { } b) outline.AddRectangle(b);
        using var pen = new Pen(halo, width) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, outline);
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

    static Bitmap RenderCharacter(string character, string text, bool capsBar, Color color, Color ink, float scale, in BadgeTheme theme, in RenderOptions o)
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
            using var brush = FillBrush(color, bounds, theme, o);

            DrawShadow(g, body, scale, color, theme, penScale: scale);
            // 테두리를 먼저 굵게 그리고 그 위를 채우면 바깥 절반만 남아, 겹친 원(구름)의 안쪽 선이 보이지 않는다.
            using (var edge = OutlinePen(color, ink, 1.6f, theme, o)) { edge.LineJoin = LineJoin.Round; if (star) edge.Width = 4f; g.DrawPath(edge, body); }
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
            // 림: 테두리의 바깥 절반(0.8 단위) 안쪽에. 별은 둥근 선(2.4)이 몸통 밖으로 1.2 나와 있어 그만큼 바깥쪽에 긋는다.
            if (o.FluentGloss) DrawRim(g, body, body.GetBounds(), star ? -0.6f : 0.6f, 1f, color, theme);

            var (family, fontStyle) = BadgeFonts.For(text, o.LatinFont);
            using var font = new Font(family, fig.FontSize, fontStyle, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(ink);
            using var halo = HaloBrush(color, bounds, theme, o);
            if (o.InkLayout)
            {
                // 잉크 기준: 글자(와 Caps Lock 밑줄)의 한 덩어리 중심을 몸통의 글자 자리(TextX, TextY)에.
                using var glyph = TextPath(text, font);
                var ib = glyph.GetBounds();
                float cy = fig.TextY;
                RectangleF? inkBar = null;
                if (capsBar)
                {
                    const float Gap = 1.2f, BarH = 1.4f;
                    float barW = Math.Max(fig.FontSize * 0.5f, ib.Width);
                    cy -= (Gap + BarH) / 2;
                    inkBar = new RectangleF(fig.TextX - barW / 2, cy + ib.Height / 2 + Gap, barW, BarH);
                }
                DrawInkText(g, text, font, glyph, fig.TextX, cy, snap: false, textBrush, inkBar, halo, HaloWidth);
                return bmp;
            }

            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            float ty = fig.TextY;
            RectangleF? capsRect = null;
            if (capsBar)
            {
                ty -= 1.4f;
                float barW = fig.FontSize * 0.75f;
                capsRect = new RectangleF(fig.TextX - barW / 2, ty + fig.FontSize * 0.5f + 0.6f, barW, 1.4f);
            }
            var area = new RectangleF(fig.TextX - fig.Width / 2, ty - fig.FontSize, fig.Width, fig.FontSize * 2);
            if (halo is not null) DrawHalo(g, text, font, area, sf, capsRect, halo, HaloWidth);
            if (capsRect is { } cr) g.FillRectangle(textBrush, cr);
            g.DrawString(text, font, textBrush, area, sf);
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
