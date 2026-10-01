using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImeBadge;

// ─────────────────────────────────────────────────────────────────
// 설정 창의 뼈대 컨트롤. 모양은 Windows 11 설정 앱을 따른다.
//   ┌ NavPane ─────┬ PageHeader ───────────────────────────┐
//   │ 아이콘 이름   │ 큰 제목 / 한 줄 설명                    │
//   │ NavList      ├ CardStack (스크롤) ────────────────────┤
//   │  ▌모양        │ SectionHeader                          │
//   │   표시        │ SettingsCard [아이콘 제목·설명   컨트롤] │
//   │   ...         │ SettingsCard [...              ]       │
//   │ 버전          ├ CommandBar ─────────── [확인] [취소] ──┤
//   └──────────────┴────────────────────────────────────────┘
// 모든 크기는 96 DPI 기준 논리 픽셀이고, 배치할 때 지금 창의 DPI 로 곱한다(Px).
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// 설정 창 아이콘 글리프. Windows 11 의 Segoe Fluent Icons 와 Windows 10 의 Segoe MDL2 Assets 는 같은 코드에 같은 뜻의 모양이 있다.
/// </summary>
static class Glyphs
{
    public const string Appearance = "\uE790";   // Color
    public const string Display = "\uE7B3";      // RedEye
    public const string General = "\uE713";      // Setting
    public const string Apps = "\uE71D";         // AllApps
    public const string Info = "\uE946";         // Info
    public const string Preview = "\uE8A1";      // Preview
    public const string Theme = "\uE771";        // Personalize
    public const string Shape = "\uE76E";        // Emoji2
    public const string Position = "\uE707";     // MapPin
    public const string Size = "\uE8E9";         // FontSize
    public const string Opacity = "\uE706";      // Brightness
    public const string Color = "\uE790";        // Color
    public const string Visibility = "\uE7B3";   // RedEye
    public const string Animation = "\uE945";    // LightningBolt
    public const string FullScreen = "\uE740";   // FullScreen
    public const string CapsLock = "\uE8D2";     // Font
    public const string Shift = "\uE752";        // ScrollUpDown
    public const string Tray = "\uE7F4";         // TVMonitor
    public const string Power = "\uE7E8";        // PowerButton
    public const string Keyboard = "\uE765";     // KeyboardClassic
    public const string Language = "\uE774";     // Globe
    public const string Clock = "\uE823";        // Recent
    public const string Update = "\uE895";       // Sync
    public const string Save = "\uE74E";         // Save
    public const string Reset = "\uE72C";        // Refresh
    public const string Download = "\uE896";     // Download
    public const string Link = "\uE71B";         // Link
    public const string Folder = "\uE8B7";       // Folder
    public const string Copy = "\uE8C8";         // Copy
    public const string Add = "\uE710";          // Add
    public const string OpenExternal = "\uE8A7"; // OpenInNewWindow
}

/// <summary>
/// 왼쪽 탐색 목록(Windows 11 NavigationView). 항목마다 아이콘과 이름이 있고, 선택된 항목은 옅은 바탕과 왼쪽의 짧은 강조색 막대로,
/// 마우스를 올린 항목은 더 옅은 바탕으로 표시한다. 위아래 화살표·Home·End 로 옮긴다.
/// </summary>
sealed class NavList : ThemedControl
{
    public sealed record Item(string Glyph, string Text);

    readonly List<Item> _items = new();
    int _selected = -1, _hot = -1;
    const int ItemH = 38, ItemGap = 4;

    public event EventHandler? SelectedChanged;

    public NavList()
    {
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    public IReadOnlyList<Item> Items => _items;

    public void SetItems(IEnumerable<Item> items)
    {
        _items.Clear();
        _items.AddRange(items);
        if (_selected < 0 && _items.Count > 0) _selected = 0;
        Invalidate();
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_items.Count == 0) return;
            value = Math.Clamp(value, 0, _items.Count - 1);
            if (value == _selected) return;
            _selected = value;
            AccessibleDescription = _items[value].Text;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposed) =>
        new(Math.Max(proposed.Width, Px(160)), _items.Count * Px(ItemH + ItemGap));

    RectangleF ItemRect(int i) => new(Px(4), i * Px(ItemH + ItemGap) + Px(2), Width - Px(8), Px(ItemH));

