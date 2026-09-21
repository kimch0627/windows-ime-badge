using System;
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
    const int FlashMs = 1500;          // DotFlash: 변경 직후 글자를 보여 주는 시간

    readonly Settings _settings;
    readonly SettingsStore _store;
    readonly AppPaths _paths;
    readonly bool _firstRun;
    readonly System.Windows.Forms.Timer _timer = new();
    readonly NotifyIcon _tray;
    readonly TrayIcons _trayIcons = new();
    ImeState _trayState = ImeState.Unknown;   // 트레이 아이콘·툴팁이 마지막으로 반영한 상태
    bool _trayPaused;
    readonly Native.WinEventProc _eventProc;   // GC 회수 방지용 필드
    readonly IntPtr[] _hooks = new IntPtr[3];

    ImeState _lastState = ImeState.Unknown;
    DateTime _flashUntil = DateTime.MinValue;
    (ImeState state, BadgeStyle style, float scale, int opacity, string hangul, string english) _renderKey;
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

    ToolStripMenuItem _pauseItem = null!, _autostartItem = null!, _statusItem = null!;

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
            if (_pendingUpdateUrl is not null) AboutForm.Open(_pendingUpdateUrl);
            else OpenSettings();
        };

        _timer.Interval = _settings.PollIntervalMs;
        _timer.Tick += (_, _) => Poll();
        _timer.Start();

        // OS 이벤트가 오면 타이머를 기다리지 않고 바로 다시 읽는다.
        _eventProc = (_, _, _, _, _, _, _) => Poll();
        _hooks[0] = Hook(Native.EVENT_OBJECT_IME_CHANGE, "IME change");
        _hooks[1] = Hook(Native.EVENT_SYSTEM_FOREGROUND, "foreground");
        _hooks[2] = Hook(Native.EVENT_OBJECT_FOCUS, "focus");

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
                _tray.ShowBalloonTip(8000, AppInfo.DisplayName,
                    "커서 옆에 한/영 배지가 표시됩니다. 트레이 아이콘을 우클릭하면 모양과 동작을 바꿀 수 있습니다.", ToolTipIcon.Info);
            if (UpdateChecker.IsDue(_settings)) CheckForUpdates(manual: false);
        };
        once.Start();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered) { Native.UnregisterHotKey(Handle, HotkeyId); _hotkeyRegistered = false; }
        base.OnHandleDestroyed(e);
    }

    void ApplyHotkey()
    {
        if (!IsHandleCreated) return;
        if (_hotkeyRegistered) { Native.UnregisterHotKey(Handle, HotkeyId); _hotkeyRegistered = false; }
        if (!_settings.HotkeyEnabled) return;
        _hotkeyRegistered = Native.RegisterHotKey(Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 'H');
        if (!_hotkeyRegistered) Log.Error("hotkey Ctrl+Alt+H registration failed (already used by another app?)");
    }

    // ── 트레이 메뉴 ──
    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // 맨 위 한 줄은 현재 상태. 누를 수 없는 안내 항목이라 비활성으로 둔다.
        _statusItem = new ToolStripMenuItem { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        _pauseItem = new ToolStripMenuItem("일시 중지(&P)", null, (_, _) => TogglePause()) { ShortcutKeyDisplayString = "Ctrl+Alt+H" };
        menu.Items.Add(_pauseItem);
        // 더블클릭과 같은 동작인 "설정"을 굵게: Windows 관행에서 굵은 항목이 기본 동작이다.
        var settingsItem = new ToolStripMenuItem("설정(&S)...", null, (_, _) => OpenSettings());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());

        var shape = new ToolStripMenuItem("모양(&M)");
        foreach (var (label, value) in Labels.Styles)
            AddRadio(shape, label, () => _settings.Style == value, () => _settings.Style = value);
        menu.Items.Add(shape);

        var place = new ToolStripMenuItem("위치(&L)");
        foreach (var (label, value) in Labels.Placements)
            AddRadio(place, label, () => _settings.Placement == value, () => _settings.Placement = value);
        menu.Items.Add(place);

        menu.Items.Add(PresetMenu("크기(&Z)", Labels.SizePresets, () => _settings.SizePercent, v => _settings.SizePercent = v));
        menu.Items.Add(PresetMenu("불투명도(&O)", Labels.OpacityPresets, () => _settings.OpacityPercent, v => _settings.OpacityPercent = v));

        menu.Items.Add(new ToolStripSeparator());
        _autostartItem = new ToolStripMenuItem("로그인 시 자동 시작(&A)", null, (_, _) =>
        {
            bool on = !Autostart.IsEnabled();
            if (!Autostart.Set(on))
                Dialogs.Warning("자동 시작 설정을 바꾸지 못했습니다.", "로그 폴더의 errors.log 에 원인이 기록되어 있습니다. (트레이 메뉴 → 정보 → 로그 폴더 열기)");
        });
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripMenuItem("업데이트 확인(&U)", null, (_, _) => CheckForUpdates(manual: true)));
        menu.Items.Add(new ToolStripMenuItem("정보(&I)...", null, (_, _) => OpenAbout()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("종료(&X)", null, (_, _) => Application.Exit()));

        // 메뉴를 열 때마다 체크 표시를 현재 상태에 맞춘다.
        menu.Opening += (_, _) =>
        {
            RefreshChecks(menu.Items);
            _statusItem.Text = "현재: " + StateText(_trayState);
            _pauseItem.Checked = _paused;
            _autostartItem.Checked = Autostart.IsEnabled();
        };
        ApplyMenuTheme(menu);
        return menu;
    }

    /// <summary>메뉴와 모든 하위 메뉴에 테마 렌더러를 적용하고, 열릴 때 Windows 11 식 둥근 모서리를 요청한다.</summary>
    static void ApplyMenuTheme(ToolStripDropDown menu)
    {
        var renderer = Theme.CreateMenuRenderer();
        void Walk(ToolStripDropDown dd)
        {
            dd.Renderer = renderer;
            dd.Opened -= RoundOnOpened; dd.Opened += RoundOnOpened;
            foreach (ToolStripItem it in dd.Items)
                if (it is ToolStripMenuItem mi && mi.HasDropDownItems) Walk(mi.DropDown);
        }
        Walk(menu);
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
            if (_tray.ContextMenuStrip is { } menu) ApplyMenuTheme(menu);   // 밝게/어둡게 전환을 따라간다
    }

    // ── 트레이 아이콘·툴팁 ──
    string StateText(ImeState state) => _paused ? "일시 중지" : state switch
    {
        ImeState.Hangul => "한글 입력",
        ImeState.English => "영문 입력",
        ImeState.OtherLang => "다른 언어 입력",
        _ => "입력 위치 없음",
    };

    /// <summary>툴팁: 이름 · 상태 · 단축키. NotifyIcon.Text 는 127자 제한이 있다.</summary>
    string TrayText(ImeState state)
    {
        string s = AppInfo.DisplayName + " · " + StateText(state);
        if (_paused) s += _settings.HotkeyEnabled ? " · Ctrl+Alt+H 로 재개" : "";
        else if (_settings.HotkeyEnabled) s += " · Ctrl+Alt+H 일시 중지";
        if (Log.Enabled) s += " [debug]";
        return s.Length > 127 ? s[..127] : s;
    }

    /// <summary>트레이 아이콘과 툴팁을 상태에 맞춘다. 같은 상태면 아무것도 하지 않는다(Shell_NotifyIcon 호출을 아낀다).</summary>
    void UpdateTray(ImeState state, bool force = false)
    {
        if (!_settings.TrayShowsState) state = ImeState.Unknown;
        if (!force && state == _trayState && _paused == _trayPaused) return;
        _trayState = state; _trayPaused = _paused;
        _tray.Icon = _trayIcons.Get(state, _paused, SystemInformation.SmallIconSize.Width, BadgeTheme.From(_settings));
        _tray.Text = TrayText(state);
    }

    /// <summary>색·DPI 가 바뀌어 캐시한 아이콘을 버리고 다시 그린다. 트레이가 버릴 아이콘을 가리키지 않도록 먼저 기본 아이콘으로 돌린다.</summary>
    void RefreshTray()
    {
        _tray.Icon = Icons.App;
        _trayIcons.Clear();
        UpdateTray(_trayState, force: true);
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
            custom.Text = $"사용자 지정 ({v}%)";
        };
        return menu;
    }

    /// <summary>설정이 바뀐 뒤 공통 처리: 저장(편집 중 미리 반영일 때는 생략), 다시 그리기, 단축키·주기 반영.</summary>
    void OnSettingsChanged(bool save = true)
    {
        _settings.Normalize();
        if (save) _store.Save(_settings);
        ResetRenderKey();                                   // 다음 틱에 강제로 다시 그림
        _flashUntil = DateTime.Now.AddMilliseconds(FlashMs); // DotFlash면 바로 글자를 한 번 보여 준다
        _timer.Interval = _settings.PollIntervalMs;
        ApplyHotkey();
        RefreshTray();   // 배지 색이나 "트레이에 상태 표시" 설정이 바뀌었을 수 있다
        Poll();
    }

    void ResetRenderKey() => _renderKey = (ImeState.Unknown, (BadgeStyle)(-1), 0, -1, "", "");

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
        _settingsForm = new SettingsForm(_settings);
        _settingsForm.Changed += () => OnSettingsChanged(save: false);   // 편집 중: 배지에 바로 반영, 저장은 아직
        _settingsForm.Applied += () => OnSettingsChanged();               // 확인·취소: 저장
        _settingsForm.FormClosed += (_, _) => { _settingsForm?.Dispose(); _settingsForm = null; };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    void OpenAbout()
    {
        if (_aboutForm is { IsDisposed: false }) { _aboutForm.Activate(); return; }
        _aboutForm = new AboutForm(_paths);
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
                    _tray.ShowBalloonTip(10000, "새 버전이 있습니다",
                        $"{AppInfo.ProductName} {info.Tag} 을(를) 받을 수 있습니다. (현재 {AppVersion.Display})\n클릭하면 다운로드 페이지가 열립니다.", ToolTipIcon.Info);
            }
            else if (manual)
            {
                Dialogs.Info(info is null ? "아직 정식 릴리스가 없습니다." : "최신 버전을 쓰고 있습니다.",
                    info is null ? $"현재 {AppVersion.Display}" : $"현재 {AppVersion.Display}, 최신 {info.Tag}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("update check failed", ex);
            if (manual)
                Dialogs.Warning("업데이트 정보를 가져오지 못했습니다.", "네트워크 연결을 확인한 뒤 다시 시도하세요.", ex.Message);
        }
    }

    /// <summary>수동 확인에서 새 버전을 찾았을 때: 열기 / 나중에 / 이 버전 건너뛰기.</summary>
    void OfferUpdate(UpdateInfo info)
    {
        int choice = Dialogs.Choose($"새 버전 {info.Tag} 이(가) 있습니다.", $"현재 {AppVersion.Display} 을(를) 쓰고 있습니다.", TaskDialogIcon.Information,
            ("다운로드 페이지 열기", "브라우저에서 릴리스 페이지를 엽니다."),
            ("나중에", "다음에 다시 알립니다."),
            ("이 버전 건너뛰기", $"{info.Tag} 은(는) 자동으로 알리지 않습니다. 더 새 버전이 나오면 다시 알립니다."));
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
        if (s.Caret is null || s.State == ImeState.Unknown)
        {
            if (s.Suppressed is not null) Log.WriteIfChanged($"suppressed: {s.Suppressed} fg='{Native.ClassName(s.Foreground)}'");
            HideBadge();
            return;
        }

        var caret = s.Caret.Value;
        if (s.State != _lastState)
        {
            _lastState = s.State;
            _flashUntil = DateTime.Now.AddMilliseconds(FlashMs);
        }
        UpdateTray(s.State);

        // DotFlash: 변경 직후 1.5초는 둥근 배지, 그 뒤는 점
        var style = _settings.Style;
        if (style == BadgeStyle.DotFlash)
            style = DateTime.Now < _flashUntil ? BadgeStyle.Pill : BadgeStyle.Dot;

        float scale = Native.DpiScaleAt(caret.Location) * _settings.SizePercent / 100f;

        var key = (s.State, style, scale, _settings.OpacityPercent, _settings.HangulColor, _settings.EnglishColor);
        bool needRender = key != _renderKey;

        Size bs = needRender ? Size.Empty : _bitmapSize;
        Bitmap? bmp = null;
        if (needRender)
        {
            bmp = BadgeRenderer.Render(s.State, style, scale, BadgeTheme.From(_settings), _settings.OpacityPercent);
            bs = bmp.Size;
        }

        var area = Screen.FromPoint(caret.Location).WorkingArea;
        var pos = BadgeLayout.Compute(new LayoutInput(caret, bs, style, _settings.Placement, scale, area));

        // FlowLauncher처럼 자기도 최상위(TopMost)인 창은 나중에 뜬 쪽이 위에 온다. 배지가 처음 보일 때,
        // 활성 창이 바뀌었을 때, 위치가 바뀌었을 때마다 최상위 창들 중에서도 맨 위로 다시 올린다.
        bool raise = !Visible || s.Foreground != _lastFg || _lastPos != pos;

        _allowShow = true;
        if (!Visible) { Show(); _timer.Interval = _settings.PollIntervalMs; }

        if (bmp is not null)
        {
            using (bmp) Present(bmp, pos);
            _renderKey = key;
            _bitmapSize = bs;
            if (raise) RaiseToTop();
        }
        else if (_lastPos != pos)
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, pos.X, pos.Y, 0, 0,
                                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);   // 이동과 동시에 맨 위로
        }
        else if (raise) RaiseToTop();

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

    /// <summary>비트맵을 픽셀별 알파로 창에 올리면서 위치·크기도 함께 지정한다.</summary>
    void Present(Bitmap bmp, Point pos)
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
            // 불투명도는 렌더러가 배경 픽셀의 알파로 이미 반영했다(글자는 또렷하게 유지). 창 전체 알파는 255 로 둔다.
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
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
            _settingsForm?.Dispose();
            _aboutForm?.Dispose();
            _tray.Visible = false;
            _tray.Icon = Icons.App;   // 캐시한 아이콘을 해제하기 전에 참조를 끊는다
            _tray.Dispose();
            _trayIcons.Dispose();
        }
        base.Dispose(disposing);
    }
}
