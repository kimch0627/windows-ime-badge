using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 설정 창. 바꾸는 즉시 실제 배지에 반영되어(WYSIWYG) 화면 밖 배지로 결과를 바로 볼 수 있고,
/// [확인]을 누르면 저장, [취소]나 닫기를 누르면 창을 열 때의 설정으로 되돌린다.
/// 편집은 복사본(<see cref="_draft"/>)에 하고 매번 실제 설정(<see cref="_live"/>)으로 복사한다.
/// 미리보기는 실제 렌더러(<see cref="BadgeRenderer"/>)로 그려 창 밖 배지와 똑같이 보인다.
/// </summary>
sealed class SettingsForm : Form
{
    readonly Settings _live;
    readonly Settings _draft;
    readonly Settings _original;   // 취소할 때 되돌릴 값
    bool _autostart, _dirty;

    ComboBox _style = null!, _placement = null!;
    TrackBar _size = null!, _opacity = null!;
    NumericUpDown _poll = null!;
    Button _hangulColor = null!, _englishColor = null!;
    CheckBox _autostartBox = null!, _fullscreen = null!, _hotkey = null!, _updates = null!, _trayStateBox = null!, _animate = null!;
    HotkeyBox _hotkeyBox = null!;
    ListBox _excludedList = null!;
    ComboBox _newProcess = null!;
    Panel _preview = null!;
    readonly ToolTip _tips = new() { AutoPopDelay = 12000 };

    /// <summary>편집 중 값이 바뀔 때마다 발생. 실제 설정에는 이미 복사되어 있으니 다시 그리기만 하면 된다(저장은 하지 않는다).</summary>
    public event Action? Changed;
    /// <summary>[확인]으로 확정되었거나 [취소]로 되돌려졌을 때 발생. 저장한다.</summary>
    public event Action? Applied;