    int HitTest(Point pt)
    {
        for (int i = 0; i < _items.Count; i++) if (ItemRect(i).Contains(pt)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h != _hot) { _hot = h; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { _hot = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int h = HitTest(e.Location);
        if (h >= 0) SelectedIndex = h;
    }
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up: SelectedIndex = _selected - 1; break;
            case Keys.Down: SelectedIndex = _selected + 1; break;
            case Keys.Home: SelectedIndex = 0; break;
            case Keys.End: SelectedIndex = _items.Count - 1; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        using var iconFont = Theme.IconFont(Px(16));
        for (int i = 0; i < _items.Count; i++)
        {
            var r = ItemRect(i);
            bool sel = i == _selected, hot = i == _hot;
            if (sel || hot)
            {
                // Windows 11 처럼 글자색을 아주 옅게 깐다: 어느 테마 색 위에서도 같은 느낌으로 보인다.
                using var path = Rounded(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Px(5));
                using var fill = new SolidBrush(Alpha(p.Text, sel ? (p.Dark ? 24 : 18) : (p.Dark ? 12 : 9)));
                g.FillPath(fill, path);
            }
            if (sel)
            {
                var pill = new RectangleF(r.X, r.Y + (r.Height - Px(16)) / 2f, Px(3), Px(16));
                using var pillPath = Rounded(pill, pill.Width / 2f);
                using var accent = new SolidBrush(p.Accent);
                g.FillPath(accent, pillPath);
            }
            int textLeft = Px(14);
            if (iconFont is not null)
            {
                TextRenderer.DrawText(g, _items[i].Glyph, iconFont, new Rectangle((int)r.X + Px(12), (int)r.Y, Px(20), (int)r.Height), p.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                textLeft = Px(46);
            }
            var textRect = new Rectangle((int)r.X + textLeft, (int)r.Y, (int)r.Width - textLeft - Px(8), (int)r.Height);
            TextRenderer.DrawText(g, _items[i].Text, Font, textRect, p.Text, TextFlags(TextFormatFlags.Left | TextFormatFlags.NoPrefix));
            if (sel) DrawFocusRing(g, r, Px(5));
        }
    }
}

/// <summary>
/// Windows 11 설정 카드. 왼쪽에 아이콘·제목·설명, 오른쪽에 컨트롤(<see cref="Action"/>), 필요하면 그 아래에 넓은 내용(<see cref="Body"/>:
/// 타일 선택기·색 견본·미리보기)을 둔다. 제목의 니모닉(&amp;)을 누르면 카드의 컨트롤로 간다(토글은 바로 켜고 끈다).
/// <see cref="Clickable"/> 이면 카드 전체가 버튼처럼 동작한다(링크 카드: 폴더 열기, 웹 페이지 열기).
/// 높이는 폭에 따라 정해지고(설명이 줄바꿈된다), 쌓는 쪽(<see cref="CardStack"/>)이 <see cref="GetPreferredSize"/> 로 묻는다.
/// </summary>
sealed class SettingsCard : Panel
{
    string _glyph = "", _title = "", _description = "", _trailingGlyph = "";
    Control? _action, _body;
    Image? _picture;
    Theme.Palette _palette = Theme.Current;
    bool _clickable, _hot, _pressed;

    const int PadH = 16, PadV = 12, MinH = 68, IconBox = 20, IconGap = 16, ActionGap = 24, BodyGap = 12, DescGap = 2, TrailingBox = 16;

    /// <summary><see cref="Theme"/> 가 칠할 때 넣어 준다.</summary>
    public Theme.Palette Palette { get => _palette; set { _palette = value; Invalidate(); } }

    public string Glyph { get => _glyph; set { _glyph = value; Changed(); } }
    /// <summary>제목. 니모닉(&amp;)을 쓸 수 있다.</summary>
    public string Title { get => _title; set { _title = value; Changed(); } }
    public string Description { get => _description; set { _description = value; Changed(); } }

    /// <summary>아이콘 자리에 글리프 대신 그릴 그림(앱 아이콘). 카드가 Dispose 한다.</summary>
    public Image? Picture { get => _picture; set { _picture = value; Changed(); } }
    public int PictureSize { get; set; } = 40;

    /// <summary>오른쪽 끝에 그릴 작은 글리프(링크 카드의 "새 창에서 열기" 표시). 컨트롤이 없을 때만 그린다.</summary>
    public string TrailingGlyph { get => _trailingGlyph; set { _trailingGlyph = value; Changed(); } }

    /// <summary>위 카드에 딸린 하위 항목(예: Caps Lock 표시 아래의 Shift 표시). 쌓을 때 들여 쓴다.</summary>
    public bool Indent { get; set; }

    /// <summary>몸통을 카드 안쪽 폭 전체로 늘린다(미리보기). 아니면 제목 글자 위치에서 시작하고 자기 크기를 쓴다.</summary>
    public bool StretchBody { get; set; }

    /// <summary>카드 전체를 누를 수 있게 한다. 마우스를 올리면 밝아지고, Tab 으로 포커스를 받아 Enter·Space 로 누른다(Click 이벤트).</summary>
    public bool Clickable
    {
        get => _clickable;
        set
        {
            _clickable = value;
            SetStyle(ControlStyles.Selectable, value);
            TabStop = value;
            Cursor = value ? Cursors.Hand : Cursors.Default;
            AccessibleRole = value ? AccessibleRole.Link : AccessibleRole.Grouping;
            Invalidate();
        }
    }

    public Control? Action { get => _action; set => Swap(ref _action, value); }
    public Control? Body { get => _body; set => Swap(ref _body, value); }

    public SettingsCard()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AccessibleRole = AccessibleRole.Grouping;
    }

    float DpiScale => DeviceDpi / 96f;
    int Px(float logical) => (int)Math.Round(logical * DpiScale);

    bool HasHeader => _title.Length > 0;
    bool HasIcon => _picture is not null || (_glyph.Length > 0 && Theme.HasIconFont);
    int IconWidth => _picture is not null ? Px(PictureSize) : Px(IconBox);
    /// <summary>
    /// 카드의 컨트롤이 꺼져 있으면 제목·설명도 흐리게. 오른쪽 컨트롤이 있으면 그것(예: Caps Lock 표시가 꺼진 동안의 Shift 표시),
    /// 없으면 아래 넓은 내용(예: 밑줄 모양일 때의 위치 타일)을 본다.
    /// </summary>
    bool Dimmed => (_action ?? _body) is { Enabled: false };
    string PlainTitle => _title.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");

    void Swap(ref Control? slot, Control? value)
    {
        if (slot is not null) { slot.EnabledChanged -= OnChildEnabledChanged; Controls.Remove(slot); }
        slot = value;
        if (value is not null) { Controls.Add(value); value.EnabledChanged += OnChildEnabledChanged; }
        Changed();
    }

    void OnChildEnabledChanged(object? sender, EventArgs e) => Invalidate();

    void Changed()
    {
        // 화면 읽기 프로그램이 컨트롤만 읽어도 무슨 설정인지 알도록 카드 제목·설명을 이름으로 준다(따로 정한 이름은 그대로 둔다).
        if (HasHeader)
        {
            AccessibleName = PlainTitle;
            AccessibleDescription = _description;
            foreach (var c in new[] { _action, _body })
                if (c is not null && string.IsNullOrEmpty(c.AccessibleName)) { c.AccessibleName = PlainTitle; c.AccessibleDescription = _description; }
        }
        Parent?.PerformLayout();
        PerformLayout();
        Invalidate();
    }

    static Size Measure(Control c) => c.AutoSize ? c.GetPreferredSize(Size.Empty) : c.Size;

    Size ActionSize => _action is { Visible: true } a ? Measure(a)
        : _trailingGlyph.Length > 0 && Theme.HasIconFont ? new Size(Px(TrailingBox), Px(TrailingBox)) : Size.Empty;

    int TextLeft => Px(PadH) + (HasIcon ? IconWidth + Px(IconGap) : 0);

    int TextWidth(int width)
    {
        var a = ActionSize;
        return Math.Max(Px(80), width - TextLeft - Px(PadH) - (a.Width > 0 ? a.Width + Px(ActionGap) : 0));
    }

    const TextFormatFlags TextFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

    (Size title, Size desc) MeasureText(int textWidth)
    {
        var t = HasHeader ? TextRenderer.MeasureText(DisplayTitle, Font, new Size(textWidth, int.MaxValue), TextFlags) : Size.Empty;
        var d = Size.Empty;
        if (_description.Length > 0)
        {
            using var hint = Theme.HintFont(Font);
            d = TextRenderer.MeasureText(_description, hint, new Size(textWidth, int.MaxValue), TextFlags | TextFormatFlags.NoPrefix);
        }
        return (t, d);
    }

    /// <summary>제목 줄(아이콘·글자·컨트롤)의 높이. 몸통이 있으면 그 아래에 이어진다.</summary>
    (int top, int height, Size title, Size desc) HeaderRow(int width)
    {
        if (!HasHeader) return (0, 0, Size.Empty, Size.Empty);
        var (t, d) = MeasureText(TextWidth(width));
        int textH = t.Height + (d.Height > 0 ? Px(DescGap) + d.Height : 0);
        int content = Math.Max(textH, Math.Max(ActionSize.Height, HasIcon ? IconWidth : 0));
        int row = Math.Max(Px(MinH - 2 * PadV), content);
        return (Px(PadV), row, t, d);
    }

    Size BodySize(int width)
    {
        if (_body is not { Visible: true } b) return Size.Empty;
        var s = Measure(b);
        return StretchBody ? new Size(width - 2 * Px(PadH), s.Height) : s;
    }

    int BodyLeft => StretchBody || !HasHeader ? Px(PadH) : TextLeft;

    int HeightFor(int width)
    {
        var (top, row, _, _) = HeaderRow(width);
        var body = BodySize(width);
        if (body.IsEmpty) return Math.Max(Px(MinH), top + row + Px(PadV));
        int bodyTop = HasHeader ? top + row + Px(BodyGap) : Px(PadH);
        return bodyTop + body.Height + Px(PadH);
    }

    public override Size GetPreferredSize(Size proposed)
    {
        int width = proposed.Width > 0 ? proposed.Width : Width;
        return new Size(width, HeightFor(width));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int width = Width;
        var (top, row, _, _) = HeaderRow(width);
        var body = BodySize(width);
        if (_action is { Visible: true } a)
        {
            var s = ActionSize;
            // 몸통이 없으면 카드 전체의 세로 가운데, 있으면 제목 줄의 세로 가운데.
            int y = body.IsEmpty ? (Height - s.Height) / 2 : top + (row - s.Height) / 2;
            a.SetBounds(width - Px(PadH) - s.Width, y, s.Width, s.Height);
        }
        if (_body is { Visible: true } b)
        {
            int bodyTop = HasHeader ? top + row + Px(BodyGap) : Px(PadH);
            b.SetBounds(BodyLeft, bodyTop, body.Width, body.Height);
        }
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Changed(); }

    /// <summary>그릴 제목. 한국어 니모닉 꼬리 "(&amp;V)" 는 Alt 를 눌러 키보드 단서가 켜졌을 때만 보인다.</summary>
    string DisplayTitle => Mnemonic.Display(_title, ShowKeyboardCues);

    protected override void OnChangeUICues(UICuesEventArgs e) { base.OnChangeUICues(e); Changed(); }

    // ── 누를 수 있는 카드 ──
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (_clickable) { _hot = true; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_clickable && e.Button == MouseButtons.Left) { _pressed = true; Focus(); Invalidate(); }
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (_pressed) { _pressed = false; Invalidate(); } }
    protected override void OnClick(EventArgs e) { if (_clickable) base.OnClick(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override bool IsInputKey(Keys keyData) =>
        (_clickable && (keyData & Keys.KeyCode) is Keys.Enter or Keys.Space) || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_clickable && e.KeyCode is Keys.Enter or Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); return; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        bool hc = SystemInformation.HighContrast;
        var p = Palette;
        var fill = hc ? SystemColors.Window : BackColor;
        var border = hc ? SystemColors.ControlDark : p.Border;
        var text = hc ? SystemColors.WindowText : p.Text;
        var subtle = hc ? SystemColors.GrayText : p.SubtleText;
        if (_clickable && !hc && (_hot || _pressed)) fill = Mix(fill, p.Text, _pressed ? 0.03f : 0.06f);
        if (Dimmed) { text = Mix(text, fill, 0.5f); subtle = Mix(subtle, fill, 0.4f); }

        g.Clear(Parent?.BackColor ?? p.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float radius = Px(Math.Min(p.Radius, 8));
        var outline = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Rounded(outline, radius))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        if (_clickable && Focused && ShowFocusCues)
        {
            var ring = new RectangleF(1.5f, 1.5f, Width - 3, Height - 3);
            using var path = Rounded(ring, radius);
            using var pen = new Pen(text, Px(2));
            g.DrawPath(pen, path);
        }
        if (!HasHeader) return;

        var (top, row, t, d) = HeaderRow(Width);
        if (BodySize(Width).IsEmpty) { top = 0; row = Height; }   // 몸통이 없으면 카드 전체의 세로 가운데
        if (_picture is not null)
        {
            int s = IconWidth;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_picture, new Rectangle(Px(PadH), top + (row - s) / 2, s, s));
        }
        else if (HasIcon)
        {
            using var icon = Theme.IconFont(Px(16));
            if (icon is not null)
                TextRenderer.DrawText(g, _glyph, icon, new Rectangle(Px(PadH), top, Px(IconBox), row), text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
        int textW = TextWidth(Width);
        int textH = t.Height + (d.Height > 0 ? Px(DescGap) + d.Height : 0);
        int y = top + (row - textH) / 2;
        TextRenderer.DrawText(g, DisplayTitle, Font, new Rectangle(TextLeft, y, textW, t.Height), text,
            TextFlags | TextFormatFlags.Left | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        if (d.Height > 0)
        {
            using var hint = Theme.HintFont(Font);
            TextRenderer.DrawText(g, _description, hint, new Rectangle(TextLeft, y + t.Height + Px(DescGap), textW, d.Height), subtle,
                TextFlags | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
        if (_action is not { Visible: true } && _trailingGlyph.Length > 0)
        {
            using var small = Theme.IconFont(Px(12));
            if (small is not null)
                TextRenderer.DrawText(g, _trailingGlyph, small, new Rectangle(Width - Px(PadH) - Px(TrailingBox), top, Px(TrailingBox), row), subtle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>제목의 니모닉(Alt+글자): 카드의 컨트롤로 간다. 토글이면 켜고 끈다(체크 상자와 같은 관례).</summary>
    protected override bool ProcessMnemonic(char charCode)
    {
        if (!HasHeader || !Visible || !Enabled || !IsMnemonic(charCode, _title)) return false;
        var target = _action ?? _body;
        if (target is null || !target.Enabled) return false;
        if (target is ToggleSwitch toggle) { toggle.Focus(); toggle.Checked = !toggle.Checked; return true; }
        if (target.CanSelect) target.Select();
        else target.SelectNextControl(null, true, true, true, false);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _picture?.Dispose(); _picture = null; }
        base.Dispose(disposing);
    }

    static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>카드 묶음 위의 소제목(굵게). 폭에 맞춰 줄바꿈한다.</summary>
sealed class SectionHeader : Label
{
    public SectionHeader(string text, Font font)
    {
        Text = text;
        Font = font;
        AutoSize = false;
        UseMnemonic = false;
        TabStop = false;
    }

    public override Size GetPreferredSize(Size proposed)
    {
        int width = proposed.Width > 0 ? proposed.Width : Width;
        var s = TextRenderer.MeasureText(Text, Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return new Size(width, s.Height + 2);
    }
}

/// <summary>카드 사이의 짧은 안내문(보조색). 폭에 맞춰 줄바꿈한다.</summary>
sealed class NoteLabel : Label
{
    public NoteLabel(string text, Font font)
    {
        Text = text;
        Font = font;
        ForeColor = SystemColors.GrayText;   // Theme 가 보조 글자색으로 바꾼다
        AutoSize = false;
        UseMnemonic = false;
        TabStop = false;
        Padding = new Padding(2, 0, 2, 0);
    }

    public override Size GetPreferredSize(Size proposed)
    {
        int width = proposed.Width > 0 ? proposed.Width : Width;
        var s = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, width - Padding.Horizontal), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return new Size(width, s.Height + 4);
    }
}

/// <summary>
/// 한 페이지의 본문: 소제목과 카드를 위에서 아래로 쌓고, 폭은 창에 맞추되 너무 넓으면 <see cref="MaxWidth"/> 까지만 쓴다(스크롤).
/// AutoSize·Dock 조합 대신 직접 배치한다. 카드 높이가 폭에 따라 바뀌어서(설명 줄바꿈) 폭을 정한 뒤 높이를 물어야 하기 때문이다.
/// </summary>
sealed class CardStack : Panel
{
    public const int SideMargin = 32, MaxWidth = 1000;
    const int TopMargin = 4, BottomMargin = 32, CardGap = 4, SectionGap = 28, AfterSection = 8, IndentWidth = 40;
    bool _arranging, _darkScrollBars;

    public CardStack()
    {
        AutoScroll = true;
        DoubleBuffered = true;
    }

    /// <summary>어두운 테마면 스크롤바도 어둡게(기본 스크롤바는 늘 밝은 회색이라 어두운 창에서 튄다). <see cref="Theme"/> 가 정한다.</summary>
    public bool DarkScrollBars
    {
        get => _darkScrollBars;
        set { if (_darkScrollBars == value) return; _darkScrollBars = value; ApplyScrollTheme(); }
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyScrollTheme(); }

    void ApplyScrollTheme()
    {
        if (!IsHandleCreated) return;
        try { Native.SetWindowTheme(Handle, _darkScrollBars ? "DarkMode_Explorer" : "Explorer", null); }
        catch (Exception ex) { Log.Error("scrollbar theme failed", ex); }
    }

    float DpiScale => DeviceDpi / 96f;
    int Px(float logical) => (int)Math.Round(logical * DpiScale);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (!_arranging && Visible)
        {
            _arranging = true;
            try { Arrange(); }
            finally { _arranging = false; }
        }
        // 자식 자리를 정한 뒤에 스크롤바를 계산한다. 먼저 계산하면 창을 줄이기 전의 카드 폭 때문에 쓸데없는 가로 스크롤바가 생긴다.
        base.OnLayout(levent);
    }

    void Arrange()
    {
        // 스크롤바 자리는 늘 비워 둔다: 스크롤바가 있는 페이지와 없는 페이지를 오갈 때 카드 오른쪽 끝이 흔들리지 않는다.
        int usable = Width - SystemInformation.VerticalScrollBarWidth;
        int width = Math.Max(Px(240), Math.Min(usable - 2 * Px(SideMargin), Px(MaxWidth)));
        int x = Px(SideMargin);
        int y = Px(TopMargin);
        Control? prev = null;
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            if (prev is not null)
                y += c is SectionHeader ? Px(SectionGap) : prev is SectionHeader ? Px(AfterSection) : Px(CardGap);
            int indent = c is SettingsCard { Indent: true } ? Px(IndentWidth) : 0;
            int h = c.GetPreferredSize(new Size(width - indent, 0)).Height;
            c.SetBounds(x + indent + AutoScrollPosition.X, y + AutoScrollPosition.Y, width - indent, h);
            y += h;
            prev = c;
        }
        var min = new Size(0, y + Px(BottomMargin));
        if (AutoScrollMinSize != min) AutoScrollMinSize = min;
    }

    public void ScrollToTop() => AutoScrollPosition = Point.Empty;
}

/// <summary>페이지 제목(크게)과 한 줄 설명. 본문 카드와 같은 왼쪽 선에 맞춘다.</summary>
sealed class PageHeader : ThemedControl
{
    string _description = "";

    public string Description { get => _description; set { _description = value; FitHeight(); Invalidate(); } }

    public PageHeader() { TabStop = false; SetStyle(ControlStyles.Selectable, false); }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitHeight(); Invalidate(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); FitHeight(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitHeight(); }

    int TextWidth => Math.Max(Px(200), Math.Min(Width - 2 * Px(CardStack.SideMargin), Px(CardStack.MaxWidth)));

    (Size title, Size desc) Measure()
    {
        using var titleFont = Theme.PageTitleFont(Font);
        using var hint = Theme.HintFont(Font);
        var t = TextRenderer.MeasureText(Text, titleFont, new Size(TextWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var d = _description.Length > 0
            ? TextRenderer.MeasureText(_description, hint, new Size(TextWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix)
            : Size.Empty;
        return (t, d);
    }

    void FitHeight()
    {
        var (t, d) = Measure();
        int h = Px(24) + t.Height + (d.Height > 0 ? Px(4) + d.Height : 0) + Px(20);
        if (Height != h) Height = h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        var (t, d) = Measure();
        int x = Px(CardStack.SideMargin), y = Px(24);
        using var titleFont = Theme.PageTitleFont(Font);
        TextRenderer.DrawText(g, Text, titleFont, new Rectangle(x, y, TextWidth, t.Height), p.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (d.Height > 0)
        {
            using var hint = Theme.HintFont(Font);
            TextRenderer.DrawText(g, _description, hint, new Rectangle(x, y + t.Height + Px(4), TextWidth, d.Height), p.SubtleText,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>왼쪽 탐색 창: 위에 앱 아이콘과 이름, 가운데 탐색 목록, 맨 아래 버전.</summary>
sealed class NavPane : Panel
{
    readonly PictureBox _icon;
    readonly Label _name, _version;
    readonly NavList _list;

    public NavPane(NavList list, string name, string version, Font nameFont)
    {
        _list = list;
        _icon = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, TabStop = false };
        using (var big = new Icon(Icons.App, 64, 64)) _icon.Image = big.ToBitmap();
        _name = new Label { Text = name, Font = nameFont, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
        _version = new Label { Text = version, ForeColor = SystemColors.GrayText, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
        Controls.Add(_icon);
        Controls.Add(_name);
        Controls.Add(_list);
        Controls.Add(_version);
    }

    float DpiScale => DeviceDpi / 96f;
    int Px(float logical) => (int)Math.Round(logical * DpiScale);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = Px(12);
        _icon.SetBounds(pad + Px(12), Px(20), Px(24), Px(24));
        _name.SetBounds(pad + Px(46), Px(16), Math.Max(0, Width - pad - Px(50)), Px(32));
        int listTop = Px(68);
        int listH = _list.GetPreferredSize(Size.Empty).Height;
        _list.SetBounds(pad, listTop, Math.Max(0, Width - 2 * pad), Math.Min(listH, Math.Max(0, Height - listTop - Px(48))));
        _version.SetBounds(pad + Px(14), Height - Px(40), Math.Max(0, Width - 2 * pad - Px(14)), Px(24));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icon.Image?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>아래 버튼 줄: 왼쪽에 안내문, 오른쪽에 버튼들. 위쪽에 얇은 구분선.</summary>
sealed class CommandBar : Panel
{
    readonly Label _hint;
    readonly Control[] _buttons;

    public CommandBar(string hint, params Control[] buttonsRightToLeft)
    {
        _hint = new Label { Text = hint, ForeColor = SystemColors.GrayText, AutoSize = false, AutoEllipsis = true, UseMnemonic = false };
        _buttons = buttonsRightToLeft;
        Controls.Add(_hint);
        foreach (var b in _buttons) Controls.Add(b);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    /// <summary>왼쪽 안내문. 편집 상태(저장하지 않은 변경이 있는지)에 따라 바꾼다.</summary>
    public string Hint
    {
        get => _hint.Text;
        set { if (_hint.Text != value) { _hint.Text = value; PerformLayout(); } }
    }

    float DpiScale => DeviceDpi / 96f;
    int Px(float logical) => (int)Math.Round(logical * DpiScale);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int x = Width - Px(CardStack.SideMargin);
        foreach (var b in _buttons)
        {
            var s = b.GetPreferredSize(Size.Empty);
            s.Width = Math.Max(s.Width, Px(104));   // 확인·취소 폭을 맞춘다
            x -= s.Width;
            b.SetBounds(x, (Height - s.Height) / 2, s.Width, s.Height);
            x -= Px(8);
        }
        int left = Px(CardStack.SideMargin);
        int w = Math.Max(0, x - Px(16) - left);
        // 창이 좁으면 두 줄까지만 쓰고 나머지는 "..." 로 줄인다.
        var hs = TextRenderer.MeasureText(_hint.Text, _hint.Font, new Size(Math.Max(1, w), int.MaxValue), TextFormatFlags.WordBreak);
        int h = Math.Min(hs.Height, 2 * _hint.Font.Height);
        _hint.SetBounds(left, (Height - h) / 2, w, h + 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var color = SystemInformation.HighContrast ? SystemColors.ControlDark : Theme.Current.Border;
        using var pen = new Pen(color);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}

/// <summary>버전처럼 짧은 정보를 담는 둥근 칩. 강조색을 옅게 깐 배경에 강조색 글자.</summary>
sealed class VersionChip : ThemedControl
{
    public VersionChip() { TabStop = false; AutoSize = true; SetStyle(ControlStyles.Selectable, false); }

    public override Size GetPreferredSize(Size proposed)
    {
        var ts = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(ts.Width + Px(16), ts.Height + Px(6));
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Size = GetPreferredSize(Size.Empty); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Size = GetPreferredSize(Size.Empty); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        using var path = Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f);
        using var fill = new SolidBrush(Alpha(p.Accent, p.Dark ? 40 : 28));
        using var pen = new Pen(Alpha(p.Accent, 90), 1f);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), p.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Windows 11 고르기 상자. 기본 ComboBox 는 어두운 테마에서 흰 테두리의 옛 모양으로 그려져 직접 그린다.
/// 누르면(또는 Space·F4·Alt+↓) 테마 메뉴로 목록을 펼치고, 닫힌 채로 위아래 화살표·Home·End 로도 바꾼다.
/// 마우스 휠은 받지 않는다: 스크롤되는 설정 페이지에서 휠을 굴리다 지나가도 값이 바뀌지 않는다(Windows 11 설정 앱과 같다).
/// </summary>
sealed class ThemedComboBox : ThemedControl
{
    readonly List<string> _items = new();
    int _selected = -1;
    ContextMenuStrip? _menu;
    long _closedAt;

    public event EventHandler? SelectedIndexChanged;

    public ThemedComboBox()
    {
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
        Size = new Size(200, 32);
    }

    public void SetItems(IEnumerable<string> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Invalidate();
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (value == _selected) return;
            _selected = value;
            Invalidate();
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedText => _selected >= 0 && _selected < _items.Count ? _items[_selected] : "";

    bool Open => _menu is { Visible: true };

    void ShowList()
    {
        if (_items.Count == 0 || Open) return;
        if (Environment.TickCount64 - _closedAt < 250) return;   // 펼친 목록을 닫으려고 상자를 다시 누른 클릭
        _menu?.Dispose();
        var menu = _menu = new ContextMenuStrip
        {
            Renderer = Theme.CreateMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Font,
            MinimumSize = new Size(Width, 0),
        };
        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(_items[i].Replace("&", "&&")) { Checked = i == _selected, Padding = new Padding(0, Px(3), 0, Px(3)) };
            item.Click += (_, _) => SelectedIndex = index;
            menu.Items.Add(item);
        }
        menu.Opened += (_, _) => Theme.RoundCorners(menu.Handle);
        menu.Closed += (_, _) => { _closedAt = Environment.TickCount64; Invalidate(); };
        menu.Show(this, new Point(0, Height + Px(2)));
        if (_selected >= 0) menu.Items[_selected].Select();   // 키보드로 바로 위아래 이동
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) ShowList();
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.F4 || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.F4 or Keys.Space || (e.Alt && e.KeyCode == Keys.Down)) { ShowList(); e.Handled = true; return; }
        switch (e.KeyCode)
        {
            case Keys.Up: SelectedIndex = _selected - 1; break;
            case Keys.Down: SelectedIndex = _selected + 1; break;
            case Keys.Home: SelectedIndex = 0; break;
            case Keys.End: SelectedIndex = _items.Count - 1; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        int alpha = Enabled ? 255 : 90;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float radius = Px(p.Radius / 2);
        using (var path = Rounded(r, radius))
        {
            var fill = Pressed || Open || Hot ? p.Hover : p.Elevated;
            using var b = new SolidBrush(Alpha(fill, alpha));
            using var pen = new Pen(Alpha(p.Border, alpha), Px(1));
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }
        if (Enabled && !Pressed && !Open)
            using (var bottom = new Pen(Alpha(Color.Black, p.Dark ? 60 : 24), Px(1)))
                g.DrawLine(bottom, r.Left + Px(4), r.Bottom, r.Right - Px(4), r.Bottom);

        int chevron = Px(32);
        TextRenderer.DrawText(g, SelectedText, Font, new Rectangle(Px(11), 0, Math.Max(0, Width - Px(11) - chevron), Height), Alpha(p.Text, alpha),
            TextFlags(TextFormatFlags.Left | TextFormatFlags.NoPrefix));
        var box = new Rectangle(Width - chevron, 0, chevron - Px(4), Height);
        using var icon = Theme.IconFont(Px(10));
        if (icon is not null)
            TextRenderer.DrawText(g, "\uE70D", icon, box, Alpha(p.SubtleText, alpha),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        else
        {
            float cx = box.Left + box.Width / 2f, cy = Height / 2f;
            using var pen = new Pen(Alpha(p.SubtleText, alpha), Px(1.5f));
            g.DrawLines(pen, new[] { new PointF(cx - Px(4), cy - Px(2)), new PointF(cx, cy + Px(2)), new PointF(cx + Px(4), cy - Px(2)) });
        }
        DrawFocusRing(g, r, radius);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ComboAccessible(this);

    /// <summary>화면 읽기 프로그램이 지금 고른 항목을 값으로 읽게 한다.</summary>
    sealed class ComboAccessible : ControlAccessibleObject
    {
        readonly ThemedComboBox _owner;
        public ComboAccessible(ThemedComboBox owner) : base(owner) { _owner = owner; }
        public override AccessibleRole Role => AccessibleRole.ComboBox;
        public override string? Value { get => _owner.SelectedText; set { } }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Windows 11 입력칸 틀. 테두리 없는 TextBox 를 안에 두고 둥근 테두리와 바탕을 직접 그린다(기본 테두리는 어두운 테마에서 흰 선이다).
/// 입력칸에 포커스가 있으면 아래쪽에 강조색 줄을 긋는다. 오른쪽 끝에 작은 버튼(<see cref="Trailing"/>)을 둘 수 있다.
/// </summary>
sealed class InputFrame : Panel
{
    Theme.Palette _palette = Theme.Current;

    public TextBox Box { get; }
    public Control? Trailing { get; }

    /// <summary><see cref="Theme"/> 가 칠할 때 넣어 준다. 안쪽 입력칸과 버튼 색도 함께 맞춘다.</summary>
    public Theme.Palette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            Box.BackColor = Fill;
            Box.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : value.Text;
            if (Trailing is ThemedControl t) { t.Palette = value; t.BackColor = Fill; }
            Invalidate();
        }
    }

    public InputFrame(TextBox box, Control? trailing = null)
    {
        Box = box;
        Trailing = trailing;
        box.BorderStyle = BorderStyle.None;
        box.GotFocus += (_, _) => Invalidate();
        box.LostFocus += (_, _) => Invalidate();
        box.EnabledChanged += (_, _) => Invalidate();
        Controls.Add(box);
        if (trailing is not null) Controls.Add(trailing);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(200, 32);
        Palette = Theme.Current;
    }

    float DpiScale => DeviceDpi / 96f;
    int Px(float logical) => (int)Math.Round(logical * DpiScale);
    Color Fill => SystemInformation.HighContrast ? SystemColors.Window : _palette.Input;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = Px(10);
        int tw = Trailing is null ? 0 : Px(30);
        int h = Box.PreferredHeight;
        Box.SetBounds(pad, (Height - h) / 2, Math.Max(0, Width - 2 * pad - tw), h);
        Trailing?.SetBounds(Width - tw - Px(3), Px(3), tw, Math.Max(0, Height - Px(6)));
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (Box.CanFocus) Box.Focus(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = _palette;
        bool hc = SystemInformation.HighContrast;
        int alpha = Box.Enabled ? 255 : 110;
        g.Clear(Parent?.BackColor ?? p.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float radius = Px(Math.Max(2, p.Radius / 2));
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Rounded(r, radius))
        using (var brush = new SolidBrush(Color.FromArgb(alpha, Fill)))
        using (var pen = new Pen(Color.FromArgb(alpha, hc ? SystemColors.WindowFrame : p.Border)))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        if (Box.Focused && !hc)
        {
            // Windows 11 입력칸: 포커스가 있으면 아래 가장자리를 강조색 2px 로
            var clip = g.Save();
            using (var path = Rounded(r, radius)) g.SetClip(path);
            using var accent = new SolidBrush(p.Accent);
            g.FillRectangle(accent, 0, Height - Px(2), Width, Px(2));
            g.Restore(clip);
        }
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>글리프 하나짜리 작은 버튼(입력칸 오른쪽의 펼치기 단추). Tab 으로는 가지 않는다(입력칸에서 Alt+↓ 로 연다).</summary>
sealed class GlyphButton : ThemedControl
{
    public string Glyph { get; set; } = "";

    public GlyphButton()
    {
        Cursor = Cursors.Hand;
        TabStop = false;
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        if (Hot || Pressed)
        {
            using var path = Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Px(3));
            using var fill = new SolidBrush(Alpha(p.Text, Pressed ? 26 : 16));
            g.FillPath(fill, path);
        }
        using var icon = Theme.IconFont(Px(10));
        if (icon is not null)
            TextRenderer.DrawText(g, Glyph, icon, ClientRectangle, Enabled ? p.SubtleText : Alpha(p.SubtleText, 90),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}

