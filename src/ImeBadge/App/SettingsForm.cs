using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 설정 창. 바꾸는 즉시 실제 배지에 반영되어(WYSIWYG) 화면 밖 배지로 결과를 바로 볼 수 있고,
/// [확인]을 누르면 저장, [취소]나 닫기를 누르면 창을 열 때의 설정으로 되돌린다.
/// 편집은 복사본(<see cref="_draft"/>)에 하고 매번 실제 설정(<see cref="_live"/>)으로 복사한다.
/// 모양은 Windows 11 설정 앱을 따른다: 제목·설명이 붙은 카드, 토글 스위치, 강조색 슬라이더, 그림 타일 선택, 색 견본.
/// 미리보기는 실제 렌더러(<see cref="BadgeRenderer"/>)로 그려 창 밖 배지와 똑같이 보인다.
/// </summary>
sealed class SettingsForm : Form
{
    readonly Settings _live;
    readonly Settings _draft;
    readonly Settings _original;   // 취소할 때 되돌릴 값
    readonly bool _builtKorean = Strings.IsKorean;   // 이 창을 만들 때의 언어. 바뀌면 새 창으로 갈아 끼운다
    bool _autostart, _dirty, _loading, _detached;

    TilePicker _theme = null!, _style = null!, _character = null!, _placement = null!;
    AccentSlider _size = null!, _opacity = null!;
    NumericUpDown _poll = null!;
    ComboBox _language = null!;
    ColorSwatches _hangulColor = null!, _englishColor = null!;
    Label _hangulHex = null!, _englishHex = null!;
    ToggleSwitch _autostartBox = null!, _fullscreen = null!, _hotkey = null!, _updates = null!, _trayStateBox = null!, _animate = null!, _capsLock = null!, _trackImage = null!;
    HotkeyBox _hotkeyBox = null!;
    ProcessListEditor _excluded = null!, _corner = null!;
    PreviewPanel _preview = null!;
    readonly ToolTip _tips = new() { AutoPopDelay = 12000 };
    // "점, 바뀔 때 1.5초 글자" 미리보기용. 실제 배지처럼 설정이 바뀐 직후 1.5초는 글자 배지를, 그 뒤엔 점을 보여 준다.
    readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = BadgeForm.FlashMs };
    bool _flashing;

    /// <summary>편집 중 값이 바뀔 때마다 발생. 실제 설정에는 이미 복사되어 있으니 다시 그리기만 하면 된다(저장은 하지 않는다).</summary>
    public event Action? Changed;
    /// <summary>[확인]으로 확정되었거나 [취소]로 되돌려졌을 때 발생. 저장한다.</summary>
    public event Action? Applied;
    /// <summary>UI 언어가 이 창을 만들 때와 달라졌다. 받는 쪽(BadgeForm)이 <see cref="Reopen"/> 으로 새 창을 만들고 이 창을 <see cref="Detach"/> 한다.</summary>
    public event Action? LanguageChanged;

    public SettingsForm(Settings live) : this(live, live.Clone(), dirty: false) { }

    /// <param name="original">취소할 때 되돌릴 값. 새 창이 이전 창의 기준을 이어받을 때 넘긴다.</param>
    /// <param name="dirty">이미 편집이 있었는가(이어받은 창은 true).</param>
    SettingsForm(Settings live, Settings original, bool dirty)
    {
        _live = live;
        _draft = live.Clone();
        _original = original;
        _autostart = Autostart.IsEnabled();

        Text = Strings.Format("settings.title", AppInfo.ProductName);
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);   // 아래 픽셀 크기들은 96 DPI 기준. 고DPI 에서 WinForms 가 배율을 곱한다
        Font = Theme.DialogFont;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(Outer);

        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); _flashing = false; _preview.Invalidate(); };

        Build();
        LoadDraftIntoControls();
        _dirty = dirty;   // 컨트롤 초기화로 생긴 변경 알림은 실제 변경이 아니다
        _painted = Theme.Design = DesignThemes.Get(_draft.Theme);
        Theme.Apply(this);
    }

    /// <summary>이 창을 마지막으로 칠한 테마. 편집 중 테마가 바뀌면 창 전체를 새 색으로 다시 칠한다(<see cref="Recolor"/>).</summary>
    DesignTheme _painted;

    /// <summary>창과 모든 컨트롤을 현재 테마 색으로 다시 칠한다. 컨트롤을 새로 만들지 않으므로 편집 중인 값·포커스가 그대로다.</summary>
    void Recolor()
    {
        Theme.Apply(this);
        Theme.ApplyTitleBar(this);
        Invalidate(true);
    }

    /// <summary>같은 편집 상태(기준값·자동 시작 체크)를 이어받는 새 창을 현재 언어로 만든다. 위치도 그대로.</summary>
    public SettingsForm Reopen()
    {
        var next = new SettingsForm(_live, _original, dirty: true) { _autostart = _autostart };
        next._autostartBox.Checked = _autostart;
        if (IsHandleCreated) { next.StartPosition = FormStartPosition.Manual; next.Location = Location; }
        return next;
    }

    /// <summary>닫을 때 되돌리기·저장 알림을 하지 않게 한다(새 창에 편집을 넘긴 뒤).</summary>
    public void Detach() => _detached = true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    // ── 화면 구성 ──
    // 레이아웃 원칙: 자동 크기(AutoSize) 컨테이너 안에는 Dock 을 쓰지 않는다. AutoSize 부모는 자식 크기로 자기 크기를 정하고,
    // Dock 된 자식은 부모 크기로 자기 크기를 정하므로 서로를 기다리다 폭 0 으로 접힌다(제목이 세로로 찍히던 문제).
    // 카드는 고정 폭(GroupWidth)을 주고 높이만 내용에 맞춘다. 96 DPI 기준 픽셀이며 고DPI 에서는 WinForms 가 배율을 곱한다.
    // 간격은 8px 격자: 바깥 여백 20, 카드 사이 12, 카드 안쪽 16, 행 사이 8.
    const int Outer = 20, GroupWidth = 424, CardInset = 16, CardGap = 12;
    const int InnerWidth = GroupWidth - 2 * CardInset;

    void Build()
    {
        var root = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(Outer, Outer) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 머리: 아이콘 + 제목 + 한 줄 안내
        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);

        // 왼쪽: 모양 + 동작
        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, CardGap, 0) };
        left.Controls.Add(BuildLookGroup());
        left.Controls.Add(BuildBehaviorGroup());
        root.Controls.Add(left, 0, 1);

        // 오른쪽: 미리보기 + 제외 앱 + 모서리 배지 앱
        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        right.Controls.Add(BuildPreviewGroup());
        right.Controls.Add(BuildExcludeGroup());
        right.Controls.Add(BuildCornerGroup());
        root.Controls.Add(right, 1, 1);

        // 아래: 버튼. 오른쪽 끝에 확인·취소, 왼쪽으로 떨어져 기본값 복원.
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
        var cancel = new AccentButton { Text = Strings.Get("settings.cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        var ok = new AccentButton { Text = Strings.Get("settings.ok"), Primary = true, AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        var reset = new AccentButton { Text = Strings.Get("settings.reset"), AutoSize = true, Margin = new Padding(24, 0, 0, 0) };
        // 모드리스(Show) 창은 DialogResult 만으로는 닫히지 않는다. 명시적으로 닫는다.
        ok.Click += (_, _) => { Apply(); DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        reset.Click += (_, _) =>
        {
            // 고른 테마는 그대로 두고, 나머지와 배지 색을 그 테마의 기본값으로.
            var design = DesignThemes.Get(_draft.Theme);
            _draft.CopyFrom(new Settings { Theme = design.Id, HangulColor = design.HangulColor, EnglishColor = design.EnglishColor });
            LoadDraftIntoControls();
        };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok); buttons.Controls.Add(reset);
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);

        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(root);
    }

    Control BuildHeader()
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 0, 16) };
        var pic = new PictureBox { Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 2, 12, 0) };
        using (var big = new Icon(Icons.App, 128, 128)) pic.Image = big.ToBitmap();
        var texts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        texts.Controls.Add(new Label { Text = Strings.Get("settings.heading"), Font = Theme.TitleFont(Font), AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
        texts.Controls.Add(new Label { Text = Strings.Get("settings.subtitle"), ForeColor = SystemColors.GrayText, AutoSize = true, Margin = Padding.Empty });
        row.Controls.Add(pic);
        row.Controls.Add(texts);
        return row;
    }

    GroupBox BuildLookGroup()
    {
        var g = NewGroup(Strings.Get("group.look"), Strings.Get("group.look.desc"));
        var t = NewTable(g);

        // 테마: 배지 색·질감과 이 창의 색을 한 벌로 바꾼다. 타일에는 그 테마의 한/영 배지를 실제 렌더러로 그린다.
        AddRow(t, null, FieldLabel(Strings.Get("look.theme")));
        _theme = new TilePicker { TileSize = new Size(68, 58) };
        _theme.SetTiles(DesignThemes.All.Select(d => new TilePicker.Tile(d.Id, Strings.Get("theme." + d.Id), (gr, r, p) => DrawThemeTile(gr, r, d))));
        _theme.SelectedChanged += (_, _) => { if (_theme.SelectedValue is string id && id != _draft.Theme) ChooseTheme(DesignThemes.Get(id)); };
        AddRow(t, null, _theme);
        _tips.SetToolTip(_theme, Strings.Get("look.theme.tip"));

        // 배지 모양: 실제 렌더러로 그린 타일에서 고른다. 윗줄은 기본 모양, 아랫줄은 캐릭터. 둘 중 한 줄에서만 선택된다.
        AddRow(t, null, FieldLabel(Strings.Get("look.style")));
        _style = new TilePicker { TileSize = new Size(68, 58) };
        _style.SetTiles(Labels.Styles.Select(s => new TilePicker.Tile(s.value, ShortStyle(s.value), (gr, r, p) => DrawStyleTile(gr, r, p, s.value))));
        _style.SelectedChanged += (_, _) =>
        {
            if (_style.SelectedValue is not BadgeStyle v) return;
            _draft.Style = v;
            _draft.Character = BadgeCharacters.None;
            _character.SelectedValue = null;
            Touch();
        };
        AddRow(t, null, _style);
        _character = new TilePicker { TileSize = new Size(68, 58) };
        _character.SetTiles(Labels.Characters.Select(c => new TilePicker.Tile(c.id, c.label, (gr, r, p) => DrawCharacterTile(gr, r, p, c.id))));
        _character.SelectedChanged += (_, _) =>
        {
            if (_character.SelectedValue is not string id) return;
            _draft.Character = id;
            _draft.Style = BadgeStyle.Pill;   // 구버전으로 되돌려도 둥근 배지로 보이게
            _style.SelectedValue = null;
            Touch();
        };
        AddRow(t, null, _character);

        AddRow(t, null, FieldLabel(Strings.Get("look.placement")));
        _placement = new TilePicker { TileSize = new Size(68, 58) };
        _placement.SetTiles(Labels.Placements.Select(pl => new TilePicker.Tile(pl.value, ShortPlacement(pl.value), (gr, r, p) => DrawPlacementTile(gr, r, p, pl.value))));
        _placement.SelectedChanged += (_, _) => { if (_placement.SelectedValue is BadgePlacement v) { _draft.Placement = v; Touch(); } };
        AddRow(t, null, _placement);
        _tips.SetToolTip(_placement, Strings.Get("look.placement.tip"));

        // 크기·불투명도는 눈으로 맞추는 값이라 숫자 입력보다 슬라이더가 자연스럽다(Windows 설정 앱과 같은 방식).
        AddRow(t, Strings.Get("look.size"), Slider(out _size, 50, 300, 5, 25, v => { _draft.SizePercent = v; Touch(); }));
        AddRow(t, Strings.Get("look.opacity"), Slider(out _opacity, 30, 100, 5, 10, v => { _draft.OpacityPercent = v; Touch(); }));
        _tips.SetToolTip(_size, Strings.Get("look.size.tip"));
        _tips.SetToolTip(_opacity, Strings.Get("look.opacity.tip"));

        AddRow(t, Strings.Get("look.hangulColor"), Swatches(out _hangulColor, out _hangulHex, v => _draft.HangulColor = v));
        AddRow(t, Strings.Get("look.englishColor"), Swatches(out _englishColor, out _englishHex, v => _draft.EnglishColor = v));
        _tips.SetToolTip(_hangulColor, Strings.Get("look.swatch.tip"));
        _tips.SetToolTip(_englishColor, Strings.Get("look.swatch.tip"));

        _animate = Toggle(Strings.Get("look.animate"), v => { _draft.Animate = v; Touch(); });
        AddRow(t, null, _animate);
        _tips.SetToolTip(_animate, Strings.Get("look.animate.tip"));

        _capsLock = Toggle(Strings.Get("look.capsLock"), v => { _draft.ShowCapsLock = v; Touch(); });
        AddRow(t, null, _capsLock);
        _tips.SetToolTip(_capsLock, Strings.Get("look.capsLock.tip"));

        g.Controls.Add(t);
        return g;
    }

    GroupBox BuildBehaviorGroup()
    {
        var g = NewGroup(Strings.Get("group.behavior"), Strings.Get("group.behavior.desc"));
        var t = NewTable(g);

        _autostartBox = Toggle(Strings.Get("behavior.autostart"), v => _autostart = v);   // 레지스트리는 [확인] 때만 만진다
        AddRow(t, null, _autostartBox);

        _fullscreen = Toggle(Strings.Get("behavior.fullscreen"), v => { _draft.HideOnFullscreen = v; Touch(); });
        AddRow(t, null, _fullscreen);

        _trayStateBox = Toggle(Strings.Get("behavior.trayState"), v => { _draft.TrayShowsState = v; Touch(); });
        AddRow(t, null, _trayStateBox);

        // 단축키: 토글 + 키 조합을 받는 입력칸을 한 줄에.
        var hotkeyRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        _hotkey = Toggle(Strings.Get("behavior.hotkey"), v => { _draft.HotkeyEnabled = v; _hotkeyBox.Enabled = v; Touch(); });
        _hotkey.Margin = new Padding(0, 0, 12, 0);
        _hotkeyBox = new HotkeyBox { Width = 130 };
        _hotkeyBox.HotkeyChanged += spec => { _draft.Hotkey = spec.ToString(); Touch(); };
        _tips.SetToolTip(_hotkeyBox, Strings.Get("behavior.hotkey.tip"));
        hotkeyRow.Controls.Add(_hotkey);
        hotkeyRow.Controls.Add(_hotkeyBox);
        AddRow(t, null, hotkeyRow);

        _updates = Toggle(Strings.Get("behavior.updates"), v => { _draft.CheckForUpdates = v; Touch(); });
        AddRow(t, null, _updates);
        _tips.SetToolTip(_updates, Strings.Get("behavior.updates.tip"));

        _poll = new NumericUpDown { Minimum = 50, Maximum = 1000, Increment = 50, Width = 80 };
        _poll.ValueChanged += (_, _) => { _draft.PollIntervalMs = (int)_poll.Value; Touch(); };
        AddRow(t, Strings.Get("behavior.poll"), _poll);
        AddRow(t, null, Hint(Strings.Get("behavior.poll.note")));
        _tips.SetToolTip(_poll, Strings.Get("behavior.poll.tip"));

        _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _language.Items.AddRange(Labels.Languages.Select(l => (object)l.label).ToArray());
        _language.SelectedIndexChanged += (_, _) => { _draft.Language = Labels.Languages[Math.Max(0, _language.SelectedIndex)].value; Touch(); };
        AddRow(t, Strings.Get("behavior.language"), _language);
        _tips.SetToolTip(_language, Strings.Get("behavior.language.tip"));

        g.Controls.Add(t);
        return g;
    }

    GroupBox BuildPreviewGroup()
    {
        var g = NewGroup(Strings.Get("group.preview"), Strings.Get("group.preview.desc"));
        _preview = new PreviewPanel { Location = g.ContentOrigin, Width = InnerWidth, Height = PreviewHeight, Tag = "custom-paint" };
        _preview.Paint += (_, e) => PaintPreview(e.Graphics);
        _tips.SetToolTip(_preview, Strings.Get("preview.tip"));
        g.Controls.Add(_preview);
        return g;
    }

    GroupBox BuildExcludeGroup()
    {
        var g = NewGroup(Strings.Get("group.exclude"), Strings.Get("group.exclude.desc"));
        // 목록은 초안(_draft)의 List 를 매번 가리킨다. "기본값 복원" 이 List 인스턴스를 바꾸므로 붙잡아 두면 안 된다.
        _excluded = new ProcessListEditor(() => _draft.ExcludedProcesses, InnerWidth, _tips,
            Strings.Get("exclude.add"), Strings.Get("exclude.remove"), Strings.Get("exclude.new.tip"), Strings.Get("exclude.note"))
        { Location = g.ContentOrigin };
        _excluded.Changed += Touch;
        g.Controls.Add(_excluded);
        return g;
    }

    /// <summary>caret 을 못 찾는 앱(Xshell 등)의 앱 목록 + 이미지 커서 추적 토글. 목록은 제외 목록과 같은 편집기를 쓴다.</summary>
    GroupBox BuildCornerGroup()
    {
        var g = NewGroup(Strings.Get("group.corner"), Strings.Get("group.corner.desc"));
        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Location = g.ContentOrigin, Margin = Padding.Empty,
        };

        _trackImage = Toggle(Strings.Get("corner.trackImage"), on => { _draft.TrackCursorByImage = on; Touch(); });
        _tips.SetToolTip(_trackImage, Strings.Get("corner.trackImage.tip"));
        stack.Controls.Add(_trackImage);

        _corner = new ProcessListEditor(() => _draft.CornerBadgeProcesses, InnerWidth, _tips,
            Strings.Get("corner.add"), Strings.Get("corner.remove"), Strings.Get("exclude.new.tip"), Strings.Get("corner.note"))
        { Margin = new Padding(0, 8, 0, 0) };
        _corner.Changed += Touch;
        stack.Controls.Add(_corner);

        g.Controls.Add(stack);
        return g;
    }

    // ── 타일 그리기 ──
    static string ShortStyle(BadgeStyle s) => Strings.Get(s switch
    {
        BadgeStyle.Box => "style.short.box", BadgeStyle.Pill => "style.short.pill", BadgeStyle.Dot => "style.short.dot",
        BadgeStyle.Underline => "style.short.underline", _ => "style.short.dotFlash",
    });

    static string ShortPlacement(BadgePlacement p) => Strings.Get(p switch
    {
        BadgePlacement.AboveRight => "place.short.aboveRight", BadgePlacement.BelowRight => "place.short.belowRight",
        BadgePlacement.AboveLeft => "place.short.aboveLeft", _ => "place.short.belowLeft",
    });

    /// <summary>테마 타일: 그 테마의 창 바탕 위에 한글·영문 배지를 나란히(테마 기본색, 실제 렌더러).</summary>
    void DrawThemeTile(Graphics g, RectangleF r, DesignTheme design)
    {
        float dpi = DeviceDpi / 96f;
        var bg = Color.FromArgb(Theme.IsDark ? design.Dark.Window : design.Light.Window);
        using (var path = RoundedPath(r, 4 * dpi))
        using (var brush = new SolidBrush(bg))
            g.FillPath(brush, path);
        var theme = BadgeTheme.Of(design);
        using var ko = BadgeRenderer.Render(ImeState.Hangul, BadgeStyle.Pill, 0.72f * dpi, theme);
        using var en = BadgeRenderer.Render(ImeState.English, BadgeStyle.Pill, 0.72f * dpi, theme);
        float gap = 1 * dpi, total = ko.Width + gap + en.Width;
        float x = r.Left + (r.Width - total) / 2, y = r.Top + (r.Height - ko.Height) / 2;
        g.DrawImage(ko, new RectangleF(x, y, ko.Width, ko.Height), new Rectangle(Point.Empty, ko.Size), GraphicsUnit.Pixel);
        g.DrawImage(en, new RectangleF(x + ko.Width + gap, y, en.Width, en.Height), new Rectangle(Point.Empty, en.Size), GraphicsUnit.Pixel);
    }

    /// <summary>캐릭터 타일: 짧은 caret 오른쪽에 그 캐릭터의 한글 배지. 귀가 있어 둥근 배지보다 조금 작게 그린다.</summary>
    void DrawCharacterTile(Graphics g, RectangleF r, Theme.Palette p, string character)
    {
        float dpi = DeviceDpi / 96f;
        using var bmp = BadgeRenderer.Render(ImeState.Hangul, BadgeStyle.Pill, 0.78f * dpi, BadgeTheme.From(_draft) with { Character = character }, 100);
        var caret = new Rectangle((int)(r.Left + 6 * dpi), (int)(r.Top + r.Height / 2 - 8 * dpi), 1, (int)(16 * dpi));
        using (var pen = new Pen(p.Text, Math.Max(1f, dpi))) g.DrawLine(pen, caret.Left, caret.Top, caret.Left, caret.Bottom);
        var pos = new Point(caret.Right + (int)(3 * dpi), (int)(r.Top + r.Height / 2 - bmp.Height / 2f));
        g.DrawImage(bmp, new Rectangle(pos, bmp.Size), new Rectangle(Point.Empty, bmp.Size), GraphicsUnit.Pixel);
    }

    /// <summary>모양 타일: 짧은 caret 오른쪽에 그 모양의 한글 배지를 실제 렌더러로 그린다.</summary>
    void DrawStyleTile(Graphics g, RectangleF r, Theme.Palette p, BadgeStyle style)
    {
        float dpi = DeviceDpi / 96f;
        var theme = BadgeTheme.From(_draft) with { Character = BadgeCharacters.None };
        var draw = style == BadgeStyle.DotFlash ? BadgeStyle.Pill : style;
        using var bmp = BadgeRenderer.Render(ImeState.Hangul, draw, 0.85f * dpi, theme, 100);
        var caret = new Rectangle((int)(r.Left + 6 * dpi), (int)(r.Top + r.Height / 2 - 8 * dpi), 1, (int)(16 * dpi));
        using (var pen = new Pen(p.Text, Math.Max(1f, dpi))) g.DrawLine(pen, caret.Left, caret.Top, caret.Left, caret.Bottom);
        // 밑줄은 caret 아래, 나머지는 caret 오른쪽에 세로 가운데.
        var pos = style == BadgeStyle.Underline
            ? new Point(caret.Left - bmp.Width / 2 + 1, caret.Bottom + (int)(2 * dpi))
            : new Point(caret.Right + (int)(4 * dpi), (int)(r.Top + r.Height / 2 - bmp.Height / 2f));
        g.DrawImage(bmp, new Rectangle(pos, bmp.Size), new Rectangle(Point.Empty, bmp.Size), GraphicsUnit.Pixel);
    }

    /// <summary>위치 타일: 가운데 caret 을 두고 그 위치에 점 배지를 놓는다. 어디에 뜨는지 한눈에 보인다.</summary>
    void DrawPlacementTile(Graphics g, RectangleF r, Theme.Palette p, BadgePlacement placement)
    {
        float dpi = DeviceDpi / 96f;
        var caret = new Rectangle((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2 - 8 * dpi), 1, (int)(16 * dpi));
        using (var pen = new Pen(p.Text, Math.Max(1f, dpi))) g.DrawLine(pen, caret.Left, caret.Top, caret.Left, caret.Bottom);
        using var bmp = BadgeRenderer.Render(ImeState.Hangul, BadgeStyle.Dot, dpi, BadgeTheme.From(_draft) with { Character = BadgeCharacters.None }, 100);
        var pos = BadgeLayout.Compute(new LayoutInput(caret, bmp.Size, BadgeStyle.Dot, placement, dpi, Rectangle.Round(r)));
        g.DrawImage(bmp, new Rectangle(pos, bmp.Size), new Rectangle(Point.Empty, bmp.Size), GraphicsUnit.Pixel);
    }

    // ── 도우미 ──
    /// <summary>폭은 고정(GroupWidth), 높이는 내용에 맞춰 자란다. 자식은 Dock 없이 <see cref="CardGroupBox.ContentOrigin"/> 에 둔다.</summary>
    static CardGroupBox NewGroup(string title, string description) =>
        new()
        {
            Text = title,
            Description = description,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(GroupWidth, 0),
            Padding = new Padding(CardInset, 0, CardInset, CardInset),
            Margin = new Padding(0, 0, 0, CardGap),
        };

    static TableLayoutPanel NewTable(CardGroupBox card)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = card.ContentOrigin };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return t;
    }

    static void AddRow(TableLayoutPanel t, string? label, Control c)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        c.Margin = new Padding(0, 4, 0, 4);
        if (label is null)
        {
            t.Controls.Add(c, 0, row);
            t.SetColumnSpan(c, 2);
        }
        else
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 4), MinimumSize = new Size(88, 0) }, 0, row);
            t.Controls.Add(c, 1, row);
        }
    }

    /// <summary>타일 선택기처럼 폭이 넓은 항목의 이름표. 아래 컨트롤로 Alt 니모닉이 이어진다.</summary>
    static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };

    static Label Hint(string text) => new() { Text = text, ForeColor = SystemColors.GrayText, AutoSize = true, MaximumSize = new Size(InnerWidth - 4, 0) };

    static ToggleSwitch Toggle(string text, Action<bool> onChange)
    {
        var t = new ToggleSwitch { Text = text, AutoSize = true };
        t.CheckedChanged += (_, _) => onChange(t.Checked);
        return t;
    }

    /// <summary>퍼센트 슬라이더 + 현재 값 라벨. 값은 <paramref name="step"/> 단위로 맞춰진다(예: 137% → 135%).</summary>
    static Control Slider(out AccentSlider bar, int min, int max, int step, int page, Action<int> onChange)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        var s = new AccentSlider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = page, Width = 200, Margin = new Padding(0, 0, 8, 0) };
        var value = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0), Text = $"{s.Value}%", MinimumSize = new Size(40, 0) };
        s.ValueChanged += (_, _) => { value.Text = $"{s.Value}%"; onChange(s.Value); };
        panel.Controls.Add(s);
        panel.Controls.Add(value);
        bar = s;
        return panel;
    }

    /// <summary>색 견본 팔레트 + 현재 색의 16진수 표시.</summary>
    Control Swatches(out ColorSwatches swatches, out Label hex, Action<string> onChange)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        var s = new ColorSwatches { Margin = new Padding(0, 0, 8, 0) };
        var h = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 7, 0, 0), Font = Theme.HintFont(Font) };
        s.ColorChanged += (_, _) => { h.Text = s.Hex; onChange(s.Hex); Touch(); };
        panel.Controls.Add(s);
        panel.Controls.Add(h);
        swatches = s; hex = h;
        return panel;
    }

    /// <summary>초안을 컨트롤에 싣는다. 컨트롤마다 변경 이벤트가 오지만 끝에 한 번만 반영한다.</summary>
    void LoadDraftIntoControls()
    {
        _loading = true;
        try { FillControls(); }
        finally { _loading = false; }
        Touch();
    }

    void FillControls()
    {
        var design = DesignThemes.Get(_draft.Theme);
        _theme.SelectedValue = design.Id;
        _hangulColor.Presets = design.Swatches;
        _englishColor.Presets = design.Swatches;
        bool character = _draft.Character.Length > 0;
        _style.SelectedValue = character ? null : _draft.Style;
        _character.SelectedValue = character ? _draft.Character : null;
        _placement.SelectedValue = _draft.Placement;
        _size.Value = _draft.SizePercent;
        _opacity.Value = _draft.OpacityPercent;
        _poll.Value = Math.Clamp(_draft.PollIntervalMs, (int)_poll.Minimum, (int)_poll.Maximum);
        _hangulColor.Hex = _draft.HangulColor; _hangulHex.Text = _hangulColor.Hex;
        _englishColor.Hex = _draft.EnglishColor; _englishHex.Text = _englishColor.Hex;
        _animate.Checked = _draft.Animate;
        _capsLock.Checked = _draft.ShowCapsLock;
        _autostartBox.Checked = _autostart;
        _fullscreen.Checked = _draft.HideOnFullscreen;
        _trayStateBox.Checked = _draft.TrayShowsState;
        _hotkey.Checked = _draft.HotkeyEnabled;
        _hotkeyBox.Enabled = _draft.HotkeyEnabled;
        _hotkeyBox.Text = _draft.Hotkey;
        _updates.Checked = _draft.CheckForUpdates;
        _language.SelectedIndex = Math.Max(0, Array.FindIndex(Labels.Languages, l => l.value == _draft.Language));
        _trackImage.Checked = _draft.TrackCursorByImage;
        _excluded.Reload();
        _corner.Reload();
    }

    /// <summary>테마를 고르면 배지 색도 그 테마의 기본색으로, 견본도 그 테마의 것으로 바꾼다. 창은 <see cref="Touch"/> 에서 다시 칠한다.</summary>
    void ChooseTheme(DesignTheme design)
    {
        _draft.Theme = design.Id;
        _draft.HangulColor = design.HangulColor;
        _draft.EnglishColor = design.EnglishColor;
        LoadDraftIntoControls();
    }

    /// <summary>편집 내용을 실제 설정에 복사하고 알린다. 창 밖 배지가 바로 바뀐다.</summary>
    void Touch()
    {
        if (_loading) return;   // 컨트롤을 채우는 중에는 마지막에 한 번만
        _draft.Normalize();
        _live.CopyFrom(_draft);
        _dirty = true;
        Changed?.Invoke();   // BadgeForm 이 여기서 Strings.Setting 을 새 언어로 맞춘다
        var design = DesignThemes.Get(_draft.Theme);
        if (!ReferenceEquals(design, _painted)) { _painted = Theme.Design = design; Recolor(); }
        _style?.Invalidate();       // 타일은 배지 색을 쓴다
        _character?.Invalidate();
        _placement?.Invalidate();
        RefreshPreview();
        if (Strings.IsKorean != _builtKorean) LanguageChanged?.Invoke();
    }

    /// <summary>미리보기를 다시 그린다. DotFlash 면 실제 배지처럼 글자 배지로 시작해 1.5초 뒤 점으로 바뀐다.</summary>
    void RefreshPreview()
    {
        _flashTimer.Stop();
        _flashing = _draft.Style == BadgeStyle.DotFlash;
        if (_flashing) _flashTimer.Start();
        _preview?.Invalidate();
    }

    // ── 미리보기 ──
    // 작은 편집기처럼 여러 줄의 글을 놓고, 그중 두 줄(한글·영문) 끝에 caret 과 배지를 그린다.
    // 옆줄 글자가 있어야 배지가 무엇을 얼마나 가리는지(위치)와 얼마나 비치는지(불투명도)가 눈에 보인다.
    // 아래 값은 96 DPI 기준 픽셀이고 그릴 때 배율을 곱한다.
    const int PreviewHeight = 240;
    const int PreviewRowPitch = 28;      // 줄 간격. 100% 배지(약 26px)가 caret 줄과 옆줄 사이에 놓이며 옆줄 글자를 덮는다
    const int PreviewTopMargin = 26, PreviewLeftMargin = 18;   // 위쪽은 캡션("밝은 배경") 자리

    /// <summary>
    /// 미리보기 줄. 상태가 있는 줄은 글자 끝에 caret 과 그 상태의 배지를 그리고, 나머지는 옅은 색 채움 글이다.
    /// caret 줄 위아래로 두 줄씩 두어 200% 크기까지는 배지가 잘리지 않고 고른 위치("위"·"아래")에 그대로 놓인다.
    /// </summary>
    static readonly (string text, ImeState? state)[] PreviewRows =
    {
        ("the quick brown fox", null),
        ("over the lazy dog", null),
        ("안녕하세요", ImeState.Hangul),
        ("다람쥐 헌 쳇바퀴에", null),
        ("타고파 abc def ghi", null),
        ("hello world", ImeState.English),
        ("가나다라 마바사아", null),
        ("abcd efgh ijkl mnop", null),
    };

    // 미리보기 배경. 왼쪽은 흰 종이(메모장), 오른쪽은 어두운 편집기(VS Code 기본 테마)와 비슷한 색.
    static readonly Color LightBg = Color.White, LightText = Color.FromArgb(0x1B, 0x1B, 0x1B), LightCaption = Color.FromArgb(0x8A, 0x8A, 0x8A);
    static readonly Color DarkBg = Color.FromArgb(0x1E, 0x1E, 0x1E), DarkText = Color.FromArgb(0xD4, 0xD4, 0xD4), DarkCaption = Color.FromArgb(0x7A, 0x7A, 0x7A);

    /// <summary>미리보기에 그릴 모양. DotFlash 는 바뀐 직후엔 글자 배지, 1.5초 뒤엔 점(<see cref="_flashing"/>).</summary>
    BadgeStyle PreviewStyle => _draft.Style == BadgeStyle.DotFlash ? (_flashing ? BadgeStyle.Pill : BadgeStyle.Dot) : _draft.Style;

    /// <summary>
    /// 왼쪽 절반은 밝은 배경, 오른쪽 절반은 어두운 배경이라 어느 편집기에서 써도 어떻게 보일지 한 번에 확인할 수 있다.
    /// 배지는 실제 렌더러·위치 계산을 그대로 써서 창 밖 배지와 똑같이 보인다. 전체는 둥근 모서리 안에 그린다.
    /// </summary>
    void PaintPreview(Graphics g)
    {
        float dpi = DeviceDpi / 96f;
        var client = _preview.ClientRectangle;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(_preview.Parent?.BackColor ?? Theme.Current.Card);

        using var clip = RoundedPath(new RectangleF(0.5f, 0.5f, client.Width - 1, client.Height - 1), 6 * dpi);
        var saved = g.Save();
        g.SetClip(clip);
        int half = client.Width / 2;
        PaintPreviewHalf(g, new Rectangle(client.Left, client.Top, half, client.Height), LightBg, LightText, LightCaption, Strings.Get("preview.light"), dpi);
        PaintPreviewHalf(g, new Rectangle(client.Left + half, client.Top, client.Width - half, client.Height), DarkBg, DarkText, DarkCaption, Strings.Get("preview.dark"), dpi);
        g.Restore(saved);
        using var border = new Pen(Theme.Current.Border);
        g.DrawPath(border, clip);
    }

    void PaintPreviewHalf(Graphics g, Rectangle area, Color bg, Color fg, Color caption, string title, float dpi)
    {
        var saved = g.Save();
        g.SetClip(area, CombineMode.Intersect);   // 큰 배지가 반대쪽 배경으로 넘어가지 않게
        try
        {
            using (var bgBrush = new SolidBrush(bg)) g.FillRectangle(bgBrush, area);
            using (var capFont = Theme.HintFont(Font))
                TextRenderer.DrawText(g, title, capFont, new Rectangle(area.Left, area.Top + (int)(6 * dpi), area.Width - (int)(10 * dpi), (int)(16 * dpi)), caption,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            using var font = new Font(BadgeRenderer.FontFamily, 11f);
            // 기본 StringFormat 은 글자 양옆에 여백을 더해 caret 이 마지막 글자에서 떨어져 보인다(배지가 왼쪽인지 오른쪽인지 헷갈림).
            using var fmt = new StringFormat(StringFormat.GenericTypographic);
            using var textBrush = new SolidBrush(fg);
            using var fillerBrush = new SolidBrush(Mix(fg, bg, 0.55f));
            using var caretPen = new Pen(fg, Math.Max(1f, (float)Math.Round(dpi)));
            int lineH = (int)Math.Ceiling(font.GetHeight(g));

            // 1) 글과 caret 을 먼저 모두 그린다. 배지는 그 위에 얹혀야 하므로(실제로도 배지는 최상위 창이다) 나중에 그린다.
            //    Caps Lock 표시를 켰으면 영문 줄을 대문자 예시로 바꿔 "A + 밑줄" 배지도 미리 보여 준다(한글 줄은 평소 모습).
            var carets = new List<(ImeState state, bool caps, Rectangle caret)>();
            for (int r = 0; r < PreviewRows.Length; r++)
            {
                var (text, state) = PreviewRows[r];
                float x = area.Left + PreviewLeftMargin * dpi, y = area.Top + (PreviewTopMargin + r * PreviewRowPitch) * dpi;
                if (state is null) { g.DrawString(text, font, fillerBrush, x, y, fmt); continue; }

                bool caps = state == ImeState.English && _draft.ShowCapsLock;
                if (caps) text = text.ToUpperInvariant();
                g.DrawString(text, font, textBrush, x, y, fmt);
                float w = g.MeasureString(text, font, PointF.Empty, fmt).Width;
                var caret = new Rectangle((int)Math.Round(x + w + dpi), (int)Math.Round(y), 1, lineH);
                g.DrawLine(caretPen, caret.Left, caret.Top, caret.Left, caret.Bottom);
                carets.Add((state.Value, caps, caret));
            }

            // 2) 배지. 미리보기에서는 화면 가장자리 대피(위에 자리가 없으면 아래로, 왼쪽에 없으면 오른쪽으로)를 하지 않는다.
            //    고른 위치를 그대로 지켜야 위/아래·왼쪽/오른쪽 차이가 보인다. 아주 크면(300%) 가장자리에서 잘려 보일 수 있다.
            float scale = dpi * _draft.SizePercent / 100f;
            var theme = BadgeTheme.From(_draft);
            var style = PreviewStyle;
            var room = Rectangle.Inflate(area, 4096, 4096);
            foreach (var (state, caps, caret) in carets)
            {
                using var bmp = BadgeRenderer.Render(state, style, scale, theme, _draft.OpacityPercent, caps);
                var pos = BadgeLayout.Compute(new LayoutInput(caret, bmp.Size, style, _draft.Placement, scale, room));
                // 픽셀 크기를 명시한다. Point 만 주는 오버로드는 비트맵의 DPI(96)와 화면 DPI 차이만큼 확대해 버린다.
                g.DrawImage(bmp, new Rectangle(pos, bmp.Size), new Rectangle(Point.Empty, bmp.Size), GraphicsUnit.Pixel);
            }
        }
        finally { g.Restore(saved); }
    }

    /// <summary><paramref name="a"/> 에서 <paramref name="b"/> 쪽으로 <paramref name="t"/>(0~1)만큼 섞은 색.</summary>
    static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    static GraphicsPath RoundedPath(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>[확인]: 자동 시작을 반영하고 저장을 알린다. 배지 설정은 이미 실제 설정에 들어가 있다.</summary>
    void Apply()
    {
        _live.CopyFrom(_draft);
        if (_autostart != Autostart.IsEnabled()) Autostart.Set(_autostart);
        _dirty = false;
        Applied?.Invoke();
    }

    /// <summary>[취소]·닫기: 창을 열 때의 설정으로 되돌리고 저장을 알린다(그 사이 트레이 메뉴가 저장했을 수 있다).</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _detached || DialogResult == DialogResult.OK || !_dirty) return;
        _live.CopyFrom(_original);
        _dirty = false;
        Applied?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _flashTimer.Dispose(); _tips.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 미리보기 패널. 슬라이더를 끌 때마다 통째로 다시 그리므로 이중 버퍼가 없으면 배경이 먼저 지워지며 깜빡인다.
/// </summary>
sealed class PreviewPanel : Panel
{
    public PreviewPanel() { DoubleBuffered = true; }
}

/// <summary>
/// 프로세스 이름 목록 편집기: 목록 + (실행 중인 앱 콤보박스, 추가, 삭제) + 안내문.
/// "배지를 띄우지 않을 앱" 과 "모서리에 표시할 앱" 이 함께 쓴다. 목록은 <see cref="_items"/> 로 매번 가져오고(설정 초안의 List),
/// 추가·삭제하면 <see cref="Changed"/> 로 알린다. Enter 로 추가, Delete 로 삭제.
/// </summary>
sealed class ProcessListEditor : TableLayoutPanel
{
    readonly Func<List<string>> _items;
    readonly ListBox _list;
    readonly ComboBox _input;

    /// <summary>목록이 바뀌었다(추가·삭제).</summary>
    public event Action? Changed;

    public ProcessListEditor(Func<List<string>> items, int width, ToolTip tips, string addText, string removeText, string inputTip, string note)
    {
        _items = items;
        ColumnCount = 1; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _list = new ListBox { Width = width - 6, Height = 72, IntegralHeight = false };
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveItem(); };
        AddRow(_list);

        // 실행 중인 앱에서 고르거나 이름을 직접 쓴다. 목록은 펼칠 때마다 새로 읽는다.
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        _input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 196, Margin = new Padding(0, 2, 8, 0) };
        _input.DropDown += (_, _) => FillRunningApps();
        _input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddItem(); e.SuppressKeyPress = true; } };
        tips.SetToolTip(_input, inputTip);
        var add = new AccentButton { Text = addText, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        add.Click += (_, _) => AddItem();
        var remove = new AccentButton { Text = removeText, AutoSize = true, Margin = Padding.Empty };
        remove.Click += (_, _) => RemoveItem();
        row.Controls.Add(_input); row.Controls.Add(add); row.Controls.Add(remove);
        AddRow(row);

        AddRow(new Label { Text = note, ForeColor = SystemColors.GrayText, AutoSize = true, MaximumSize = new Size(width - 4, 0) });
    }

    void AddRow(Control c)
    {
        int row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        c.Margin = new Padding(0, 4, 0, 4);
        Controls.Add(c, 0, row);
    }

    /// <summary>설정 초안의 목록을 다시 싣는다(창을 열 때, 기본값 복원 때).</summary>
    public void Reload()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in _items()) _list.Items.Add(p);
        _list.EndUpdate();
    }

    void FillRunningApps()
    {
        System.Diagnostics.Process[]? procs = null;
        try
        {
            procs = System.Diagnostics.Process.GetProcesses();
            var names = procs
                .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })   // 창이 있는 앱만
                .Select(p => p.ProcessName)
                .Where(n => !string.Equals(n, AppInfo.ProductName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string typed = _input.Text;
            _input.Items.Clear();
            _input.Items.AddRange(names);
            _input.Text = typed;
        }
        catch (Exception ex) { Log.Error("list running apps failed", ex); }
        finally { if (procs is not null) foreach (var p in procs) p.Dispose(); }
    }

    void AddItem()
    {
        string name = _input.Text.Trim();
        if (name.Length == 0) return;
        var items = _items();
        if (!items.Any(x => string.Equals(ProcessFilter.Normalize(x), ProcessFilter.Normalize(name), StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(name);
            Reload();
            Changed?.Invoke();
        }
        _input.Text = "";
        _input.Focus();
    }

    void RemoveItem()
    {
        int i = _list.SelectedIndex;
        var items = _items();
        if (i < 0 || i >= items.Count) return;
        items.RemoveAt(i);
        Reload();
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(i, _list.Items.Count - 1);
        Changed?.Invoke();
    }
}

/// <summary>
/// 키 조합을 받아 "Ctrl+Alt+H" 로 보여 주는 입력칸. 글자를 타이핑하는 곳이 아니라 눌린 키를 읽는 곳이라
/// 한글 IME 를 끄고(ImeMode.Disable), 잘라내기·붙여넣기 단축키와 Alt 니모닉이 가로채지 못하게 막는다.
/// 보조키(Ctrl/Alt/Shift)만 누른 상태는 무시하고, 유효한 조합(<see cref="HotkeySpec.IsValid"/>)이 완성될 때만 알린다.
/// </summary>
sealed class HotkeyBox : TextBox
{
    public event Action<HotkeySpec>? HotkeyChanged;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;   // ReadOnly 의 회색(비활성처럼 보임) 대신 보통 입력칸 색
        ShortcutsEnabled = false;
        ImeMode = ImeMode.Disable;
        Cursor = Cursors.Hand;
        TextAlign = HorizontalAlignment.Center;
        PlaceholderText = Strings.Get("behavior.hotkey.placeholder");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        var code = e.KeyCode;
        if (code is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin or Keys.None) return;   // 보조키만 눌림
        var mods = HotkeyModifiers.None;
        if (e.Control) mods |= HotkeyModifiers.Control;
        if (e.Alt) mods |= HotkeyModifiers.Alt;
        if (e.Shift) mods |= HotkeyModifiers.Shift;
        var spec = new HotkeySpec(mods, (int)code);
        if (!spec.IsValid) { System.Media.SystemSounds.Beep.Play(); return; }
        Text = spec.ToString();
        HotkeyChanged?.Invoke(spec);
    }

    // Alt+글자가 폼의 니모닉(예: Alt+K)으로 새지 않게, 이 칸에 포커스가 있는 동안은 여기서 끝낸다.
    // (Tab·Enter·Esc 는 KeyDown 전에 ProcessDialogKey 가 처리하므로 포커스 이동과 확인·취소는 그대로 동작한다.)
    protected override bool ProcessDialogChar(char charCode) => Focused || base.ProcessDialogChar(charCode);
}