    public SettingsForm(Settings live)
    {
        _live = live;
        _draft = live.Clone();
        _original = live.Clone();
        _autostart = Autostart.IsEnabled();

        Text = $"{AppInfo.ProductName} 설정";
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);   // 아래 픽셀 크기들은 96 DPI 기준. 고DPI 에서 WinForms 가 배율을 곱한다
        Font = Theme.DialogFont;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        Build();
        LoadDraftIntoControls();
        _dirty = false;   // 컨트롤 초기화로 생긴 변경 알림은 실제 변경이 아니다
        Theme.Apply(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    // ── 화면 구성 ──
    // 레이아웃 원칙: 자동 크기(AutoSize) 컨테이너 안에는 Dock 을 쓰지 않는다. AutoSize 부모는 자식 크기로 자기 크기를 정하고,
    // Dock 된 자식은 부모 크기로 자기 크기를 정하므로 서로를 기다리다 폭 0 으로 접힌다(제목이 세로로 찍히던 문제).
    // 그룹박스는 고정 폭(GroupWidth)을 주고 높이만 내용에 맞춘다. 96 DPI 기준 픽셀이며 고DPI 에서는 WinForms 가 배율을 곱한다.
    const int GroupWidth = 400;
    const int InnerWidth = GroupWidth - 2 * 12;

    void Build()
    {
        var root = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(12, 12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 왼쪽: 모양 + 동작
        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 8, 0) };
        left.Controls.Add(BuildLookGroup());
        left.Controls.Add(BuildBehaviorGroup());
        root.Controls.Add(left, 0, 0);

        // 오른쪽: 미리보기 + 제외 앱
        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        right.Controls.Add(BuildPreviewGroup());
        right.Controls.Add(BuildExcludeGroup());
        root.Controls.Add(right, 1, 0);

        // 아래: 버튼
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "확인", AutoSize = true };
        var reset = new Button { Text = "기본값 복원(&R)", AutoSize = true, Margin = new Padding(24, 3, 3, 3) };
        // 모드리스(Show) 창은 DialogResult 만으로는 닫히지 않는다. 명시적으로 닫는다.
        ok.Click += (_, _) => { Apply(); DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        reset.Click += (_, _) => { _draft.CopyFrom(new Settings()); LoadDraftIntoControls(); };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok); buttons.Controls.Add(reset);
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 2);

        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(root);
    }

    GroupBox BuildLookGroup()
    {
        var g = NewGroup("모양");
        var t = NewTable();

        _style = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _style.Items.AddRange(Labels.Styles.Select(s => (object)s.label).ToArray());
        _style.SelectedIndexChanged += (_, _) => { _draft.Style = Labels.Styles[Math.Max(0, _style.SelectedIndex)].value; Touch(); };
        AddRow(t, "배지 모양(&M)", _style);

        _placement = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _placement.Items.AddRange(Labels.Placements.Select(p => (object)p.label).ToArray());
        _placement.SelectedIndexChanged += (_, _) => { _draft.Placement = Labels.Placements[Math.Max(0, _placement.SelectedIndex)].value; Touch(); };
        AddRow(t, "위치(&L)", _placement);
        _tips.SetToolTip(_placement, "위쪽은 다음 줄 글자를 덜 가립니다. 화면 가장자리라 자리가 없으면 반대쪽으로 옮깁니다.");

        // 크기·불투명도는 눈으로 맞추는 값이라 숫자 입력보다 슬라이더가 자연스럽다(Windows 설정 앱과 같은 방식).
        AddRow(t, "크기(&Z)", Slider(out _size, 50, 300, 5, 25, v => { _draft.SizePercent = v; Touch(); }));
        AddRow(t, "불투명도(&O)", Slider(out _opacity, 30, 100, 5, 10, v => { _draft.OpacityPercent = v; Touch(); }));
        _tips.SetToolTip(_size, "모니터 DPI 배율에 추가로 곱해집니다.");
        _tips.SetToolTip(_opacity, "배지 배경만 비치고 글자는 또렷하게 유지됩니다.");

        _hangulColor = ColorButton(() => _draft.HangulColor, v => _draft.HangulColor = v);
        AddRow(t, "한글 배지 색(&H)", _hangulColor);
        _englishColor = ColorButton(() => _draft.EnglishColor, v => _draft.EnglishColor = v);
        AddRow(t, "영문 배지 색(&E)", _englishColor);
        _tips.SetToolTip(_hangulColor, "글자색(흰/검)은 고른 색의 밝기에 맞춰 자동으로 정해집니다.");
        _tips.SetToolTip(_englishColor, "글자색(흰/검)은 고른 색의 밝기에 맞춰 자동으로 정해집니다.");

        _animate = new CheckBox { Text = "나타날 때 서서히, 바뀔 때 살짝 커지는 효과(&N)", AutoSize = true };
        _animate.CheckedChanged += (_, _) => { _draft.Animate = _animate.Checked; Touch(); };
        AddRow(t, null, _animate);
        _tips.SetToolTip(_animate, "Windows 설정 → 접근성 → 시각 효과 → 애니메이션 효과가 꺼져 있으면 여기와 상관없이 생략합니다.");

        g.Controls.Add(t);
        return g;
    }

    GroupBox BuildBehaviorGroup()
    {
        var g = NewGroup("동작");
        var t = NewTable();

        _autostartBox = new CheckBox { Text = "Windows 로그인 시 자동 시작(&A)", AutoSize = true };
        _autostartBox.CheckedChanged += (_, _) => _autostart = _autostartBox.Checked;   // 레지스트리는 [확인] 때만 만진다
        AddRow(t, null, _autostartBox);

        _fullscreen = new CheckBox { Text = "전체 화면 앱(게임·동영상)에서는 숨김(&F)", AutoSize = true };
        _fullscreen.CheckedChanged += (_, _) => { _draft.HideOnFullscreen = _fullscreen.Checked; Touch(); };
        AddRow(t, null, _fullscreen);

        _trayStateBox = new CheckBox { Text = "트레이 아이콘에도 한/영 상태 표시(&T)", AutoSize = true };
        _trayStateBox.CheckedChanged += (_, _) => { _draft.TrayShowsState = _trayStateBox.Checked; Touch(); };
        AddRow(t, null, _trayStateBox);

        // 단축키: 체크박스 + 키 조합을 받는 입력칸을 한 줄에.
        var hotkeyRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        _hotkey = new CheckBox { Text = "단축키로 일시 중지 켜기/끄기(&K)", AutoSize = true, Margin = new Padding(0, 4, 8, 0) };
        _hotkey.CheckedChanged += (_, _) => { _draft.HotkeyEnabled = _hotkey.Checked; _hotkeyBox.Enabled = _hotkey.Checked; Touch(); };
        _hotkeyBox = new HotkeyBox { Width = 130 };
        _hotkeyBox.HotkeyChanged += spec => { _draft.Hotkey = spec.ToString(); Touch(); };
        _tips.SetToolTip(_hotkeyBox, "여기를 클릭한 뒤 원하는 키 조합을 누르세요. Ctrl, Alt, Win 중 하나가 들어가야 합니다.");
        hotkeyRow.Controls.Add(_hotkey);
        hotkeyRow.Controls.Add(_hotkeyBox);
        AddRow(t, null, hotkeyRow);

        _updates = new CheckBox { Text = "새 버전이 나오면 알림 (하루 한 번 확인)(&U)", AutoSize = true };
        _updates.CheckedChanged += (_, _) => { _draft.CheckForUpdates = _updates.Checked; Touch(); };
        AddRow(t, null, _updates);
        _tips.SetToolTip(_updates, "GitHub Releases 에서 정식 버전만 확인합니다. 끄면 네트워크 접속이 전혀 없습니다.");

        _poll = new NumericUpDown { Minimum = 50, Maximum = 1000, Increment = 50, Width = 80 };
        _poll.ValueChanged += (_, _) => { _draft.PollIntervalMs = (int)_poll.Value; Touch(); };
        AddRow(t, "확인 주기 (ms)(&I)", _poll);
        AddRow(t, null, new Label { Text = "작을수록 빨리 반응하고 CPU 를 조금 더 씁니다. 기본 100.", ForeColor = SystemColors.GrayText, AutoSize = true });
        _tips.SetToolTip(_poll, "한/영 상태를 다시 읽는 간격입니다. 배지가 한동안 안 보이면 자동으로 3배 느려집니다.");

        g.Controls.Add(t);
        return g;
    }

    GroupBox BuildPreviewGroup()
    {
        var g = NewGroup("미리보기");
        _preview = new Panel { Location = ContentOrigin, Width = InnerWidth, Height = 120, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Tag = "custom-paint" };
        _preview.Paint += (_, e) => PaintPreview(e.Graphics);
        _tips.SetToolTip(_preview, "왼쪽은 밝은 배경(메모장), 오른쪽은 어두운 배경(VS Code 등)에서의 모습입니다.");
        g.Controls.Add(_preview);
        return g;
    }

    GroupBox BuildExcludeGroup()
    {
        var g = NewGroup("배지를 띄우지 않을 앱");
        var t = NewTable();

        _excludedList = new ListBox { Width = InnerWidth - 6, Height = 72, IntegralHeight = false };
        AddRow(t, null, _excludedList);

        // 실행 중인 앱에서 고르거나 이름을 직접 쓴다. 목록은 펼칠 때마다 새로 읽는다.
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        _newProcess = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 190, Margin = new Padding(0, 0, 4, 0) };
        _newProcess.DropDown += (_, _) => FillRunningApps();
        _newProcess.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddExcluded(); e.SuppressKeyPress = true; } };
        _tips.SetToolTip(_newProcess, "실행 중인 앱을 고르거나 실행 파일 이름을 직접 입력하세요. 끝에 * 를 붙이면 앞부분만 맞으면 됩니다.");
        var add = new Button { Text = "추가(&D)", AutoSize = true, Margin = new Padding(0, 0, 4, 0) };
        add.Click += (_, _) => AddExcluded();
        var remove = new Button { Text = "삭제(&X)", AutoSize = true, Margin = Padding.Empty };
        remove.Click += (_, _) => RemoveExcluded();
        _excludedList.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveExcluded(); };
        row.Controls.Add(_newProcess); row.Controls.Add(add); row.Controls.Add(remove);
        AddRow(t, null, row);

        AddRow(t, null, new Label
        {
            Text = "실행 파일 이름(.exe 생략 가능). 끝에 * 를 붙이면 앞부분만 맞으면 됩니다.\n예)  mstsc  /  vmware-vmx  /  Unreal*",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
        });
        g.Controls.Add(t);
        return g;
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
            string typed = _newProcess.Text;
            _newProcess.Items.Clear();
            _newProcess.Items.AddRange(names);
            _newProcess.Text = typed;
        }
        catch (Exception ex) { Log.Error("list running apps failed", ex); }
        finally { if (procs is not null) foreach (var p in procs) p.Dispose(); }
    }

    void AddExcluded()
    {
        string name = _newProcess.Text.Trim();
        if (name.Length == 0) return;
        if (!_draft.ExcludedProcesses.Any(x => string.Equals(ProcessFilter.Normalize(x), ProcessFilter.Normalize(name), StringComparison.OrdinalIgnoreCase)))
        {
            _draft.ExcludedProcesses.Add(name);
            RefreshExcludedList();
            Touch();
        }
        _newProcess.Text = "";
        _newProcess.Focus();
    }

    void RemoveExcluded()
    {
        int i = _excludedList.SelectedIndex;
        if (i < 0 || i >= _draft.ExcludedProcesses.Count) return;
        _draft.ExcludedProcesses.RemoveAt(i);
        RefreshExcludedList();
        if (_excludedList.Items.Count > 0) _excludedList.SelectedIndex = Math.Min(i, _excludedList.Items.Count - 1);
        Touch();
    }

    void RefreshExcludedList()
    {
        _excludedList.BeginUpdate();
        _excludedList.Items.Clear();
        foreach (var p in _draft.ExcludedProcesses) _excludedList.Items.Add(p);
        _excludedList.EndUpdate();
    }

    // ── 도우미 ──
    /// <summary>그룹박스 안에서 내용이 시작하는 위치(제목 줄 아래).</summary>
    static readonly Point ContentOrigin = new(12, 26);

    /// <summary>폭은 고정(GroupWidth), 높이는 내용에 맞춰 자란다. 자식은 Dock 없이 <see cref="ContentOrigin"/> 에 둔다.</summary>
    static GroupBox NewGroup(string title) =>
        new CardGroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(GroupWidth, 0),
            Padding = new Padding(12, 4, 12, 12),
            Margin = new Padding(0, 0, 0, 10),
        };

    static TableLayoutPanel NewTable()
    {
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = ContentOrigin };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return t;
    }

    static void AddRow(TableLayoutPanel t, string? label, Control c)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        c.Margin = new Padding(3, 4, 3, 4);
        if (label is null)
        {
            t.Controls.Add(c, 0, row);
            t.SetColumnSpan(c, 2);
        }
        else
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 4) }, 0, row);
            t.Controls.Add(c, 1, row);
        }
    }

    /// <summary>
    /// 퍼센트 슬라이더 + 현재 값 라벨. 마우스로 끌면 <paramref name="step"/> 단위로 맞춰 준다(예: 137% → 135%).
    /// 키보드 화살표는 step 씩, Page Up/Down 은 <paramref name="page"/> 씩 움직인다.
    /// </summary>
    static Control Slider(out TrackBar bar, int min, int max, int step, int page, Action<int> onChange)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        var tb = new TrackBar
        {
            Minimum = min, Maximum = max, SmallChange = step, LargeChange = page, TickFrequency = page,
            AutoSize = false, Width = 170, Height = 30, Margin = new Padding(0, 0, 4, 0),
        };
        var value = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0), Text = $"{tb.Value}%" };   // 값이 최소값 그대로면 ValueChanged 가 안 오므로 미리 적는다
        tb.ValueChanged += (_, _) =>
        {
            int v = (int)Math.Round(tb.Value / (double)step) * step;
            v = Math.Clamp(v, min, max);
            if (v != tb.Value) { tb.Value = v; return; }   // 되돌아와서 다시 처리된다
            value.Text = $"{v}%";
            onChange(v);
        };
        panel.Controls.Add(tb);
        panel.Controls.Add(value);
        bar = tb;
        return panel;
    }

    Button ColorButton(Func<string> get, Action<string> set)
    {
        var b = new Button { Width = 120, Height = 26, TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat, Tag = "color" };
        b.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = ToColor(get()), FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            set(ColorHex.ToHex(dlg.Color.ToArgb()));
            PaintColorButton(b, get());
            Touch();
        };
        return b;
    }

    /// <summary>버튼을 그 색으로 칠하고, 글자색은 배지와 같은 규칙(<see cref="BadgeRenderer.TextColorOn"/>)으로 고른다.</summary>
    static void PaintColorButton(Button b, string hex)
    {
        var c = ToColor(hex);
        b.BackColor = c;
        b.ForeColor = BadgeRenderer.TextColorOn(c);
        b.FlatAppearance.BorderColor = ControlPaint.Dark(c, 0.1f);
        b.Text = hex.ToUpperInvariant();
    }

    static Color ToColor(string hex) => Color.FromArgb(ColorHex.TryParse(hex, out int a) ? a : unchecked((int)0xFF000000));

    void LoadDraftIntoControls()
    {
        _style.SelectedIndex = Array.FindIndex(Labels.Styles, s => s.value == _draft.Style);
        _placement.SelectedIndex = Array.FindIndex(Labels.Placements, p => p.value == _draft.Placement);
        _size.Value = Math.Clamp(_draft.SizePercent, _size.Minimum, _size.Maximum);
        _opacity.Value = Math.Clamp(_draft.OpacityPercent, _opacity.Minimum, _opacity.Maximum);
        _poll.Value = Math.Clamp(_draft.PollIntervalMs, (int)_poll.Minimum, (int)_poll.Maximum);
        PaintColorButton(_hangulColor, _draft.HangulColor);
        PaintColorButton(_englishColor, _draft.EnglishColor);
        _animate.Checked = _draft.Animate;
        _autostartBox.Checked = _autostart;
        _fullscreen.Checked = _draft.HideOnFullscreen;
        _trayStateBox.Checked = _draft.TrayShowsState;
        _hotkey.Checked = _draft.HotkeyEnabled;
        _hotkeyBox.Enabled = _draft.HotkeyEnabled;
        _hotkeyBox.Text = _draft.Hotkey;
        _updates.Checked = _draft.CheckForUpdates;
        RefreshExcludedList();
        Touch();   // 컨트롤 값이 이미 같아서 이벤트가 안 온 항목까지 한 번에 반영
    }

    /// <summary>편집 내용을 실제 설정에 복사하고 알린다. 창 밖 배지가 바로 바뀐다.</summary>
    void Touch()
    {
        _draft.Normalize();
        _live.CopyFrom(_draft);
        _dirty = true;
        Changed?.Invoke();
        RefreshPreview();
    }

    void RefreshPreview() => _preview?.Invalidate();

    // 미리보기 배경. 왼쪽은 흰 종이(메모장), 오른쪽은 어두운 편집기(VS Code 기본 테마)와 비슷한 색.
    static readonly Color LightBg = Color.White, LightText = Color.Black;
    static readonly Color DarkBg = Color.FromArgb(0x1E, 0x1E, 0x1E), DarkText = Color.FromArgb(0xD4, 0xD4, 0xD4);

    /// <summary>
    /// "안녕하세요|" 와 "hello|" 두 줄 옆에 실제 렌더러로 그린 배지를 놓는다.
    /// 왼쪽 절반은 밝은 배경, 오른쪽 절반은 어두운 배경이라 어느 편집기에서 써도 어떻게 보일지 한 번에 확인할 수 있다.
    /// </summary>
    void PaintPreview(Graphics g)
    {
        float dpi = DeviceDpi / 96f;
        var client = _preview.ClientRectangle;
        int half = client.Width / 2;
        PaintPreviewHalf(g, new Rectangle(client.Left, client.Top, half, client.Height), LightBg, LightText, dpi);
        PaintPreviewHalf(g, new Rectangle(client.Left + half, client.Top, client.Width - half, client.Height), DarkBg, DarkText, dpi);
    }

    void PaintPreviewHalf(Graphics g, Rectangle area, Color bg, Color fg, float dpi)
    {
        using (var bgBrush = new SolidBrush(bg)) g.FillRectangle(bgBrush, area);
        float scale = dpi * _draft.SizePercent / 100f;
        var theme = BadgeTheme.From(_draft);
        var style = _draft.Style == BadgeStyle.DotFlash ? BadgeStyle.Pill : _draft.Style;
        using var font = new Font(BadgeRenderer.FontFamily, 11f);
        using var textBrush = new SolidBrush(fg);
        using var caretPen = new Pen(fg);

        var samples = new[] { (ImeState.Hangul, "안녕하세요"), (ImeState.English, "hello") };
        int lineH = (int)(48 * dpi);
        for (int i = 0; i < samples.Length; i++)
        {
            var (state, text) = samples[i];
            var textSize = g.MeasureString(text, font);
            float x = area.Left + 14 * dpi, y = area.Top + 20 * dpi + i * lineH;
            g.DrawString(text, font, textBrush, x, y);
            // 텍스트 끝에 세로 caret 을 그리고, 그 caret 기준으로 배지 위치를 계산한다.
            var caret = new Rectangle((int)(x + textSize.Width - 2 * dpi), (int)y, 1, (int)textSize.Height);
            g.DrawLine(caretPen, caret.Left, caret.Top, caret.Left, caret.Bottom);

            using var bmp = BadgeRenderer.Render(state, style, scale, theme, _draft.OpacityPercent);
            var pos = BadgeLayout.Compute(new LayoutInput(caret, bmp.Size, style, _draft.Placement, scale, area));
            // 픽셀 크기를 명시한다. Point 만 주는 오버로드는 비트맵의 DPI(96)와 화면 DPI 차이만큼 확대해 버린다.
            g.DrawImage(bmp, new Rectangle(pos, bmp.Size), new Rectangle(Point.Empty, bmp.Size), GraphicsUnit.Pixel);
        }
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
        if (e.Cancel || DialogResult == DialogResult.OK || !_dirty) return;
        _live.CopyFrom(_original);
        _dirty = false;
        Applied?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
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
        PlaceholderText = "키 조합을 누르세요";
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
