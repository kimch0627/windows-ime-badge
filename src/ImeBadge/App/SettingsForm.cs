using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 설정 창. 편집은 복사본(<see cref="_draft"/>)에 하고, [확인]을 눌러야 실제 설정에 반영된다.
/// 미리보기는 실제 렌더러(<see cref="BadgeRenderer"/>)로 그려 창 밖 배지와 똑같이 보인다.
/// </summary>
sealed class SettingsForm : Form
{
    readonly Settings _live;
    readonly Settings _draft;
    bool _autostart;

    ComboBox _style = null!, _placement = null!;
    NumericUpDown _size = null!, _opacity = null!, _poll = null!;
    Button _hangulColor = null!, _englishColor = null!;
    CheckBox _autostartBox = null!, _fullscreen = null!, _hotkey = null!, _updates = null!;
    TextBox _excluded = null!;
    Panel _preview = null!;

    /// <summary>[확인]으로 설정이 실제 반영된 뒤 발생.</summary>
    public event Action? Applied;

    static readonly (string label, BadgeStyle value)[] Styles =
    {
        ("사각 배지  [한]", BadgeStyle.Box),
        ("둥근 배지  (한)", BadgeStyle.Pill),
        ("점  ●", BadgeStyle.Dot),
        ("밑줄  ▬", BadgeStyle.Underline),
        ("점 + 바뀔 때만 글자", BadgeStyle.DotFlash),
    };
    static readonly (string label, BadgePlacement value)[] Placements =
    {
        ("커서 오른쪽 위", BadgePlacement.AboveRight),
        ("커서 오른쪽 아래", BadgePlacement.BelowRight),
    };

    public SettingsForm(Settings live)
    {
        _live = live;
        _draft = live.Clone();
        _autostart = Autostart.IsEnabled();

        Text = $"{AppInfo.ProductName} 설정";
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);   // 아래 픽셀 크기들은 96 DPI 기준. 고DPI 에서 WinForms 가 배율을 곱한다
        Font = new Font(BadgeRenderer.FontFamily, 9f);
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        Build();
        LoadDraftIntoControls();
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
        var reset = new Button { Text = "기본값 복원", AutoSize = true, Margin = new Padding(24, 3, 3, 3) };
        ok.Click += (_, _) => { Apply(); DialogResult = DialogResult.OK; };
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
        _style.Items.AddRange(Styles.Select(s => (object)s.label).ToArray());
        _style.SelectedIndexChanged += (_, _) => { _draft.Style = Styles[Math.Max(0, _style.SelectedIndex)].value; RefreshPreview(); };
        AddRow(t, "배지 모양", _style);

        _placement = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _placement.Items.AddRange(Placements.Select(p => (object)p.label).ToArray());
        _placement.SelectedIndexChanged += (_, _) => { _draft.Placement = Placements[Math.Max(0, _placement.SelectedIndex)].value; RefreshPreview(); };
        AddRow(t, "위치", _placement);

        _size = new NumericUpDown { Minimum = 50, Maximum = 300, Increment = 10, Width = 80 };
        _size.ValueChanged += (_, _) => { _draft.SizePercent = (int)_size.Value; RefreshPreview(); };
        AddRow(t, "크기 (%)", _size);

        _opacity = new NumericUpDown { Minimum = 30, Maximum = 100, Increment = 5, Width = 80 };
        _opacity.ValueChanged += (_, _) => { _draft.OpacityPercent = (int)_opacity.Value; RefreshPreview(); };
        AddRow(t, "불투명도 (%)", _opacity);

        _hangulColor = ColorButton(() => _draft.HangulColor, v => _draft.HangulColor = v);
        AddRow(t, "한글 배지 색", _hangulColor);
        _englishColor = ColorButton(() => _draft.EnglishColor, v => _draft.EnglishColor = v);
        AddRow(t, "영문 배지 색", _englishColor);

