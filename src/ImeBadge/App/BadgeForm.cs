using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ImeBadge;

/// <summary>
/// caret 옆에 뜨는 배지 창(레이어드 창) + 트레이 아이콘·메뉴. 앱의 중심 객체다.
/// 100ms 타이머와 OS 이벤트 훅(IME 변경·활성 창 변경·포커스 변경)이 <see cref="Poll"/> 을 부르고,
/// Poll 은 상태를 읽어 배지를 그리거나 옮기거나 숨긴다.
/// </summary>
sealed class BadgeForm : Form
{
    const int HotkeyId = 1;
    const int IdleAfterMs = 2000;      // 배지가 이만큼 숨겨져 있으면 폴링을 느리게
    internal const int FlashMs = 1500; // DotFlash: 변경 직후 글자를 보여 주는 시간 (설정 창 미리보기도 같은 시간을 쓴다)
    const int FadeMs = 150;            // 나타날 때 페이드인
    const int PulseMs = 260;           // 한/영이 바뀔 때 살짝 커졌다 돌아오는 시간
    const float PulseGrow = 0.18f;     // 펄스 최대 확대 비율

    /// <summary>진행 중인 배지 애니메이션. 별도의 16ms 타이머가 마지막 상태로 다시 그린다(IME 는 다시 읽지 않는다).</summary>
    enum Anim { None, FadeIn, Pulse }
    Anim _anim;
    long _animStart;
    readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 16 };
    Snapshot _lastSnapshot;
    Bitmap? _lastBmp;          // 마지막으로 올린 비트맵. 알파만 바꿔 다시 올릴 때 쓴다
    byte _lastAlpha = 255;
    bool _systemAnimations = Native.AnimationsEnabled();   // Windows 접근성 "애니메이션 효과"

    readonly Settings _settings;
    readonly SettingsStore _store;
    readonly AppPaths _paths;
    readonly bool _firstRun;
    readonly System.Windows.Forms.Timer _timer = new();
    readonly NotifyIcon _tray;
    readonly TrayIcons _trayIcons = new();
    ImeState _trayState = ImeState.Unknown;   // 트레이 아이콘·툴팁이 마지막으로 반영한 상태
    bool _trayPaused, _trayCaps;
    readonly Native.WinEventProc _eventProc;   // GC 회수 방지용 필드
    readonly IntPtr[] _hooks = new IntPtr[5];
    long _lastEventPoll;                        // 이벤트로 촉발한 마지막 Poll 시각. caret 이동 이벤트가 몰려올 때 억제용
    const int EventPollMinGapMs = 15;           // 이보다 촘촘한 이벤트는 건너뛴다(타이머가 곧 따라잡는다)

    ImeState _lastState = ImeState.Unknown;
    bool _lastCaps;
    DateTime _flashUntil = DateTime.MinValue;
    (ImeState state, bool caps, BadgeStyle style, float scale, int opacity, string hangul, string english) _renderKey;
    Size _bitmapSize;
    Point _lastPos = new(int.MinValue, int.MinValue);
    IntPtr _lastFg;
    bool _allowShow;

    bool _paused, _sessionLocked, _polling, _hotkeyRegistered, _startupScheduled;
    long _hiddenSince = Environment.TickCount64;
    int _pollErrors;
    IntPtr _raiseFailFg; int _raiseFailCount;
    string? _pendingUpdateUrl;
    CancellationTokenSource? _updateCts;
    SettingsForm? _settingsForm;
    AboutForm? _aboutForm;

    ToolStripMenuItem _pauseItem = null!, _autostartItem = null!;
    ToolStripLabel _statusItem = null!;   // 누를 수 없는 상태 줄. 비활성 메뉴 항목과 달리 아이콘이 회색으로 바래지 않는다

    public BadgeForm(Settings settings, SettingsStore store, AppPaths paths, bool firstRun)
    {
        _settings = settings;
        _store = store;
        _paths = paths;
        _firstRun = firstRun;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;   // DPI 변경 시 WinForms가 창 크기를 건드리지 않게
        Size = new Size(1, 1);
        Text = AppInfo.ProductName;
        ResetRenderKey();

        _tray = new NotifyIcon
        {
            Icon = Icons.App,
            Text = TrayText(ImeState.Unknown),
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
        // 왼쪽 클릭은 트레이 앱의 대표 동작(Windows 관행). 더블클릭도 같은 곳으로 간다.
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };
        _tray.DoubleClick += (_, _) => OpenSettings();
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_pendingUpdateUrl is not null) AboutForm.Open(SafeUrl.GitHubOr(_pendingUpdateUrl, AppInfo.ReleasesUrl));
            else OpenSettings();
        };

        _timer.Interval = _settings.PollIntervalMs;
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        _animTimer.Tick += (_, _) => AnimTick();

        // OS 이벤트가 오면 타이머를 기다리지 않고 바로 다시 읽는다.
        _eventProc = OnWinEvent;
        _hooks[0] = Hook(Native.EVENT_OBJECT_IME_CHANGE, "IME change");
        _hooks[1] = Hook(Native.EVENT_SYSTEM_FOREGROUND, "foreground");
        _hooks[2] = Hook(Native.EVENT_OBJECT_FOCUS, "focus");
        // caret 이 움직이면(타이핑·화살표 키·창 이동) 다음 틱을 기다리지 않고 배지를 따라 옮긴다.
        // LOCATIONCHANGE 는 마우스 포인터·모든 창의 이동에도 오므로 OnWinEvent 에서 caret 것만 골라낸다.
        _hooks[3] = Hook(Native.EVENT_OBJECT_LOCATIONCHANGE, "location change");
        _hooks[4] = Hook(Native.EVENT_OBJECT_TEXTSELECTIONCHANGED, "text selection");

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    IntPtr Hook(uint evt, string name)
    {
        var h = Native.SetWinEventHook(evt, evt, IntPtr.Zero, _eventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        Log.Write(h == IntPtr.Zero ? $"{name} hook FAILED" : $"{name} hook registered");
        return h;
    }

    /// <summary>
    /// WinEvent 콜백(OUTOFCONTEXT 라 우리 UI 스레드에서 불린다). 위치 변경 이벤트는 caret 것만 받고, 그마저도 배지가 보이는 동안
    /// 활성 창에서 온 것만 받는다. 타이핑 중에는 글자마다 이벤트가 오므로 15ms 안에 몰린 것은 건너뛴다(타이머가 곧 따라잡는다).
    /// </summary>
    void OnWinEvent(IntPtr hHook, uint evt, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (evt is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_TEXTSELECTIONCHANGED)
        {
            if (evt == Native.EVENT_OBJECT_LOCATIONCHANGE && idObject != Native.OBJID_CARET) return;
            if (!Visible || _paused) return;   // 숨겨진 동안은 포커스·활성 창 이벤트와 타이머만으로 충분하다
            long now = Environment.TickCount64;
            if (now - _lastEventPoll < EventPollMinGapMs) return;
            _lastEventPoll = now;
        }
        Poll();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyHotkey();
        // 첫 실행 안내와 업데이트 확인은 창이 준비된 뒤 잠깐 있다가. (핸들이 다시 만들어져도 한 번만)
        if (_startupScheduled) return;
        _startupScheduled = true;
        var once = new System.Windows.Forms.Timer { Interval = 3000 };
        once.Tick += (_, _) =>
        {
            once.Dispose();
            if (_firstRun)
                _tray.ShowBalloonTip(8000, AppInfo.DisplayName, Strings.Get("app.firstRun"), ToolTipIcon.Info);
            if (UpdateChecker.IsDue(_settings)) CheckForUpdates(manual: false);
        };
        once.Start();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered) { Native.UnregisterHotKey(Handle, HotkeyId); _hotkeyRegistered = false; }
        base.OnHandleDestroyed(e);
    }

    /// <summary>설정의 단축키를 전역으로 등록한다. <paramref name="warnOnFailure"/> 면 실패를 사용자에게도 알린다(설정 저장 시).</summary>
    void ApplyHotkey(bool warnOnFailure = false)
    {
        if (!IsHandleCreated) return;
        if (_hotkeyRegistered) { Native.UnregisterHotKey(Handle, HotkeyId); _hotkeyRegistered = false; }
        _pauseItem.ShortcutKeyDisplayString = _settings.HotkeyEnabled ? _settings.Hotkey : null;
        if (!_settings.HotkeyEnabled) return;
        if (!HotkeySpec.TryParse(_settings.Hotkey, out var hk)) hk = HotkeySpec.Default;
        _hotkeyRegistered = Native.RegisterHotKey(Handle, HotkeyId, (uint)hk.Modifiers | Native.MOD_NOREPEAT, (uint)hk.Key);
        if (_hotkeyRegistered) return;
        Log.Error($"hotkey {hk} registration failed (already used by another app?)");
        if (warnOnFailure)
            Dialogs.Warning(Strings.Format("hotkey.failed", hk), Strings.Get("hotkey.failed.text"));
    }

    // ── 트레이 메뉴 ──
    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // 맨 위 한 줄은 현재 상태. 누를 수 없는 안내 줄(ToolStripLabel)이라 마우스를 올려도 반응하지 않는다.
        _statusItem = new ToolStripLabel { Margin = new Padding(0, 2, 0, 2), ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        _glyphs.Clear();   // 언어가 바뀌어 메뉴를 다시 만들 때 옛 항목을 버린다
        _pauseItem = new ToolStripMenuItem(Strings.Get("menu.pause"), null, (_, _) => TogglePause()) { ShortcutKeyDisplayString = _settings.HotkeyEnabled ? _settings.Hotkey : null };
        _glyphs[_pauseItem] = MenuIcons.Pause;
        menu.Items.Add(_pauseItem);
        // 더블클릭과 같은 동작인 "설정"을 굵게: Windows 관행에서 굵은 항목이 기본 동작이다.
        var settingsItem = new ToolStripMenuItem(Strings.Get("menu.settings"), null, (_, _) => OpenSettings());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
        _glyphs[settingsItem] = MenuIcons.Settings;
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());

        var shape = new ToolStripMenuItem(Strings.Get("menu.style"));
        foreach (var (label, value) in Labels.Styles)
            AddRadio(shape, label, () => _settings.Style == value, () => _settings.Style = value);
        _glyphs[shape] = MenuIcons.Shape;
        menu.Items.Add(shape);

        var place = new ToolStripMenuItem(Strings.Get("menu.placement"));
        foreach (var (label, value) in Labels.Placements)
            AddRadio(place, label, () => _settings.Placement == value, () => _settings.Placement = value);
        _glyphs[place] = MenuIcons.Place;
        menu.Items.Add(place);

        var size = PresetMenu(Strings.Get("menu.size"), Labels.SizePresets, () => _settings.SizePercent, v => _settings.SizePercent = v);
        _glyphs[size] = MenuIcons.Size;
        menu.Items.Add(size);
        var opacity = PresetMenu(Strings.Get("menu.opacity"), Labels.OpacityPresets, () => _settings.OpacityPercent, v => _settings.OpacityPercent = v);
        _glyphs[opacity] = MenuIcons.Opacity;
        menu.Items.Add(opacity);

        menu.Items.Add(new ToolStripSeparator());
        _autostartItem = new ToolStripMenuItem(Strings.Get("menu.autostart"), null, (_, _) =>
        {
            bool on = !Autostart.IsEnabled();
            if (!Autostart.Set(on))
                Dialogs.Warning(Strings.Get("autostart.failed"), Strings.Get("autostart.failed.text"));
        });
        menu.Items.Add(_autostartItem);
        var update = new ToolStripMenuItem(Strings.Get("menu.checkUpdates"), null, (_, _) => CheckForUpdates(manual: true));
        _glyphs[update] = MenuIcons.Update;
        menu.Items.Add(update);
        var about = new ToolStripMenuItem(Strings.Get("menu.about"), null, (_, _) => OpenAbout());
        _glyphs[about] = MenuIcons.Info;
        menu.Items.Add(about);
        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem(Strings.Get("menu.exit"), null, (_, _) => Application.Exit());
        _glyphs[exit] = MenuIcons.Exit;
        menu.Items.Add(exit);

        // 메뉴를 열 때마다 체크 표시·상태 줄을 현재 상태에 맞춘다.
        menu.Opening += (_, _) =>
        {
            RefreshChecks(menu.Items);
            _statusItem.Text = Strings.Format("menu.current", StateText(_trayState, _trayCaps));
            RefreshStatusImage();
            _pauseItem.Checked = _paused;
            _autostartItem.Checked = Autostart.IsEnabled();
        };
        ApplyMenuTheme(menu);
        return menu;
    }

    // 메뉴 항목 ↔ 아이콘 글리프. 테마가 바뀌면 글자색으로 다시 그린다.
    readonly Dictionary<ToolStripItem, string> _glyphs = new();
    readonly Dictionary<ToolStripItem, Bitmap> _glyphImages = new();
    Bitmap? _statusImage;

    /// <summary>상태 줄 앞에 지금 트레이에 보이는 것과 같은 작은 아이콘을 둔다.</summary>
    void RefreshStatusImage()
    {
        var icon = _trayIcons.Get(_trayState, _paused, 16, BadgeTheme.From(_settings), _trayCaps);
        var old = _statusImage;
        _statusImage = icon.ToBitmap();
        _statusItem.Image = _statusImage;
        old?.Dispose();
    }

    /// <summary>메뉴와 모든 하위 메뉴에 테마 렌더러와 글리프 아이콘을 적용하고, 열릴 때 Windows 11 식 둥근 모서리를 요청한다.</summary>
    void ApplyMenuTheme(ToolStripDropDown menu)
    {
        var renderer = Theme.CreateMenuRenderer();
        var palette = Theme.Current;
        void Walk(ToolStripDropDown dd)
        {
            dd.Renderer = renderer;
            dd.Opened -= RoundOnOpened; dd.Opened += RoundOnOpened;
            foreach (ToolStripItem it in dd.Items)
                if (it is ToolStripMenuItem mi && mi.HasDropDownItems) Walk(mi.DropDown);
        }
        Walk(menu);

        // 옛 메뉴(언어 전환 전)의 그림은 모두 버리고 현재 항목에만 새로 그린다.
        foreach (var (item, old) in _glyphImages) { item.Image = null; old.Dispose(); }
        _glyphImages.Clear();
        foreach (var (item, glyph) in _glyphs)
        {
            var bmp = MenuIcons.Glyph(glyph, item.Enabled ? palette.Text : palette.SubtleText, menu.ImageScalingSize);
            if (bmp is null) continue;
            _glyphImages[item] = bmp;
            item.Image = bmp;
        }
    }

    static void RoundOnOpened(object? sender, EventArgs e)
    {
        if (sender is ToolStripDropDown dd && dd.IsHandleCreated) Theme.RoundCorners(dd.Handle);
    }

    void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => OnUserPreferenceChanged(sender, e)); return; }
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility)
        {
            if (_tray.ContextMenuStrip is { } menu) ApplyMenuTheme(menu);   // 밝게/어둡게 전환을 따라간다
            _systemAnimations = Native.AnimationsEnabled();
        }
    }

    // ── 애니메이션 ──
    bool AnimationsOn => _settings.Animate && _systemAnimations;

    void StartAnim(Anim kind)
    {
        _anim = kind;
        _animStart = Environment.TickCount64;
        _animTimer.Start();
    }

    void StopAnim()
    {
        _anim = Anim.None;
        _animTimer.Stop();
    }

    /// <summary>진행률 0~1. 애니메이션이 없으면 1.</summary>
    float AnimProgress()
    {
        if (_anim == Anim.None) return 1f;
        int total = _anim == Anim.FadeIn ? FadeMs : PulseMs;
        return Math.Clamp((Environment.TickCount64 - _animStart) / (float)total, 0f, 1f);
    }

    static float EaseOut(float p) => 1f - (1f - p) * (1f - p);

    void AnimTick()
    {
        if (_polling || IsDisposed) return;
        if (_anim == Anim.None || _paused || _sessionLocked || !Visible) { StopAnim(); return; }
        _polling = true;
        try { Apply(_lastSnapshot); }
        catch (Exception ex) { StopAnim(); Log.Error("animation frame failed", ex); }
        finally { _polling = false; }
    }

    // ── 트레이 아이콘·툴팁 ──
    string StateText(ImeState state, bool caps = false) => Strings.Get(_paused ? "state.paused" : state switch
    {
        ImeState.Hangul => "state.hangul",
        ImeState.English => caps ? "state.englishCaps" : "state.english",
        ImeState.OtherLang => "state.other",
        _ => "state.none",
    });

    /// <summary>툴팁: 이름 · 상태 · 단축키. NotifyIcon.Text 는 127자 제한이 있다.</summary>
    string TrayText(ImeState state, bool caps = false)
    {
        string s = AppInfo.DisplayName + " · " + StateText(state, caps);
        if (_paused) s += _settings.HotkeyEnabled ? Strings.Format("tray.resumeWith", _settings.Hotkey) : "";
        else if (_settings.HotkeyEnabled) s += Strings.Format("tray.pauseWith", _settings.Hotkey);
        if (Log.Enabled) s += " [debug]";
        return s.Length > 127 ? s[..127] : s;
    }

    /// <summary>트레이 아이콘과 툴팁을 상태에 맞춘다. 같은 상태면 아무것도 하지 않는다(Shell_NotifyIcon 호출을 아낀다).</summary>
    void UpdateTray(ImeState state, bool caps = false, bool force = false)
    {
        if (!_settings.TrayShowsState) { state = ImeState.Unknown; caps = false; }
        if (!force && state == _trayState && _paused == _trayPaused && caps == _trayCaps) return;
        _trayState = state; _trayPaused = _paused; _trayCaps = caps;
        _tray.Icon = _trayIcons.Get(state, _paused, SystemInformation.SmallIconSize.Width, BadgeTheme.From(_settings), caps);
        _tray.Text = TrayText(state, caps);
    }

    /// <summary>색·DPI 가 바뀌어 캐시한 아이콘을 버리고 다시 그린다. 트레이가 버릴 아이콘을 가리키지 않도록 먼저 기본 아이콘으로 돌린다.</summary>
    void RefreshTray()
    {
        _tray.Icon = Icons.App;
        _trayIcons.Clear();
        UpdateTray(_trayState, _trayCaps, force: true);
    }

    static void RefreshChecks(ToolStripItemCollection items)
    {
        foreach (ToolStripItem it in items)
        {
            if (it is not ToolStripMenuItem mi) continue;
            if (mi.Tag is Func<bool> isOn) mi.Checked = isOn();
            RefreshChecks(mi.DropDownItems);
        }
    }

    void AddRadio(ToolStripMenuItem parent, string text, Func<bool> isOn, Action apply)
    {
        var item = new ToolStripMenuItem(text) { Tag = isOn };
        item.Click += (_, _) => { apply(); OnSettingsChanged(); };
        parent.DropDownItems.Add(item);
    }

    /// <summary>
    /// 퍼센트 프리셋 하위 메뉴. 설정 창에서 프리셋에 없는 값(예: 90%)을 골랐으면 체크된 항목이 하나도 없어 혼란스러우므로,
    /// 그럴 때만 맨 아래에 "사용자 지정 (90%)" 항목을 체크된 채로 보여 준다.
    /// </summary>
    ToolStripMenuItem PresetMenu(string title, (string label, int pct)[] presets, Func<int> get, Action<int> set)
    {
        var menu = new ToolStripMenuItem(title);
        foreach (var (label, pct) in presets)
            AddRadio(menu, label, () => get() == pct, () => set(pct));
        var custom = new ToolStripMenuItem { Enabled = false, Visible = false, Checked = true };
        menu.DropDownItems.Add(custom);
        menu.DropDownOpening += (_, _) =>
        {
            int v = get();
            custom.Visible = Array.FindIndex(presets, p => p.pct == v) < 0;
            custom.Text = Strings.Format("menu.custom", v);
        };
        return menu;
    }

    // 마지막으로 반영한 단축키·트레이·언어 설정. 설정 창에서 슬라이더를 끌 때마다 불리므로, 실제로 바뀐 것만 다시 적용한다.
    (bool enabled, string hotkey) _appliedHotkey;
    (string hangul, string english, bool showState) _appliedTray;
    bool _appliedKorean = Strings.IsKorean;

    /// <summary>설정이 바뀐 뒤 공통 처리: 저장(편집 중 미리 반영일 때는 생략), 다시 그리기, 단축키·주기·트레이·언어 반영.</summary>
    void OnSettingsChanged(bool save = true)
    {
        _settings.Normalize();
        if (save) _store.Save(_settings);
        ResetRenderKey();                                   // 다음 틱에 강제로 다시 그림
        _flashUntil = DateTime.Now.AddMilliseconds(FlashMs); // DotFlash면 바로 글자를 한 번 보여 준다
        _timer.Interval = _settings.PollIntervalMs;

        Strings.Setting = _settings.Language;
        if (Strings.IsKorean != _appliedKorean) { _appliedKorean = Strings.IsKorean; ApplyLanguage(); }

        var hk = (_settings.HotkeyEnabled, _settings.Hotkey);
        if (hk != _appliedHotkey || save) { _appliedHotkey = hk; ApplyHotkey(warnOnFailure: save); }

        var tray = (_settings.HangulColor, _settings.EnglishColor, _settings.TrayShowsState);
        if (tray != _appliedTray) { _appliedTray = tray; RefreshTray(); }

        Poll();
    }

    /// <summary>UI 언어가 바뀌면 트레이 메뉴를 새 문구로 다시 만들고 툴팁을 갱신한다. 열려 있는 정보 창은 닫는다(다시 열면 새 언어).</summary>
    void ApplyLanguage()
    {
        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = BuildMenu();
        old?.Dispose();
        ApplyHotkey();                           // 새 메뉴의 "일시 중지" 항목에 단축키 표시
        UpdateTray(_trayState, _trayCaps, force: true);
        if (_aboutForm is { IsDisposed: false }) _aboutForm.Close();
    }

    void ResetRenderKey() => _renderKey = (ImeState.Unknown, false, (BadgeStyle)(-1), 0, -1, "", "");

    // ── 일시 중지 / 설정 / 정보 ──
    void TogglePause()
    {
        _paused = !_paused;
        Log.Write(_paused ? "paused" : "resumed");
        UpdateTray(ImeState.Unknown, force: true);
        Poll();
    }

    void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false }) { _settingsForm.Activate(); return; }
        ShowSettings(new SettingsForm(_settings));
    }

    void ShowSettings(SettingsForm form)
    {
        _settingsForm = form;
        form.Changed += () => OnSettingsChanged(save: false);   // 편집 중: 배지에 바로 반영, 저장은 아직
        form.Applied += () => OnSettingsChanged();               // 확인·취소: 저장
        form.FormClosed += (_, _) => { if (ReferenceEquals(_settingsForm, form)) _settingsForm = null; form.Dispose(); };
        // 언어를 바꾸면 창의 모든 문구를 새로 그려야 한다. 같은 편집 상태를 이어받는 새 창으로 갈아 끼운다(이벤트 처리가 끝난 뒤).
        form.LanguageChanged += () => BeginInvoke(() =>
        {
            if (form.IsDisposed || !ReferenceEquals(_settingsForm, form)) return;
            var next = form.Reopen();
            form.Detach();
            form.Close();
            ShowSettings(next);
        });
        form.Show();
        form.Activate();
    }

    void OpenAbout()
    {
        if (_aboutForm is { IsDisposed: false }) { _aboutForm.Activate(); return; }
        _aboutForm = new AboutForm(_paths, _settings, () => CheckForUpdates(manual: true));
        _aboutForm.FormClosed += (_, _) => { _aboutForm?.Dispose(); _aboutForm = null; };
        _aboutForm.Show();
        _aboutForm.Activate();
    }

    // ── 업데이트 확인 ──
    async void CheckForUpdates(bool manual)
    {
        _updateCts?.Cancel();
        var cts = _updateCts = new CancellationTokenSource();
        try
        {
            var info = await UpdateChecker.FetchLatestAsync(cts.Token);
            if (cts.IsCancellationRequested || IsDisposed) return;
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _store.Save(_settings);

            if (info is not null && VersionInfo.IsNewer(AppVersion.Display, info.Tag))
            {
                _pendingUpdateUrl = info.Url;
                Log.Write($"update available: {info.Tag}");
                if (manual) OfferUpdate(info);
                else if (info.Tag != _settings.SkippedUpdateTag)
                    _tray.ShowBalloonTip(10000, Strings.Get("update.balloon.title"),
                        Strings.Format("update.balloon.text", AppInfo.ProductName, info.Tag, AppVersion.Display), ToolTipIcon.Info);
            }
            else if (manual)
            {
                Dialogs.Info(Strings.Get(info is null ? "update.none" : "update.latest"),
                    info is null ? Strings.Format("update.current", AppVersion.Display) : Strings.Format("update.currentLatest", AppVersion.Display, info.Tag));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("update check failed", ex);
            if (manual)
                Dialogs.Warning(Strings.Get("update.failed"), Strings.Get("update.failed.text"), ex.Message);
        }
    }

    /// <summary>수동 확인에서 새 버전을 찾았을 때: 열기 / 나중에 / 이 버전 건너뛰기.</summary>
    void OfferUpdate(UpdateInfo info)
    {
        int choice = Dialogs.Choose(Strings.Format("update.offer.heading", info.Tag), Strings.Format("update.offer.text", AppVersion.Display), TaskDialogIcon.Information,
            (Strings.Get("update.open"), Strings.Get("update.open.note")),
            (Strings.Get("update.later"), Strings.Get("update.later.note")),
            (Strings.Get("update.skip"), Strings.Format("update.skip.note", info.Tag)));
        if (choice == 0) AboutForm.Open(info.Url);
        else if (choice == 2) { _settings.SkippedUpdateTag = info.Tag; _store.Save(_settings); }
    }

    // ── 시스템 이벤트 ──
    // SystemEvents 는 별도 스레드에서 올 수 있다. 창·타이머는 UI 스레드에서만 만지도록 넘긴다.
    void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => OnSessionSwitch(sender, e)); return; }
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                _sessionLocked = true; _timer.Stop(); HideBadge();
                Log.Write($"session {e.Reason}: paused polling");
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                _sessionLocked = false; _timer.Start();
                Log.Write($"session {e.Reason}: resumed polling");
                break;
        }
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => OnDisplaySettingsChanged(sender, e)); return; }
        ResetRenderKey();   // 모니터·DPI 가 바뀌면 배율이 달라질 수 있으니 다시 그린다
        _lastPos = new(int.MinValue, int.MinValue);
        RefreshTray();      // 트레이 아이콘 크기(SmallIconSize)도 DPI 를 따라간다
    }

    // ── 창 속성 ──
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW
                        | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            return cp;
        }
    }
    protected override bool ShowWithoutActivation => true;
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(_allowShow && value);
    protected override void OnPaintBackground(PaintEventArgs e) { }   // 레이어드 창은 직접 그린다

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)Native.WM_HOTKEY && (int)m.WParam == HotkeyId) { TogglePause(); return; }
        if (m.Msg == (int)SingleInstance.ShowSettingsMessage && m.Msg != 0) { OpenSettings(); return; }
        base.WndProc(ref m);
    }

    // ── 갱신 ──
    void Poll()
    {
        if (_polling || IsDisposed) return;   // 훅 콜백과 타이머가 겹쳐 들어와도 한 번에 하나만
        _polling = true;
        try
        {
            if (_paused || _sessionLocked) { HideBadge(); return; }
            Apply(ImeReader.Read(Handle, _settings));
        }
        catch (Exception ex)
        {
            // 한 틱의 실패로 앱이 죽지 않게 한다. 처음 몇 번만 자세히 남기고 그 뒤는 100번에 한 번.
            if (++_pollErrors <= 5 || _pollErrors % 100 == 0) Log.Error($"poll failed (#{_pollErrors})", ex);
        }
        finally { _polling = false; }
    }

    void HideBadge()
    {
        if (Visible) { Hide(); _hiddenSince = Environment.TickCount64; }
        StopAnim();
        UpdateTray(ImeState.Unknown);
        AdjustIdleInterval();
    }

    /// <summary>배지가 한동안 안 보이면(입력 중이 아님) 폴링을 느리게 해 CPU·배터리를 아낀다. 이벤트 훅이 즉시 깨워 준다.</summary>
    void AdjustIdleInterval()
    {
        int want = _settings.PollIntervalMs;
        if (!Visible && Environment.TickCount64 - _hiddenSince > IdleAfterMs)
            want = Math.Max(want * 3, 300);
        if (_timer.Interval != want) _timer.Interval = want;
    }

    void Apply(Snapshot s)
    {
        if ((s.Caret is null && s.Corner is null) || s.State == ImeState.Unknown)
        {
            if (s.Suppressed is not null) Log.WriteIfChanged($"suppressed: {s.Suppressed} fg='{Native.ClassName(s.Foreground)}'");
            HideBadge();
            return;
        }

        // caret 이 없으면(모서리 배지) 포커스 창의 왼쪽 아래를 기준점으로 삼아 DPI·모니터를 정한다. 위치는 아래에서 따로 계산한다.
        var corner = s.Caret is null ? s.Corner : null;
        var caret = s.Caret ?? (corner is { } c ? new Rectangle(c.Left, c.Bottom, 1, 0) : default);
        _lastSnapshot = s;
        bool appearing = !Visible;
        bool changed = s.State != _lastState || s.CapsLock != _lastCaps;   // Caps Lock 토글도 "바뀜"으로 알린다
        if (changed)
        {
            _lastState = s.State; _lastCaps = s.CapsLock;
            _flashUntil = DateTime.Now.AddMilliseconds(FlashMs);
        }
        UpdateTray(s.State, s.CapsLock);

        // 나타날 때는 페이드인, 보이는 중에 한/영이 바뀌면 펄스. 둘 다 "지금 바뀌었다"를 눈에 띄게 한다.
        if (AnimationsOn)
        {
            if (appearing) StartAnim(Anim.FadeIn);
            else if (changed) StartAnim(Anim.Pulse);
        }
        float progress = AnimProgress();
        float pulse = _anim == Anim.Pulse ? 1f + PulseGrow * (float)Math.Sin(progress * Math.PI) : 1f;
        byte alpha = _anim == Anim.FadeIn ? (byte)Math.Round(255 * EaseOut(progress)) : (byte)255;

        // DotFlash: 변경 직후 1.5초는 둥근 배지, 그 뒤는 점
        var style = _settings.Style;
        if (style == BadgeStyle.DotFlash)
            style = DateTime.Now < _flashUntil ? BadgeStyle.Pill : BadgeStyle.Dot;

        float scale = Native.DpiScaleAt(caret.Location) * _settings.SizePercent / 100f * pulse;

        var key = (s.State, s.CapsLock, style, scale, _settings.OpacityPercent, _settings.HangulColor, _settings.EnglishColor);
        bool needRender = key != _renderKey || _lastBmp is null;

        Size bs = needRender ? Size.Empty : _bitmapSize;
        Bitmap? bmp = null;
        if (needRender)
        {
            bmp = BadgeRenderer.Render(s.State, style, scale, BadgeTheme.From(_settings), _settings.OpacityPercent, s.CapsLock);
            bs = bmp.Size;
        }

        var area = Screen.FromPoint(caret.Location).WorkingArea;
        var pos = corner is { } win
            ? BadgeLayout.Corner(win, bs, scale, area)
            : BadgeLayout.Compute(new LayoutInput(caret, bs, style, _settings.Placement, scale, area));

        // FlowLauncher처럼 자기도 최상위(TopMost)인 창은 나중에 뜬 쪽이 위에 온다. 배지가 처음 보일 때,
        // 활성 창이 바뀌었을 때, 위치가 바뀌었을 때마다 최상위 창들 중에서도 맨 위로 다시 올린다.
        bool raise = !Visible || s.Foreground != _lastFg || _lastPos != pos;

        _allowShow = true;
        if (!Visible) { Show(); _timer.Interval = _settings.PollIntervalMs; }

        if (bmp is not null)
        {
            Present(bmp, pos, alpha);
            _lastBmp?.Dispose();
            _lastBmp = bmp;
            _renderKey = key;
            _bitmapSize = bs;
            if (raise) RaiseToTop();
        }
        else if (alpha != _lastAlpha)
        {
            Present(_lastBmp!, pos, alpha);   // 페이드 중: 같은 그림을 알파만 바꿔 다시 올린다(위치도 함께)
            if (raise) RaiseToTop();
        }
        else if (_lastPos != pos)
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, pos.X, pos.Y, 0, 0,
                                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);   // 이동과 동시에 맨 위로
        }
        else if (raise) RaiseToTop();

        if (_anim != Anim.None && progress >= 1f) StopAnim();   // 마지막 프레임(알파 255, 배율 1)까지 올린 뒤 멈춘다

        // 활성 창이 자기도 최상위(TopMost)라면(FlowLauncher 등) 매 틱 실제 z-order를 확인한다. 활성 창이 뒤늦게
        // 활성화되며 다시 위로 올라오거나, 백그라운드 프로세스의 z-order 변경이 활성 창 아래로 제한되는 경우가 있다.
        if (Native.IsTopmost(s.Foreground) && Native.IsAbove(s.Foreground, Handle))
            RaiseAbove(s.Foreground);

        _lastPos = pos;
        _lastFg = s.Foreground;
    }

    /// <summary>위치·크기는 그대로 두고 z-order만 최상위 창들 중 맨 위로 올린다. 포커스는 건드리지 않는다.</summary>
    void RaiseToTop() =>
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    /// <summary>
    /// 활성 창 <paramref name="fg"/>보다 위로 올린다. 보통의 SetWindowPos로 안 되면(Windows는 마지막 입력을 받지 않은
    /// 프로세스가 활성 창 위로 창을 올리는 것을 막는다) 활성 창 스레드의 입력 큐에 잠깐 붙어 그 권한을 빌린 뒤 바로 떼어 낸다.
    /// 안전장치: 응답 없는 창에는 붙지 않고(같이 멈출 수 있음), 같은 창에 세 번 실패하면 그 창에 대해서는 포기한다.
    /// </summary>
    void RaiseAbove(IntPtr fg)
    {
        RaiseToTop();
        if (!Native.IsAbove(fg, Handle)) return;
        if (_raiseFailFg == fg && _raiseFailCount >= 3) return;
        if (Native.IsHungAppWindow(fg)) { Log.WriteIfChanged($"raise: skip hung fg='{Native.ClassName(fg)}'"); return; }

        uint fgTid = Native.GetWindowThreadProcessId(fg, out _), me = Native.GetCurrentThreadId();
        if (fgTid == 0 || fgTid == me) return;
        if (!Native.AttachThreadInput(me, fgTid, true)) { NoteRaiseFailure(fg); Log.WriteIfChanged($"raise: AttachThreadInput failed fg='{Native.ClassName(fg)}'"); return; }
        try { RaiseToTop(); }
        finally { Native.AttachThreadInput(me, fgTid, false); }

        bool ok = !Native.IsAbove(fg, Handle);
        if (ok) { _raiseFailFg = IntPtr.Zero; _raiseFailCount = 0; } else NoteRaiseFailure(fg);
        Log.WriteIfChanged($"raise: via AttachThreadInput fg='{Native.ClassName(fg)}' ok={ok}");
    }

    void NoteRaiseFailure(IntPtr fg)
    {
        if (_raiseFailFg == fg) _raiseFailCount++;
        else { _raiseFailFg = fg; _raiseFailCount = 1; }
    }

    /// <summary>비트맵을 픽셀별 알파로 창에 올리면서 위치·크기도 함께 지정한다. <paramref name="alpha"/> 는 페이드인용 창 전체 알파.</summary>
    void Present(Bitmap bmp, Point pos, byte alpha)
    {
        if (Size != bmp.Size) Size = bmp.Size;   // WinForms가 아는 크기와 실제 창 크기를 일치시킨다
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = Native.SelectObject(memDc, hBmp);
            var size = new Native.SIZE(bmp.Width, bmp.Height);
            var src = new Native.POINT(0, 0);
            var dst = new Native.POINT(pos.X, pos.Y);
            // 불투명도는 렌더러가 배경 픽셀의 알파로 이미 반영했다(글자는 또렷하게 유지). 창 전체 알파는 페이드인에만 쓴다.
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = alpha,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            _lastAlpha = alpha;
        }
        finally
        {
            if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
            if (hBmp != IntPtr.Zero) Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _updateCts?.Cancel();
            for (int i = 0; i < _hooks.Length; i++)
                if (_hooks[i] != IntPtr.Zero) { Native.UnhookWinEvent(_hooks[i]); _hooks[i] = IntPtr.Zero; }
            _timer.Dispose();
            _animTimer.Dispose();
            _lastBmp?.Dispose();
            _settingsForm?.Dispose();
            _aboutForm?.Dispose();
            _tray.Visible = false;
            _tray.Icon = Icons.App;   // 캐시한 아이콘을 해제하기 전에 참조를 끊는다
            _tray.Dispose();
            _trayIcons.Dispose();
            foreach (var bmp in _glyphImages.Values) bmp.Dispose();
            _statusImage?.Dispose();
            ImmCaret.Release();   // 다른 프로세스에 빌린 버퍼를 돌려준다
        }
        base.Dispose(disposing);
    }
}
