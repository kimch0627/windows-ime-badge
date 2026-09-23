using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace ImeBadge;

/// <summary>
/// 개발용 배지 확인 그림 한 장(PNG). <c>ImeBadge.exe --render-sheet [경로]</c> 로 실행하면 창·트레이 없이 그림만 저장하고 끝낸다.
/// 렌더러는 GDI+(Windows 전용)라 Linux 에서 도는 단위 테스트로는 모습을 확인할 수 없어, CI 의 Windows 러너가 이 그림을 만들어
/// 롤링 사전 릴리스에 붙인다(build.yml). 모든 테마·모양·캐릭터, 불투명도, 배율, 트레이 아이콘, 한/영 색의 흑백 구분을 한눈에 본다.
/// </summary>
static class RenderSheet
{
    public const string Switch = "--render-sheet";

    /// <summary><see cref="Switch"/> 가 있으면 그림을 저장하고 true. 실패하면 로그를 남기고 종료 코드 1.</summary>
    public static bool TryRun(string[] args)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        string path = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : "badge-sheet.png";
        try
        {
            path = Path.GetFullPath(path);
            using var sheet = Draw();
            sheet.Save(path, ImageFormat.Png);
            Log.Write($"render-sheet: {path} ({sheet.Width}x{sheet.Height})");
        }
        catch (Exception ex)
        {
            Log.Error("render-sheet failed", ex);
            Environment.ExitCode = 1;
        }
        return true;
    }

    static readonly Dictionary<string, string> ThemeNames = new()
    {
        [DesignThemes.ClassicId] = "클래식", ["blossom"] = "벚꽃", ["candy"] = "캔디", ["mint"] = "민트초코", ["midnight"] = "미드나잇",
    };

    /// <summary>한 칸에 그릴 배지.</summary>
    readonly record struct Item(string Caption, ImeState State, BadgeStyle Style = BadgeStyle.Pill, bool Caps = false, string Character = "");

    static readonly Item Han = new("한", ImeState.Hangul), EnA = new("a", ImeState.English),
        EnCaps = new("A Caps", ImeState.English, Caps: true), HanCaps = new("꺆 Caps", ImeState.Hangul, Caps: true);

    static readonly Item[] Gallery =
    {
        Han, EnA, EnCaps, HanCaps,
        new("상자", ImeState.Hangul, BadgeStyle.Box), new("점", ImeState.Hangul, BadgeStyle.Dot), new("?", ImeState.OtherLang),
        new("고양이", ImeState.Hangul, Character: BadgeCharacters.Cat), new("강아지", ImeState.English, Character: BadgeCharacters.Dog),
        new("하트", ImeState.Hangul, Character: BadgeCharacters.Heart), new("구름 Caps", ImeState.English, Caps: true, Character: BadgeCharacters.Cloud),
        new("별 Caps", ImeState.Hangul, Caps: true, Character: BadgeCharacters.Star),
    };

    static readonly Color Light = Color.White, Dark = Color.FromArgb(0x20, 0x20, 0x20);

    static Bitmap Draw()
    {
        using var s = new Sheet(2200, 9000);
        const float Main = 1.5f;   // 125~150% 배율 노트북이 흔하다
        var themes = DesignThemes.All;

        s.Title("ImeBadge 배지 모습");
        s.Note($"버전 {AppVersion.Display} · {DateTime.Now:yyyy-MM-dd HH:mm} · {Environment.OSVersion.VersionString}");
        s.Note("글꼴: " + string.Join(" · ", new[] { BadgeRenderer.FontFamily, BadgeFonts.Latin }
            .Select(f => $"{f} {(BadgeFonts.IsInstalled(f) ? "있음" : "없음")}")));

        // 1. 테마별 모양
        s.Section("1. 테마별 모양", "배율 150%, 불투명도 100%. Caps = Caps Lock 켜짐, ? = 다른 언어.");
        float slot = 54;
        var gallerySlots = Gallery.Select(i => i.Caption).ToArray();
        s.Headers(new[] { ("밝은 배경", gallerySlots, slot), ("어두운 배경", gallerySlots, slot) });
        foreach (var design in themes)
        {
            var theme = BadgeTheme.Of(design);
            s.Row(ThemeNames[design.Id], new[] { Panel(Light, slot, Gallery, theme, Main, 100), Panel(Dark, slot, Gallery, theme, Main, 100) });
        }

        // 2. 불투명도
        s.Section("2. 불투명도와 글자 가독성", "배율 150%. 배지를 글 위에 겹쳐 그렸다. '같은 색' = 배지와 같은 색의 선택 영역 위.");
        var pair = new[] { Han, EnA };
        var pairSlots = pair.Select(i => i.Caption).ToArray();
        var opacities = new[] { 100, 60, 30 };
        s.Headers(opacities.SelectMany(op => new[] { ($"{op}% 흰 문서", pairSlots, slot), ($"{op}% 어두운", pairSlots, slot), ($"{op}% 같은 색", pairSlots, slot) }).ToArray());
        foreach (var design in themes)
        {
            var theme = BadgeTheme.Of(design);
            s.Row(ThemeNames[design.Id], opacities.SelectMany(op => new[]
            {
                Panel(Light, slot, pair, theme, Main, op, backdrop: Color.FromArgb(0x30, 0x30, 0x30)),
                Panel(Dark, slot, pair, theme, Main, op, backdrop: Color.FromArgb(0xE0, 0xE0, 0xE0)),
                Panel(theme.Hangul, slot, pair, theme, Main, op, backdrop: Color.White),
            }).ToArray());
        }

        // 3. 크기
        s.Section("3. 크기(배율)", "흰 배경. 마지막 칸은 배율 100% 를 3배로 확대(픽셀 그대로)한 것 — 림·테두리가 뭉개지는지 본다.");
        var scales = new[] { 1f, 1.25f, 1.5f, 2f };
        var sizeItems = new[] { Han, EnA, HanCaps };
        var sizeSlots = sizeItems.Select(i => i.Caption).ToArray();
        s.Headers(scales.Select(k => ($"{k * 100:0}%", sizeSlots, 36 * k)).Append(("100% ×3 확대", pairSlots, 110f)).ToArray());
        foreach (var design in themes)
        {
            var theme = BadgeTheme.Of(design);
            var panels = scales.Select(k => Panel(Light, 36 * k, sizeItems, theme, k, 100)).ToList();
            panels.Add(new SheetPanel(Light, 110, pair.Select(i => Zoom(RenderItem(i, theme, 1f, 100), 3)).ToList()));
            s.Row(ThemeNames[design.Id], panels);
        }

        // 4. 트레이 아이콘
        s.Section("4. 트레이 아이콘", "100% 배율은 16px, 150% 는 24px. 마지막 칸은 16px 을 4배로 확대한 것.");
        var trayStates = new[] { ImeState.Hangul, ImeState.English, ImeState.OtherLang };
        var traySlots = new[] { "한", "A", "?" };
        var traySizes = new[] { 16, 20, 24, 32 };
        s.Headers(traySizes.Select(px => ($"{px}px", traySlots, 40f))
            .Append(("24px 어두운 작업 표시줄", traySlots, 40f)).Append(("16px ×4 확대", traySlots, 72f)).ToArray());
        foreach (var design in themes)
        {
            var theme = BadgeTheme.Of(design);
            var panels = traySizes.Select(px => new SheetPanel(Light, 40, trayStates.Select(st => TrayIcons.RenderState(st, px, theme)).ToList())).ToList();
            panels.Add(new SheetPanel(Dark, 40, trayStates.Select(st => TrayIcons.RenderState(st, 24, theme)).ToList()));
            panels.Add(new SheetPanel(Light, 72, trayStates.Select(st => Zoom(TrayIcons.RenderState(st, 16, theme), 4)).ToList()));
            s.Row(ThemeNames[design.Id], panels);
        }

        // 5. 한/영 색 구분
        s.Section("5. 한/영 색 구분 — 흑백으로 보면", "두 기본색의 휘도 대비가 1.5 이상이면 흑백·색약으로 봐도 밝기로 구별된다(테스트로 확인).");
        s.Headers(new[] { ("색", pairSlots, 54f), ("흑백", pairSlots, 54f) });
        foreach (var design in themes)
        {
            var theme = BadgeTheme.Of(design);
            double ratio = ColorHex.ContrastRatio(theme.Hangul.ToArgb(), theme.English.ToArgb());
            s.Row(ThemeNames[design.Id], new[]
            {
                Panel(Light, 54, pair, theme, Main, 100),
                new SheetPanel(Light, 54, pair.Select(i => Gray(RenderItem(i, theme, Main, 100))).ToList()),
            }, $"{design.HangulColor} / {design.EnglishColor} · 휘도 대비 {ratio:0.00}");
        }

        return s.Finish();
    }

    static Bitmap RenderItem(in Item it, BadgeTheme theme, float scale, int opacity) =>
        BadgeRenderer.Render(it.State, it.Style, scale, theme with { Character = it.Character }, opacity, it.Caps);

    static SheetPanel Panel(Color bg, float slot, IEnumerable<Item> items, BadgeTheme theme, float scale, int opacity, Color? backdrop = null) =>
        new(bg, slot, items.Select(i => RenderItem(i, theme, scale, opacity)).ToList(), backdrop);

    /// <summary>픽셀을 그대로 k 배로 키운다(nearest-neighbor). 1px 선이 뭉개졌는지 보려고.</summary>
    static Bitmap Zoom(Bitmap src, int k)
    {
        using (src)
        {
            var bmp = new Bitmap(src.Width * k, src.Height * k, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height));
            return bmp;
        }
    }

    /// <summary>흑백(사람 눈이 느끼는 밝기 가중치)으로 바꾼다. 색을 빼고 밝기만 남았을 때 한/영이 구별되는지 보려고.</summary>
    static Bitmap Gray(Bitmap src)
    {
        using (src)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            using var attr = new ImageAttributes();
            attr.SetColorMatrix(new ColorMatrix(new[]
            {
                new[] { 0.2126f, 0.2126f, 0.2126f, 0, 0 },
                new[] { 0.7152f, 0.7152f, 0.7152f, 0, 0 },
                new[] { 0.0722f, 0.0722f, 0.0722f, 0, 0 },
                new[] { 0f, 0, 0, 1, 0 },
                new[] { 0f, 0, 0, 0, 1 },
            }));
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
            return bmp;
        }
    }

    /// <summary>배경 판 하나: 배지들을 같은 폭의 칸에 하나씩 가운데 맞춰 놓는다. <see cref="Backdrop"/> 이 있으면 그 색의 글 위에 겹친다.</summary>
    sealed record SheetPanel(Color Bg, float Slot, List<Bitmap> Items, Color? Backdrop = null)
    {
        public float Width => Items.Count * Slot + 2 * Sheet.PanelPad;
    }

    /// <summary>위에서 아래로 쌓아 그리는 큰 그림. 다 그리면 쓴 만큼만 잘라 낸다.</summary>
    sealed class Sheet : IDisposable
    {
        public const float Margin = 28, LabelWidth = 250, PanelPad = 8, PanelGap = 10;

        static readonly Color Ink = Color.FromArgb(0x1B, 0x1B, 0x1B), Subtle = Color.FromArgb(0x5F, 0x5F, 0x5F);

        readonly Bitmap _bmp;
        readonly Graphics _g;
        readonly Font _title = new(BadgeRenderer.FontFamily, 24, FontStyle.Bold, GraphicsUnit.Pixel);
        readonly Font _section = new(BadgeRenderer.FontFamily, 17, FontStyle.Bold, GraphicsUnit.Pixel);
        readonly Font _label = new(BadgeRenderer.FontFamily, 13, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly Font _small = new(BadgeRenderer.FontFamily, 11, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly Font _backdrop = new(BadgeRenderer.FontFamily, 15, FontStyle.Regular, GraphicsUnit.Pixel);
        float _y = Margin, _maxX;

        public Sheet(int width, int height)
        {
            _bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            _g = Graphics.FromImage(_bmp);
            _g.SmoothingMode = SmoothingMode.AntiAlias;
            _g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _g.Clear(Color.FromArgb(0xF3, 0xF3, 0xF3));
        }

        public void Title(string text) { Text(text, _title, Ink, Margin, _y); _y += 36; }

        public void Note(string text) { Text(text, _label, Subtle, Margin, _y); _y += 20; }

        public void Space(float h) => _y += h;

        public void Section(string title, string note)
        {
            _y += 26;
            Text(title, _section, Ink, Margin, _y);
            _y += 26;
            Text(note, _label, Subtle, Margin, _y);
            _y += 24;
        }

        /// <summary>판마다 제목 한 줄과 칸 이름 한 줄.</summary>
        public void Headers(IReadOnlyList<(string Title, string[] Slots, float Slot)> panels)
        {
            float x = Margin + LabelWidth;
            foreach (var (title, slots, slot) in panels)
            {
                Text(title, _small, Ink, x + PanelPad, _y);
                for (int i = 0; i < slots.Length; i++)
                {
                    var size = _g.MeasureString(slots[i], _small);
                    Text(slots[i], _small, Subtle, x + PanelPad + i * slot + (slot - size.Width) / 2, _y + 15);
                }
                x += slots.Length * slot + 2 * PanelPad + PanelGap;
            }
            _maxX = Math.Max(_maxX, x);
            _y += 34;
        }

        /// <summary>왼쪽에 이름, 오른쪽으로 판들. 판 안의 배지 비트맵은 그린 뒤 해제한다.</summary>
        public void Row(string label, IReadOnlyList<SheetPanel> panels, string? trailing = null)
        {
            float itemsH = panels.SelectMany(p => p.Items).Select(b => (float)b.Height).DefaultIfEmpty(20).Max();
            float h = itemsH + 2 * PanelPad;
            var labelSize = _g.MeasureString(label, _label);
            Text(label, _label, Ink, Margin, _y + (h - labelSize.Height) / 2);

            float x = Margin + LabelWidth;
            foreach (var p in panels)
            {
                var r = new RectangleF(x, _y, p.Width, h);
                using (var path = RoundRect(r, 6))
                using (var bg = new SolidBrush(p.Bg))
                    _g.FillPath(bg, path);
                if (p.Backdrop is { } ink)
                {
                    var state = _g.Save();
                    _g.SetClip(r);
                    using var brush = new SolidBrush(ink);
                    var sz = _g.MeasureString("가", _backdrop);
                    _g.DrawString("가나다라마 abcde 바사아자 fghij", _backdrop, brush, x + 2, _y + (h - sz.Height) / 2);
                    _g.Restore(state);
                }
                for (int i = 0; i < p.Items.Count; i++)
                {
                    using var b = p.Items[i];
                    float bx = x + PanelPad + i * p.Slot + (p.Slot - b.Width) / 2, by = _y + PanelPad + (itemsH - b.Height) / 2;
                    _g.DrawImage(b, new Rectangle((int)Math.Round(bx), (int)Math.Round(by), b.Width, b.Height));
                }
                x += p.Width + PanelGap;
            }
            if (trailing is not null)
            {
                var size = _g.MeasureString(trailing, _label);
                Text(trailing, _label, Ink, x + 4, _y + (h - size.Height) / 2);
                x += size.Width + 8;
            }
            _maxX = Math.Max(_maxX, x);
            _y += h + 6;
        }

        /// <summary>쓴 부분만 잘라 새 비트맵으로.</summary>
        public Bitmap Finish()
        {
            int w = (int)Math.Min(_bmp.Width, Math.Ceiling(_maxX + Margin)), h = (int)Math.Min(_bmp.Height, Math.Ceiling(_y + Margin));
            return _bmp.Clone(new Rectangle(0, 0, w, h), _bmp.PixelFormat);
        }

        void Text(string text, Font font, Color color, float x, float y)
        {
            using var brush = new SolidBrush(color);
            _g.DrawString(text, font, brush, x, y);
        }

        static GraphicsPath RoundRect(RectangleF r, float radius)
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

        public void Dispose()
        {
            _g.Dispose();
            _bmp.Dispose();
            _title.Dispose();
            _section.Dispose();
            _label.Dispose();
            _small.Dispose();
            _backdrop.Dispose();
        }
    }
}