        g.Controls.Add(t);
        return g;
    }

    GroupBox BuildBehaviorGroup()
    {
        var g = NewGroup("동작");
        var t = NewTable();

        _autostartBox = new CheckBox { Text = "Windows 로그인 시 자동 시작", AutoSize = true };
        _autostartBox.CheckedChanged += (_, _) => _autostart = _autostartBox.Checked;
        AddRow(t, null, _autostartBox);

        _fullscreen = new CheckBox { Text = "전체 화면 앱(게임·동영상)에서는 숨김", AutoSize = true };
        _fullscreen.CheckedChanged += (_, _) => _draft.HideOnFullscreen = _fullscreen.Checked;
        AddRow(t, null, _fullscreen);

        _hotkey = new CheckBox { Text = "Ctrl+Alt+H 로 일시 중지 켜기/끄기", AutoSize = true };
        _hotkey.CheckedChanged += (_, _) => _draft.HotkeyEnabled = _hotkey.Checked;
        AddRow(t, null, _hotkey);

        _updates = new CheckBox { Text = "새 버전이 나오면 알림 (하루 한 번 확인)", AutoSize = true };
        _updates.CheckedChanged += (_, _) => _draft.CheckForUpdates = _updates.Checked;
        AddRow(t, null, _updates);

        _poll = new NumericUpDown { Minimum = 50, Maximum = 1000, Increment = 50, Width = 80 };
        _poll.ValueChanged += (_, _) => _draft.PollIntervalMs = (int)_poll.Value;
        AddRow(t, "확인 주기 (ms)", _poll);
        AddRow(t, null, new Label { Text = "작을수록 빨리 반응하고 CPU 를 조금 더 씁니다. 기본 100.", ForeColor = SystemColors.GrayText, AutoSize = true });

        g.Controls.Add(t);
        return g;
    }

    GroupBox BuildPreviewGroup()
    {
        var g = NewGroup("미리보기");
        _preview = new Panel { Location = ContentOrigin, Width = InnerWidth, Height = 120, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        _preview.Paint += (_, e) => PaintPreview(e.Graphics);
        g.Controls.Add(_preview);
        return g;
    }

    GroupBox BuildExcludeGroup()
    {
        var g = NewGroup("배지를 띄우지 않을 앱");
        var t = NewTable();
        _excluded = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = InnerWidth - 6, Height = 90, AcceptsReturn = true };
        _excluded.TextChanged += (_, _) =>
            _draft.ExcludedProcesses = _excluded.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        AddRow(t, null, _excluded);
        AddRow(t, null, new Label
        {
            Text = "한 줄에 하나, 실행 파일 이름(.exe 생략 가능).\n끝에 * 를 붙이면 앞부분만 맞으면 됩니다.\n예)  mstsc  /  vmware-vmx  /  Unreal*",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
        });
        g.Controls.Add(t);
        return g;
    }

    // ── 도우미 ──
    /// <summary>그룹박스 안에서 내용이 시작하는 위치(제목 줄 아래).</summary>
    static readonly Point ContentOrigin = new(12, 26);

    /// <summary>폭은 고정(GroupWidth), 높이는 내용에 맞춰 자란다. 자식은 Dock 없이 <see cref="ContentOrigin"/> 에 둔다.</summary>
    static GroupBox NewGroup(string title) =>
        new()
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

    Button ColorButton(Func<string> get, Action<string> set)
    {
        var b = new Button { Width = 120, Height = 26, TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat };
        b.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = ToColor(get()), FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            set(ColorHex.ToHex(dlg.Color.ToArgb()));
            PaintColorButton(b, get());
            RefreshPreview();
        };
        return b;
    }

    static void PaintColorButton(Button b, string hex)
    {
        var c = ToColor(hex);
        b.BackColor = c;
        b.ForeColor = c.GetBrightness() < 0.55f ? Color.White : Color.Black;
        b.Text = hex.ToUpperInvariant();
    }

    static Color ToColor(string hex) => Color.FromArgb(ColorHex.TryParse(hex, out int a) ? a : unchecked((int)0xFF000000));

    void LoadDraftIntoControls()
    {
        _style.SelectedIndex = Array.FindIndex(Styles, s => s.value == _draft.Style);
        _placement.SelectedIndex = Array.FindIndex(Placements, p => p.value == _draft.Placement);
        _size.Value = Math.Clamp(_draft.SizePercent, (int)_size.Minimum, (int)_size.Maximum);
        _opacity.Value = Math.Clamp(_draft.OpacityPercent, (int)_opacity.Minimum, (int)_opacity.Maximum);
        _poll.Value = Math.Clamp(_draft.PollIntervalMs, (int)_poll.Minimum, (int)_poll.Maximum);
        PaintColorButton(_hangulColor, _draft.HangulColor);
        PaintColorButton(_englishColor, _draft.EnglishColor);
        _autostartBox.Checked = _autostart;
        _fullscreen.Checked = _draft.HideOnFullscreen;
        _hotkey.Checked = _draft.HotkeyEnabled;
        _updates.Checked = _draft.CheckForUpdates;
        _excluded.Text = string.Join(Environment.NewLine, _draft.ExcludedProcesses);
        RefreshPreview();
    }

    void RefreshPreview() => _preview?.Invalidate();

    /// <summary>"안녕하세요|" 와 "hello|" 두 줄 옆에 실제 렌더러로 그린 배지를 놓는다.</summary>
    void PaintPreview(Graphics g)
    {
        g.Clear(Color.White);
        float dpi = DeviceDpi / 96f;
        float scale = dpi * _draft.SizePercent / 100f;
        var theme = BadgeTheme.From(_draft);
        var style = _draft.Style == BadgeStyle.DotFlash ? BadgeStyle.Pill : _draft.Style;
        using var font = new Font(BadgeRenderer.FontFamily, 11f);
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(new ColorMatrix { Matrix33 = _draft.OpacityPercent / 100f });

        var samples = new[] { (ImeState.Hangul, "안녕하세요"), (ImeState.English, "hello") };
        int lineH = (int)(48 * dpi);
        for (int i = 0; i < samples.Length; i++)
        {
            var (state, text) = samples[i];
            var textSize = g.MeasureString(text, font);
            float x = 14 * dpi, y = 20 * dpi + i * lineH;
            g.DrawString(text, font, Brushes.Black, x, y);
            // 텍스트 끝에 세로 caret 을 그리고, 그 caret 기준으로 배지 위치를 계산한다.
            var caret = new Rectangle((int)(x + textSize.Width - 2 * dpi), (int)y, 1, (int)textSize.Height);
            g.DrawLine(Pens.Black, caret.Left, caret.Top, caret.Left, caret.Bottom);

            using var bmp = BadgeRenderer.Render(state, style, scale, theme);
            var pos = BadgeLayout.Compute(new LayoutInput(caret, bmp.Size, style, _draft.Placement, scale, _preview.ClientRectangle));
            g.DrawImage(bmp, new Rectangle(pos, bmp.Size), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
        }
    }

    void Apply()
    {
        _draft.Normalize();
        _live.CopyFrom(_draft);
        if (_autostart != Autostart.IsEnabled()) Autostart.Set(_autostart);
        Applied?.Invoke();
    }
}
