using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>설정 창의 페이지. 순서가 왼쪽 탐색 목록의 순서다.</summary>
enum SettingsPage { Appearance, Display, General, Excluded, About }

/// <summary>
/// 설정 창. Windows 11 설정 앱처럼 왼쪽 탐색 목록에서 페이지를 고르고, 페이지마다 아이콘·제목·설명이 붙은 카드로 항목을 보여 준다.
/// 바꾸는 즉시 실제 배지에 반영되어(WYSIWYG) 창 밖 배지로 결과를 바로 볼 수 있고,
/// [확인]을 누르면 저장, [취소]나 닫기를 누르면 창을 열 때의 설정으로 되돌린다.
/// 편집은 복사본(<see cref="_draft"/>)에 하고 매번 실제 설정(<see cref="_live"/>)으로 복사한다.
/// 미리보기는 실제 렌더러(<see cref="BadgeRenderer"/>)로 그려 창 밖 배지와 똑같이 보인다.
/// 뼈대 컨트롤(탐색 목록·카드·페이지)은 SettingsControls.cs 에 있다.
/// </summary>
sealed class SettingsForm : Form
{
    readonly Settings _live;
    readonly Settings _draft;
    readonly Settings _original;   // 취소할 때 되돌릴 값
    readonly AppPaths _paths;
    readonly Action _checkUpdates;
    readonly bool _builtKorean = Strings.IsKorean;   // 이 창을 만들 때의 언어. 바뀌면 새 창으로 갈아 끼운다
    bool _autostart, _dirty, _loading, _detached;

    TilePicker _theme = null!, _style = null!, _character = null!, _placement = null!;
    AccentSlider _size = null!, _opacity = null!, _poll = null!;
    ThemedComboBox _language = null!, _visibility = null!;
    InputTextBox _excludeInput = null!;
    InputFrame _excludeFrame = null!;
    ContextMenuStrip? _appsMenu;
    ColorSwatches _hangulColor = null!, _englishColor = null!;
    Label _hangulHex = null!, _englishHex = null!;
    ToggleSwitch _autostartBox = null!, _fullscreen = null!, _hotkey = null!, _updates = null!, _trayStateBox = null!, _animate = null!, _capsLock = null!, _shiftHold = null!;
    HotkeyBox _hotkeyBox = null!;
    PreviewPanel _preview = null!;
    SettingsCard _updateCard = null!, _diagCard = null!;
    SectionHeader _excludeHeader = null!;
    CardStack _excludePage = null!;
    readonly List<Control> _excludeRows = new();

    NavList _nav = null!;
    PageHeader _header = null!;
    CommandBar _commands = null!;
    readonly CardStack[] _pages = new CardStack[Pages.Length];

    readonly ToolTip _tips = new() { AutoPopDelay = 12000 };
    readonly Font _sectionFont, _hintFont;
    // "점, 바뀔 때 1.5초 글자" 미리보기용. 실제 배지처럼 설정이 바뀐 직후 1.5초는 글자 배지를, 그 뒤엔 점을 보여 준다.
    readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = BadgeForm.FlashMs };
    bool _flashing;
    // "진단 정보 복사" 를 누른 뒤 잠깐 "복사했습니다" 를 보여 준다.
    readonly System.Windows.Forms.Timer _copiedTimer = new() { Interval = 2500 };

    // 언어를 바꿔 새 창으로 갈아 끼울 때 이어받는 창 상태(Reopen).
    Rectangle? _restoreBounds;
    bool _restoreMaximized, _focusLanguage;
    int _restoreScroll;

    /// <summary>페이지마다 탐색 목록의 아이콘과 문구 키(nav.{key}, page.{key}.desc). 순서는 <see cref="SettingsPage"/> 와 같다.</summary>
    static readonly (string glyph, string key)[] Pages =
    {
        (Glyphs.Appearance, "appearance"), (Glyphs.Display, "display"), (Glyphs.General, "general"), (Glyphs.Apps, "excluded"), (Glyphs.Info, "about"),
    };

    // 창 크기(96 DPI 기준 논리 픽셀). 처음에는 DefaultClient 로 열되 작업 영역보다 크면 줄이고, MinClient 보다 작게는 줄일 수 없다.
    // 최소 폭은 타일 다섯 개(테마·모양)가 카드 안에 잘리지 않고 들어가는 폭이다.
    static readonly Size DefaultClient = new(1040, 760), MinClient = new(800, 540);
    const int NavWidth = 264, CommandHeight = 64;

    /// <summary>편집 중 값이 바뀔 때마다 발생. 실제 설정에는 이미 복사되어 있으니 다시 그리기만 하면 된다(저장은 하지 않는다).</summary>
    public event Action? Changed;
    /// <summary>[확인]으로 확정되었거나 [취소]로 되돌려졌을 때 발생. 저장한다.</summary>
    public event Action? Applied;
    /// <summary>UI 언어가 이 창을 만들 때와 달라졌다. 받는 쪽(BadgeForm)이 <see cref="Reopen"/> 으로 새 창을 만들고 이 창을 <see cref="Detach"/> 한다.</summary>
    public event Action? LanguageChanged;

    /// <param name="checkUpdates">"정보" 페이지의 [새 버전 확인]. 트레이 메뉴의 업데이트 확인과 같다.</param>
    public SettingsForm(Settings live, AppPaths paths, Action checkUpdates, SettingsPage page = SettingsPage.Appearance)
        : this(live, live.Clone(), dirty: false, paths, checkUpdates, page) { }

    /// <param name="original">취소할 때 되돌릴 값. 새 창이 이전 창의 기준을 이어받을 때 넘긴다.</param>
    /// <param name="dirty">이미 편집이 있었는가(이어받은 창은 true).</param>
    SettingsForm(Settings live, Settings original, bool dirty, AppPaths paths, Action checkUpdates, SettingsPage page)
    {
        _live = live;
        _draft = live.Clone();
        _original = original;
        _paths = paths;
        _checkUpdates = checkUpdates;
        _autostart = Autostart.IsEnabled();

        Text = Strings.Format("settings.title", AppInfo.ProductName);
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true; MinimizeBox = true; ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;   // 위치와 크기는 OnLoad 의 Place 가 정한다
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);   // 아래 픽셀 크기들은 96 DPI 기준. 고DPI 에서 WinForms 가 배율을 곱한다
        // 본문은 시스템 대화상자 글꼴보다 조금 크게(10pt): Windows 11 설정 앱의 본문 크기에 가깝다.
        using (var dialog = Theme.DialogFont) Font = new Font(dialog.FontFamily, Math.Max(10f, dialog.SizeInPoints));
        _sectionFont = Theme.SectionFont(Font);
        _hintFont = Theme.HintFont(Font);
        ClientSize = DefaultClient;

        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); _flashing = false; _preview.Invalidate(); };
        _copiedTimer.Tick += (_, _) => { _copiedTimer.Stop(); if (!_diagCard.IsDisposed) _diagCard.Description = Strings.Get("about.copyDiag.desc"); };

        Build(page);
        LoadDraftIntoControls();
        _dirty = dirty;   // 컨트롤 초기화로 생긴 변경 알림은 실제 변경이 아니다
        UpdateHint();
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

    /// <summary>같은 편집 상태(기준값·자동 시작 체크)를 이어받는 새 창을 현재 언어로 만든다. 위치·크기·페이지·스크롤도 그대로.</summary>
    public SettingsForm Reopen()
    {
        var next = new SettingsForm(_live, _original, dirty: true, _paths, _checkUpdates, CurrentPage) { _autostart = _autostart };
        next._autostartBox.Checked = _autostart;
        if (IsHandleCreated)
        {
            next._restoreBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            next._restoreMaximized = WindowState == FormWindowState.Maximized;
            next._restoreScroll = -CurrentStack.AutoScrollPosition.Y;
            next._focusLanguage = ActiveControl == _language;
        }
        return next;
    }

    /// <summary>닫을 때 되돌리기·저장 알림을 하지 않게 한다(새 창에 편집을 넘긴 뒤).</summary>
    public void Detach() => _detached = true;

    public SettingsPage CurrentPage => (SettingsPage)Math.Max(0, _nav.SelectedIndex);
    CardStack CurrentStack => _pages[(int)CurrentPage];

    /// <summary>다른 페이지로 옮긴다(트레이 메뉴의 "정보").</summary>
    public void ShowPage(SettingsPage page) => _nav.SelectedIndex = (int)page;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Place();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_restoreScroll > 0) CurrentStack.AutoScrollPosition = new Point(0, _restoreScroll);
        if (_focusLanguage) _language.Focus();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (CurrentPage == SettingsPage.About) RefreshUpdateCard();   // [새 버전 확인] 의 결과 대화상자를 닫고 돌아왔을 때
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(() => { if (!IsDisposed) MinimumSize = MinimumFor(Screen.FromControl(this).WorkingArea); });   // 배율이 다른 모니터로 옮겼다
    }

    /// <summary>
    /// 처음 열 때: 마우스가 있는 모니터(트레이 아이콘을 누른 곳)의 가운데에 기본 크기로, 작업 영역(작업 표시줄 제외)을 넘지 않게.
    /// 언어를 바꿔 다시 연 창은 이전 창의 자리와 크기를 그대로 쓴다. 해상도가 낮으면 내용 영역이 스크롤된다.
    /// </summary>
    void Place()
    {
        var frame = Size - ClientSize;   // 제목 표시줄·테두리
        var area = (_restoreBounds is { } prev ? Screen.FromRectangle(prev) : Screen.FromPoint(Cursor.Position)).WorkingArea;
        MinimumSize = MinimumFor(area);
        Rectangle bounds;
        if (_restoreBounds is { } r) bounds = r;
        else
        {
            int gap = LogicalToDeviceUnits(8);
            var want = new Size(LogicalToDeviceUnits(DefaultClient.Width), LogicalToDeviceUnits(DefaultClient.Height)) + frame;
            var size = new Size(Math.Min(want.Width, area.Width - 2 * gap), Math.Min(want.Height, area.Height - 2 * gap));
            bounds = new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2, size.Width, size.Height);
        }
        bounds.Width = Math.Min(bounds.Width, area.Width);
        bounds.Height = Math.Min(bounds.Height, area.Height);
        bounds.X = Math.Clamp(bounds.X, area.Left, area.Right - bounds.Width);
        bounds.Y = Math.Clamp(bounds.Y, area.Top, area.Bottom - bounds.Height);
        Bounds = bounds;
        if (_restoreMaximized) WindowState = FormWindowState.Maximized;
    }

    Size MinimumFor(Rectangle area)
    {
        var frame = Size - ClientSize;
        var min = new Size(LogicalToDeviceUnits(MinClient.Width), LogicalToDeviceUnits(MinClient.Height)) + frame;
        return new Size(Math.Min(min.Width, area.Width), Math.Min(min.Height, area.Height));
    }

    /// <summary>Ctrl+Tab·Ctrl+Shift+Tab(또는 Ctrl+PageDown·PageUp)으로 다음·이전 페이지. 단축키 입력칸에서는 그 키 조합을 입력으로 받는다.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        int step = keyData switch
        {
            Keys.Control | Keys.Tab or Keys.Control | Keys.PageDown => 1,
            Keys.Control | Keys.Shift | Keys.Tab or Keys.Control | Keys.PageUp => -1,
            _ => 0,
        };
        if (step != 0 && ActiveControl is not HotkeyBox)
        {
            _nav.SelectedIndex = (_nav.SelectedIndex + step + Pages.Length) % Pages.Length;
            _nav.Focus();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── 화면 구성 ──
    // 창: 왼쪽 탐색 창(NavPane) + 오른쪽 내용(content). 내용은 위에 페이지 제목(PageHeader), 가운데 페이지(CardStack, 고른 것만 보임),
    // 아래에 늘 보이는 [확인]/[취소] 줄(CommandBar). Dock 은 나중에 추가한 것부터 자리를 잡으므로 채우는(Fill) 것을 먼저 넣는다.
    // 페이지 안의 카드는 CardStack 이 직접 쌓는다(SettingsControls.cs 설명 참고). 크기는 96 DPI 기준이고 고DPI 에서는 배율을 곱한다.
    void Build(SettingsPage page)
    {
        _nav = new NavList { TabIndex = 0 };
        _nav.SetItems(Pages.Select(p => new NavList.Item(p.glyph, Strings.Get("nav." + p.key))));
        var navPane = new NavPane(_nav, AppInfo.ProductName, $"v{AppVersion.Display}", new Font(Font, FontStyle.Bold))
        {
            Dock = DockStyle.Left, Width = NavWidth, TabIndex = 0,
        };

        var ok = new AccentButton { Text = Strings.Get("settings.ok"), Primary = true, AutoSize = true };
        var cancel = new AccentButton { Text = Strings.Get("settings.cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        // 모드리스(Show) 창은 DialogResult 만으로는 닫히지 않는다. 명시적으로 닫는다.
        ok.Click += (_, _) => { Apply(); DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        AcceptButton = ok; CancelButton = cancel;
        _commands = new CommandBar(Strings.Get("settings.hint.clean"), cancel, ok) { Dock = DockStyle.Bottom, Height = CommandHeight, TabIndex = 1 };

        _pages[(int)SettingsPage.Appearance] = BuildAppearancePage();
        _pages[(int)SettingsPage.Display] = BuildDisplayPage();
        _pages[(int)SettingsPage.General] = BuildGeneralPage();
        _pages[(int)SettingsPage.Excluded] = BuildExcludedPage();
        _pages[(int)SettingsPage.About] = BuildAboutPage();

        _header = new PageHeader { Dock = DockStyle.Top };
        var content = new Panel { Dock = DockStyle.Fill, TabIndex = 1 };
        foreach (var p in _pages) { p.Dock = DockStyle.Fill; p.Visible = false; p.TabIndex = 0; content.Controls.Add(p); }
        content.Controls.Add(_header);
        content.Controls.Add(_commands);

        Controls.Add(content);
        Controls.Add(navPane);

        _nav.SelectedIndex = (int)page;
        SelectPage();
        _nav.SelectedChanged += (_, _) => SelectPage();
    }

    /// <summary>탐색 목록에서 고른 페이지만 보이고, 제목과 설명을 그 페이지의 것으로 바꾼다. 새 페이지는 맨 위부터.</summary>
    void SelectPage()
    {
        int index = Math.Max(0, _nav.SelectedIndex);
        var key = Pages[index].key;
        _header.Text = Strings.Get("nav." + key);
        _header.Description = Strings.Get("page." + key + ".desc");
        for (int i = 0; i < _pages.Length; i++)
            if (i != index) _pages[i].Visible = false;
        var page = _pages[index];
        page.Visible = true;
        page.ScrollToTop();
        page.PerformLayout();
        if (index == (int)SettingsPage.About) RefreshUpdateCard();
    }

    CardStack BuildAppearancePage()
    {
        var page = new CardStack();

        _preview = new PreviewPanel { Height = PreviewHeight, Tag = "custom-paint" };
        _preview.Paint += (_, e) => PaintPreview(e.Graphics);
        _tips.SetToolTip(_preview, Strings.Get("preview.tip"));
        page.Controls.Add(new SettingsCard
        {
            Glyph = Glyphs.Preview, Title = Strings.Get("group.preview"), Description = Strings.Get("group.preview.desc"),
            StretchBody = true, Body = _preview,
        });

        page.Controls.Add(Section("section.themeShape"));
        // 테마: 배지 색·질감과 이 창의 색을 한 벌로 바꾼다. 타일에는 그 테마의 한/영 배지를 실제 렌더러로 그린다.
        _theme = new TilePicker { TileSize = new Size(68, 58) };
        _theme.SetTiles(DesignThemes.All.Select(d => new TilePicker.Tile(d.Id, Strings.Get("theme." + d.Id), (gr, r, p) => DrawThemeTile(gr, r, d))));
        _theme.SelectedChanged += (_, _) => { if (_theme.SelectedValue is string id && id != _draft.Theme) ChooseTheme(DesignThemes.Get(id)); };
        page.Controls.Add(Card(Glyphs.Theme, "look.theme", body: _theme));

        // 배지 모양: 실제 렌더러로 그린 타일에서 고른다. 윗줄은 기본 모양, 아랫줄은 캐릭터. 둘 중 한 줄에서만 선택된다.
        _style = new TilePicker { TileSize = new Size(68, 58), AccessibleName = Strings.Get("menu.style.basic"), Margin = new Padding(0, 0, 0, 8) };
        _style.SetTiles(Labels.Styles.Select(s => new TilePicker.Tile(s.value, ShortStyle(s.value), (gr, r, p) => DrawStyleTile(gr, r, p, s.value))));
        _style.SelectedChanged += (_, _) =>
        {
            if (_style.SelectedValue is not BadgeStyle v) return;
            _draft.Style = v;
            _draft.Character = BadgeCharacters.None;
            _character.SelectedValue = null;
            SyncPlacementEnabled();
            Touch();
        };
        _character = new TilePicker { TileSize = new Size(68, 58), AccessibleName = Strings.Get("menu.style.characters"), Margin = Padding.Empty };
        _character.SetTiles(Labels.Characters.Select(c => new TilePicker.Tile(c.id, c.label, (gr, r, p) => DrawCharacterTile(gr, r, p, c.id))));
        _character.SelectedChanged += (_, _) =>
        {
            if (_character.SelectedValue is not string id) return;
            _draft.Character = id;
            _draft.Style = BadgeStyle.Pill;   // 구버전으로 되돌려도 둥근 배지로 보이게
            _style.SelectedValue = null;
            SyncPlacementEnabled();
            Touch();
        };
        page.Controls.Add(Card(Glyphs.Shape, "look.style", body: Stack(_style, _character)));

        _placement = new TilePicker { TileSize = new Size(68, 58) };
        _placement.SetTiles(Labels.Placements.Select(pl => new TilePicker.Tile(pl.value, ShortPlacement(pl.value), (gr, r, p) => DrawPlacementTile(gr, r, p, pl.value))));
        _placement.SelectedChanged += (_, _) => { if (_placement.SelectedValue is BadgePlacement v) { _draft.Placement = v; Touch(); } };
        page.Controls.Add(Card(Glyphs.Position, "look.placement", body: _placement));

        page.Controls.Add(Section("section.sizeColor"));
        // 크기·불투명도는 눈으로 맞추는 값이라 숫자 입력보다 슬라이더가 자연스럽다(Windows 설정 앱과 같은 방식).
        page.Controls.Add(Card(Glyphs.Size, "look.size", action: Slider(out _size, 50, 300, 5, 25, "%", v => { _draft.SizePercent = v; Touch(); })));
        page.Controls.Add(Card(Glyphs.Opacity, "look.opacity", action: Slider(out _opacity, 30, 100, 5, 10, "%", v => { _draft.OpacityPercent = v; Touch(); })));
        page.Controls.Add(Card(Glyphs.Color, "look.hangulColor", body: Swatches(out _hangulColor, out _hangulHex, v => _draft.HangulColor = v)));
        page.Controls.Add(Card(Glyphs.Color, "look.englishColor", body: Swatches(out _englishColor, out _englishHex, v => _draft.EnglishColor = v)));
        _tips.SetToolTip(_hangulColor, Strings.Get("look.swatch.tip"));
        _tips.SetToolTip(_englishColor, Strings.Get("look.swatch.tip"));
        if (SystemInformation.HighContrast)   // 배지는 시스템 색(BadgeTheme.From). 견본은 그대로 저장되고 고대비를 끄면 쓰인다
            page.Controls.Add(Note(Strings.Get("look.highContrastNote")));
        return page;
    }

    CardStack BuildDisplayPage()
    {
        var page = new CardStack();

        _visibility = Combo(240, Labels.Visibilities.Select(v => v.label), i => { _draft.Visibility = Labels.Visibilities[i].value; Touch(); });
        _tips.SetToolTip(_visibility, Strings.Get("look.visibility.tip"));
        page.Controls.Add(Card(Glyphs.Visibility, "look.visibility", action: _visibility));

        _animate = Toggle(v => { _draft.Animate = v; Touch(); });
        page.Controls.Add(Card(Glyphs.Animation, "look.animate", action: _animate));

        page.Controls.Add(Section("section.capsShift"));
        _capsLock = Toggle(v => { _draft.ShowCapsLock = v; _shiftHold.Enabled = v; Touch(); });
        _tips.SetToolTip(_capsLock, Strings.Get("look.capsLock.tip"));
        page.Controls.Add(Card(Glyphs.CapsLock, "look.capsLock", action: _capsLock));
        // Caps Lock 표시의 하위 옵션: 대소문자를 글자로 구별할 때만 의미가 있다. 들여 쓰고, Caps Lock 표시가 꺼져 있으면 흐리게.
        _shiftHold = Toggle(v => { _draft.ShowShiftHold = v; Touch(); });
        _tips.SetToolTip(_shiftHold, Strings.Get("look.shiftHold.tip"));
        var shift = Card(Glyphs.Shift, "look.shiftHold", action: _shiftHold);
        shift.Indent = true;
        page.Controls.Add(shift);

        page.Controls.Add(Section("section.hideTray"));
        _fullscreen = Toggle(v => { _draft.HideOnFullscreen = v; Touch(); });
        page.Controls.Add(Card(Glyphs.FullScreen, "behavior.fullscreen", action: _fullscreen));
        _trayStateBox = Toggle(v => { _draft.TrayShowsState = v; Touch(); });
        page.Controls.Add(Card(Glyphs.Tray, "behavior.trayState", action: _trayStateBox));
        return page;
    }

    CardStack BuildGeneralPage()
    {
        var page = new CardStack();

        page.Controls.Add(Section("section.startup"));
        // 자동 시작은 레지스트리를 만지므로 [확인] 때만 반영한다.
        _autostartBox = Toggle(v => { _autostart = v; if (!_loading) { _dirty = true; UpdateHint(); } });
        page.Controls.Add(Card(Glyphs.Power, "behavior.autostart", action: _autostartBox));

        // 단축키: 키 조합을 받는 입력칸 + 켜기/끄기.
        _hotkeyBox = new HotkeyBox { AccessibleName = Mnemonic.Plain(Strings.Get("behavior.hotkey")) };
        _hotkeyBox.HotkeyChanged += spec => { _draft.Hotkey = spec.ToString(); Touch(); };
        _tips.SetToolTip(_hotkeyBox, Strings.Get("behavior.hotkey.tip"));
        _hotkey = Toggle(v => { _draft.HotkeyEnabled = v; _hotkeyBox.Enabled = v; Touch(); });
        _hotkey.AccessibleName = Mnemonic.Plain(Strings.Get("behavior.hotkey"));
        var hotkeyFrame = new InputFrame(_hotkeyBox) { Width = 170, Margin = new Padding(0, 0, 16, 0) };
        page.Controls.Add(Card(Glyphs.Keyboard, "behavior.hotkey", action: Row(hotkeyFrame, _hotkey)));

        page.Controls.Add(Section("section.updatesLanguage"));
        _updates = Toggle(v => { _draft.CheckForUpdates = v; Touch(); });
        page.Controls.Add(Card(Glyphs.Update, "behavior.updates", action: _updates));
        _language = Combo(200, Labels.Languages.Select(l => l.label), i => { _draft.Language = Labels.Languages[i].value; Touch(); });
        page.Controls.Add(Card(Glyphs.Language, "behavior.language", action: _language));

        page.Controls.Add(Section("section.advanced"));
        page.Controls.Add(Card(Glyphs.Clock, "behavior.poll", action: Slider(out _poll, 50, 1000, 50, 100, " ms", v => { _draft.PollIntervalMs = v; Touch(); })));
        _tips.SetToolTip(_poll, Strings.Get("behavior.poll.tip"));

        page.Controls.Add(Section("section.manage"));
        // 내보내기·가져오기: 다른 PC 로 설정을 옮긴다. 가져온 값은 초안에만 채우므로 [확인] 해야 저장되고 [취소] 하면 되돌아간다.
        var import = Button("settings.import", ImportSettings);
        var export = Button("settings.export", ExportSettings);
        import.Margin = new Padding(0, 0, 8, 0);
        _tips.SetToolTip(import, Strings.Get("settings.import.tip"));
        _tips.SetToolTip(export, Strings.Get("settings.export.tip"));
        page.Controls.Add(Card(Glyphs.Save, "settings.backup", action: Row(import, export)));
        page.Controls.Add(Card(Glyphs.Reset, "settings.resetTitle", action: Button("settings.reset", ResetToDefaults)));
        return page;
    }

    CardStack BuildExcludedPage()
    {
        var page = _excludePage = new CardStack();

        // 이름을 직접 쓰거나(Enter 로 추가) 오른쪽 단추(Alt+↓)로 실행 중인 앱 목록을 펼쳐 고른다. 쓰는 동안 실행 중인 앱 이름을 자동 완성한다.
        _excludeInput = new InputTextBox
        {
            AccessibleName = Strings.Get("exclude.addTitle"), PlaceholderText = Strings.Get("exclude.placeholder"),
            AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource,
        };
        _excludeInput.GotFocus += (_, _) => { _excludeInput.AutoCompleteCustomSource.Clear(); _excludeInput.AutoCompleteCustomSource.AddRange(RunningAppNames()); };
        _excludeInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { AddExcluded(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) { ShowRunningApps(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        _tips.SetToolTip(_excludeInput, Strings.Get("exclude.new.tip"));
        var pick = new GlyphButton { Glyph = "\uE70D", AccessibleName = Strings.Get("exclude.running") };
        pick.Click += (_, _) => ShowRunningApps();
        _tips.SetToolTip(pick, Strings.Get("exclude.running"));
        _excludeFrame = new InputFrame(_excludeInput, pick) { Width = 280, Margin = new Padding(0, 0, 8, 0) };
        var add = Button("exclude.add", AddExcluded);
        add.Margin = Padding.Empty;
        page.Controls.Add(Card(Glyphs.Add, "exclude.addTitle", body: Row(_excludeFrame, add)));

        // 제외한 앱: 하나에 카드 하나, 오른쪽에 [삭제]. 목록은 ReloadExcluded 가 다시 만든다.
        _excludeHeader = Section("exclude.listHeader");
        page.Controls.Add(_excludeHeader);
        return page;
    }

    CardStack BuildAboutPage()
    {
        var page = new CardStack();

        Bitmap appIcon;
        using (var big = new Icon(Icons.App, 128, 128)) appIcon = big.ToBitmap();   // 큰 프레임을 줄여 그려 고DPI 에서도 또렷하게
        page.Controls.Add(new SettingsCard
        {
            Picture = appIcon, PictureSize = 48, Title = Strings.Get("app.name"), Description = Strings.Get("app.tagline"),
            Action = new VersionChip { Text = $"v{AppVersion.Display}" },
        });

        var check = new AccentButton { Text = Strings.Get("about.checkUpdates"), AutoSize = true };
        check.Click += (_, _) => _checkUpdates();
        _updateCard = new SettingsCard { Glyph = Glyphs.Update, Title = Strings.Get("about.update"), Action = check };
        page.Controls.Add(_updateCard);

        page.Controls.Add(Section("section.links"));
        page.Controls.Add(Link(Glyphs.Download, "about.releases", () => AboutInfo.Open(AppInfo.ReleasesUrl)));
        page.Controls.Add(Link(Glyphs.Link, "about.repo", () => AboutInfo.Open(AppInfo.RepoUrl)));

        page.Controls.Add(Section("section.troubleshoot"));
        page.Controls.Add(Link(Glyphs.Folder, "about.settingsDir", () => AboutInfo.OpenFolder(_paths.SettingsDir)));
        page.Controls.Add(Link(Glyphs.Folder, "about.logDir", () => AboutInfo.OpenFolder(_paths.LogDir)));
        // 진단 정보: 이슈에 붙여 넣으면 재현 환경을 바로 알 수 있다.
        _diagCard = Link(Glyphs.Copy, "about.copyDiag", CopyDiagnostics, trailing: "");
        page.Controls.Add(_diagCard);

        page.Controls.Add(Note(Strings.Get("about.license")));
        return page;
    }

    /// <summary>"정보" 페이지의 업데이트 카드 설명: 마지막으로 확인한 시각.</summary>
    void RefreshUpdateCard()
    {
        var last = _live.LastUpdateCheckUtc;
        _updateCard.Description = last is { } t
            ? Strings.Format("about.lastCheck", t.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
            : Strings.Get("about.neverChecked");
    }

    void CopyDiagnostics()
    {
        try
        {
            Clipboard.SetText(AboutInfo.Diagnostics(_paths, _live, DeviceDpi));
            _diagCard.Description = Strings.Get("about.copied");
            _copiedTimer.Stop();
            _copiedTimer.Start();
        }
        catch (Exception ex) { Log.Error("clipboard failed", ex); }
    }

    void ResetToDefaults()
    {
        // 고른 테마는 그대로 두고, 나머지와 배지 색을 그 테마의 기본값으로.
        var design = DesignThemes.Get(_draft.Theme);
        _draft.CopyFrom(new Settings { Theme = design.Id, HangulColor = design.HangulColor, EnglishColor = design.EnglishColor });
        LoadDraftIntoControls();
    }

    // ── 제외 앱 ──
    /// <summary>제외 앱 카드들을 초안의 목록으로 다시 만든다. 목록은 초안(_draft)의 List 를 매번 읽는다("기본값 복원" 이 List 인스턴스를 바꾼다).</summary>
    void ReloadExcluded()
    {
        var page = _excludePage;
        page.SuspendLayout();
        foreach (var c in _excludeRows) { page.Controls.Remove(c); c.Dispose(); }
        _excludeRows.Clear();
        var items = _draft.ExcludedProcesses;
        _excludeHeader.Text = Strings.Format("exclude.listHeader", items.Count);
        if (items.Count == 0) _excludeRows.Add(Note(Strings.Get("exclude.empty")));
        foreach (var name in items)
        {
            string item = name;
            var remove = new AccentButton { Text = Strings.Get("exclude.removeItem"), AutoSize = true, AccessibleName = Strings.Format("exclude.removeItem.name", name) };
            // 눌린 버튼이 든 카드를 지우므로 클릭 처리가 끝난 뒤에 지운다.
            remove.Click += (_, _) => BeginInvoke(() => RemoveExcluded(item));
            _excludeRows.Add(new SettingsCard
            {
                Glyph = Glyphs.Apps, Title = name.Replace("&", "&&"),
                Description = name.EndsWith('*') ? Strings.Format("exclude.prefix", name.TrimEnd('*')) : "",
                Action = remove,
            });
        }
        foreach (var c in _excludeRows) page.Controls.Add(c);
        page.ResumeLayout(true);
        if (IsHandleCreated) Theme.Apply(this);   // 새로 만든 카드에 색을 입힌다(처음에는 생성자 끝에서 칠한다)
    }

    void AddExcluded()
    {
        string name = _excludeInput.Text.Trim();
        if (name.Length == 0) return;
        var items = _draft.ExcludedProcesses;
        if (!items.Any(x => string.Equals(ProcessFilter.Normalize(x), ProcessFilter.Normalize(name), StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(name);
            Touch();
            ReloadExcluded();
        }
        _excludeInput.Text = "";
        _excludeInput.Focus();
    }

    void RemoveExcluded(string name)
    {
        if (IsDisposed) return;
        var items = _draft.ExcludedProcesses;
        int i = items.IndexOf(name);
        if (i < 0) return;
        items.RemoveAt(i);
        Touch();
        ReloadExcluded();
        // 키보드로 지우던 사람이 이어서 지울 수 있게 같은 자리의 다음 항목으로, 없으면 입력칸으로.
        var next = _excludeRows.OfType<SettingsCard>().ElementAtOrDefault(Math.Min(i, items.Count - 1))?.Action;
        (next ?? _excludeInput).Focus();
    }

    /// <summary>실행 중인 앱 목록을 펼친다. 고르면 바로 제외 목록에 넣는다.</summary>
    void ShowRunningApps()
    {
        var names = RunningAppNames()
            .Where(n => !_draft.ExcludedProcesses.Any(x => string.Equals(ProcessFilter.Normalize(x), ProcessFilter.Normalize(n), StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (names.Length == 0) return;
        _appsMenu?.Dispose();
        var menu = _appsMenu = new ContextMenuStrip
        {
            Renderer = Theme.CreateMenuRenderer(), ShowImageMargin = false, Font = Font, MinimumSize = new Size(_excludeFrame.Width, 0),
        };
        foreach (var n in names)
        {
            string name = n;
            var item = new ToolStripMenuItem(name.Replace("&", "&&"));
            item.Click += (_, _) => { _excludeInput.Text = name; AddExcluded(); };
            menu.Items.Add(item);
        }
        menu.Opened += (_, _) => Theme.RoundCorners(menu.Handle);
        menu.Show(_excludeFrame, new Point(0, _excludeFrame.Height + LogicalToDeviceUnits(2)));
    }

    /// <summary>창이 있는 실행 중인 앱의 프로세스 이름(이 프로그램 제외), 이름순.</summary>
    static string[] RunningAppNames()
    {
        System.Diagnostics.Process[]? procs = null;
        try
        {
            procs = System.Diagnostics.Process.GetProcesses();
            return procs
                .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })   // 창이 있는 앱만
                .Select(p => p.ProcessName)
                .Where(n => !string.Equals(n, AppInfo.ProductName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) { Log.Error("list running apps failed", ex); return Array.Empty<string>(); }
        finally { if (procs is not null) foreach (var p in procs) p.Dispose(); }
    }

    void ExportSettings()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = Strings.Get("settings.fileFilter"), DefaultExt = "json", AddExtension = true,
            FileName = "ImeBadge-settings.json", OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SettingsStore.Export(_draft, dlg.FileName);
            Log.Write($"settings exported: {dlg.FileName}");
            Dialogs.Info(Strings.Get("settings.exported"), dlg.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("settings export failed", ex);
            Dialogs.Warning(Strings.Get("settings.exportFailed"), ex.Message);
        }
    }

    void ImportSettings()
    {
        using var dlg = new OpenFileDialog { Filter = Strings.Get("settings.fileFilter"), CheckFileExists = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Settings imported;
        try { imported = SettingsStore.Import(dlg.FileName); }
        catch (InvalidDataException ex)
        {
            Log.Error("settings import: not a settings file", ex);
            Dialogs.Warning(Strings.Get("settings.importFailed"), Strings.Get("settings.importInvalid"));
            return;
        }
        catch (Exception ex)
        {
            Log.Error("settings import failed", ex);
            Dialogs.Warning(Strings.Get("settings.importFailed"), ex.Message);
            return;
        }
        // 이 PC 의 업데이트 기록은 그대로 둔다(내보낼 때도 빼지만, 손으로 만든 파일일 수도 있다).
        imported.LastUpdateCheckUtc = _draft.LastUpdateCheckUtc;
        imported.SkippedUpdateTag = _draft.SkippedUpdateTag;
        _draft.CopyFrom(imported);
        LoadDraftIntoControls();
        Log.Write($"settings imported: {dlg.FileName}");
        Dialogs.Info(Strings.Get("settings.imported"), Strings.Get("settings.imported.text"));
    }

    // ── 타일 그리기 ──
    /// <summary>밑줄 모양은 위치와 상관없이 caret 바로 아래에 붙으므로(BadgeLayout) 위치 타일을 끈다. 고른 위치는 그대로 남는다.</summary>
    void SyncPlacementEnabled()
    {
        if (_placement is not null) _placement.Enabled = _draft.Style != BadgeStyle.Underline;
    }

    static string ShortStyle(BadgeStyle s) => Strings.Get(s switch
    {
        BadgeStyle.Box => "style.short.box", BadgeStyle.Pill => "style.short.pill", BadgeStyle.Dot => "style.short.dot",
        BadgeStyle.Underline => "style.short.underline", _ => "style.short.dotFlash",
    });

    static string ShortPlacement(BadgePlacement p) => Strings.Get(p switch
    {
        BadgePlacement.AboveRight => "place.short.aboveRight", BadgePlacement.BelowRight => "place.short.belowRight",
        BadgePlacement.AboveLeft => "place.short.aboveLeft", BadgePlacement.BelowLeft => "place.short.belowLeft",
        BadgePlacement.Above => "place.short.above", _ => "place.short.below",
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

    /// <summary>
    /// 위치 타일: 가운데 caret 을 두고 그 위치에 점 배지를 놓는다. 어디에 뜨는지 한눈에 보인다.
    /// 타일 그림 칸은 낮아서 타일 안에서 자리를 정하면 "위에 자리가 없으면 아래로" 가 끼어들어 위·아래 타일이 똑같아진다.
    /// 그래서 넓은 영역에서 자리를 정한 뒤 caret 과 배지를 묶어 타일 세로 가운데로 옮긴다.
    /// </summary>
    void DrawPlacementTile(Graphics g, RectangleF r, Theme.Palette p, BadgePlacement placement)
    {
        float dpi = DeviceDpi / 96f;
        using var bmp = BadgeRenderer.Render(ImeState.Hangul, BadgeStyle.Dot, dpi, BadgeTheme.From(_draft) with { Character = BadgeCharacters.None }, 100);
        var caret = new Rectangle((int)(r.Left + r.Width / 2), 0, 1, (int)(12 * dpi));
        var room = Rectangle.Inflate(caret, 1000, 1000);
        var pos = BadgeLayout.Compute(new LayoutInput(caret, bmp.Size, BadgeStyle.Dot, placement, dpi, room));
        int top = Math.Min(caret.Top, pos.Y), bottom = Math.Max(caret.Bottom, pos.Y + bmp.Height);
        int dy = (int)Math.Round(r.Top + (r.Height - (bottom - top)) / 2f) - top;
        caret.Offset(0, dy);
        pos.Offset(0, dy);
        using (var pen = new Pen(p.Text, Math.Max(1f, dpi))) g.DrawLine(pen, caret.Left, caret.Top, caret.Left, caret.Bottom);
        g.DrawImage(bmp, new Rectangle(pos, bmp.Size), new Rectangle(Point.Empty, bmp.Size), GraphicsUnit.Pixel);
    }

    // ── 도우미 ──
    /// <summary>카드 하나: 아이콘, 제목(key, 니모닉 포함), 설명(key + ".desc"), 오른쪽 컨트롤 또는 아래 넓은 내용.</summary>
    SettingsCard Card(string glyph, string key, Control? action = null, Control? body = null)
    {
        var card = new SettingsCard { Glyph = glyph, Title = Strings.Get(key), Description = Strings.Get(key + ".desc") };
        if (action is not null) card.Action = action;
        if (body is not null) card.Body = body;
        return card;
    }

    /// <summary>누를 수 있는 카드(폴더·웹 페이지 열기). 오른쪽 끝에 "새 창에서 열기" 표시.</summary>
    SettingsCard Link(string glyph, string key, Action onClick, string trailing = Glyphs.OpenExternal)
    {
        var card = Card(glyph, key);
        card.Clickable = true;
        card.TrailingGlyph = trailing;
        card.Click += (_, _) => onClick();
        return card;
    }

    SectionHeader Section(string key) => new(Strings.Get(key), _sectionFont);

    NoteLabel Note(string text) => new(text, _hintFont);

    /// <summary>"켬/끔" 이 붙은 토글(카드 오른쪽). 이름은 카드 제목이 준다.</summary>
    ToggleSwitch Toggle(Action<bool> onChange)
    {
        var t = new ToggleSwitch { AutoSize = true, OnText = Strings.Get("toggle.on"), OffText = Strings.Get("toggle.off"), Margin = Padding.Empty };
        t.CheckedChanged += (_, _) => onChange(t.Checked);
        return t;
    }

    static AccentButton Button(string key, Action onClick)
    {
        var b = new AccentButton { Text = Strings.Get(key), AutoSize = true };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>고르기 상자(카드 오른쪽). 휠은 받지 않아 페이지 스크롤 중에 값이 바뀌지 않는다(<see cref="ThemedComboBox"/>).</summary>
    static ThemedComboBox Combo(int width, IEnumerable<string> items, Action<int> onChange)
    {
        var c = new ThemedComboBox { Width = width };
        c.SetItems(items);
        c.SelectedIndexChanged += (_, _) => onChange(Math.Max(0, c.SelectedIndex));
        return c;
    }

    /// <summary>가로로 나란히(카드 오른쪽의 여러 컨트롤).</summary>
    static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        panel.Controls.AddRange(controls);
        return panel;
    }

    /// <summary>세로로 쌓기(배지 모양의 기본·캐릭터 두 줄).</summary>
    static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = Row(controls);
        panel.FlowDirection = FlowDirection.TopDown;
        return panel;
    }

    /// <summary>슬라이더 + 현재 값(왼쪽). 값은 <paramref name="step"/> 단위로 맞춰진다(예: 137% → 135%).</summary>
    static Control Slider(out AccentSlider bar, int min, int max, int step, int page, string unit, Action<int> onChange)
    {
        var s = new AccentSlider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = page, Width = 220, Margin = Padding.Empty };
        var value = new Label { AutoSize = true, Margin = new Padding(0, 6, 8, 0), MinimumSize = new Size(56, 0), TextAlign = ContentAlignment.TopRight, Text = $"{s.Value}{unit}" };
        s.ValueChanged += (_, _) => { value.Text = $"{s.Value}{unit}"; onChange(s.Value); };
        bar = s;
        return Row(value, s);
    }

    /// <summary>색 견본 팔레트 + 현재 색의 16진수 표시.</summary>
    Control Swatches(out ColorSwatches swatches, out Label hex, Action<string> onChange)
    {
        var s = new ColorSwatches { Margin = new Padding(0, 0, 8, 0) };
        var h = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 7, 0, 0), Font = _hintFont };
        s.ColorChanged += (_, _) => { h.Text = s.Hex; onChange(s.Hex); Touch(); };
        swatches = s; hex = h;
        return Row(s, h);
    }

    void UpdateHint() => _commands.Hint = Strings.Get(_dirty ? "settings.hint.dirty" : "settings.hint.clean");

    /// <summary>
    /// 편집 값을 실제 설정에 넣는다. 업데이트 확인 기록(마지막 확인 시각·건너뛴 버전)은 이 창이 편집하는 값이 아니므로 실제 설정의 것을 지킨다:
    /// 창이 열린 동안 [새 버전 확인]이나 하루 한 번 자동 확인이 그 값을 바꿨을 수 있다.
    /// </summary>
    void Store(Settings from)
    {
        var lastCheck = _live.LastUpdateCheckUtc;
        var skipped = _live.SkippedUpdateTag;
        _live.CopyFrom(from);
        _live.LastUpdateCheckUtc = lastCheck;
        _live.SkippedUpdateTag = skipped;
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
        SyncPlacementEnabled();
        _size.Value = _draft.SizePercent;
        _opacity.Value = _draft.OpacityPercent;
        _poll.Value = Math.Clamp(_draft.PollIntervalMs, _poll.Minimum, _poll.Maximum);
        _hangulColor.Hex = _draft.HangulColor; _hangulHex.Text = _hangulColor.Hex;
        _englishColor.Hex = _draft.EnglishColor; _englishHex.Text = _englishColor.Hex;
        _animate.Checked = _draft.Animate;
        _visibility.SelectedIndex = Math.Max(0, Array.FindIndex(Labels.Visibilities, v => v.value == _draft.Visibility));
        _capsLock.Checked = _draft.ShowCapsLock;
        _shiftHold.Checked = _draft.ShowShiftHold;
        _shiftHold.Enabled = _draft.ShowCapsLock;
        _autostartBox.Checked = _autostart;
        _fullscreen.Checked = _draft.HideOnFullscreen;
        _trayStateBox.Checked = _draft.TrayShowsState;
        _hotkey.Checked = _draft.HotkeyEnabled;
        _hotkeyBox.Enabled = _draft.HotkeyEnabled;
        _hotkeyBox.Text = _draft.Hotkey;
        _updates.Checked = _draft.CheckForUpdates;
        _language.SelectedIndex = Math.Max(0, Array.FindIndex(Labels.Languages, l => l.value == _draft.Language));
        ReloadExcluded();
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
        Store(_draft);
        _dirty = true;
        UpdateHint();
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
        Store(_draft);
        if (_autostart != Autostart.IsEnabled()) Autostart.Set(_autostart);
        _dirty = false;
        Applied?.Invoke();
    }

    /// <summary>[취소]·닫기: 창을 열 때의 설정으로 되돌리고 저장을 알린다(그 사이 트레이 메뉴가 저장했을 수 있다).</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || _detached || DialogResult == DialogResult.OK || !_dirty) return;
        Store(_original);
        _dirty = false;
        Applied?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _flashTimer.Dispose(); _copiedTimer.Dispose(); _tips.Dispose(); _sectionFont.Dispose(); _hintFont.Dispose(); _appsMenu?.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 미리보기 패널. 슬라이더를 끌 때마다 통째로 다시 그리므로 이중 버퍼가 없으면 배경이 먼저 지워지며 깜빡인다.
/// </summary>
sealed class PreviewPanel : Panel
{
    // 창 크기를 바꾸면 밝은·어두운 반쪽의 경계가 옮겨 가므로 새로 드러난 곳만이 아니라 전체를 다시 그린다.
    public PreviewPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
}

/// <summary>
/// 직접 입력하는 칸(제외 앱 추가). Enter 를 창의 [확인] 대신 이 칸이 받아 "추가" 로 쓴다.
/// </summary>
sealed class InputTextBox : TextBox
{
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Enter || base.IsInputKey(keyData);
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
