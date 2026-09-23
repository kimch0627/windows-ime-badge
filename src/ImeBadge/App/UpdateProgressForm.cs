using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 자동 업그레이드가 도는 동안 보여 주는 작은 창. 한 줄 설명 + 진행 막대 + 취소 버튼뿐이다.
/// 진행 상황은 <see cref="Report"/> 로만 바뀌며(UI 스레드에서 호출), 창을 닫거나 취소를 누르면 <see cref="Cancelled"/> 가 불린다.
/// </summary>
sealed class UpdateProgressForm : Form
{
    readonly Label _status;
    readonly ProgressStrip _bar;
    readonly AccentButton _cancel;
    bool _done;

    /// <summary>사용자가 취소(또는 창 닫기)를 눌렀다.</summary>
    public event Action? Cancelled;

    public UpdateProgressForm(string tag)
    {
        Text = Strings.Format("update.progress.title", tag);
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = Theme.DialogFont;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24);

        // AutoSize 폼에서는 Dock 을 쓰지 않는다(AboutForm 의 설명 참고).
        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(24, 24) };
        _status = new Label { Text = Strings.Get("update.preparing"), AutoSize = true, Margin = new Padding(0, 0, 0, 12), MaximumSize = new Size(380, 0) };
        _bar = new ProgressStrip { Width = 380, Margin = new Padding(0, 0, 0, 16) };
        _cancel = new AccentButton { Text = Strings.Get("update.cancel"), AutoSize = true, Anchor = AnchorStyles.Right };
        _cancel.Click += (_, _) => { if (!_done) Cancelled?.Invoke(); };

        root.Controls.Add(_status);
        root.Controls.Add(_bar);
        root.Controls.Add(_cancel);
        Controls.Add(root);
        CancelButton = _cancel;
        Theme.Apply(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this);
    }

    /// <summary>설명 한 줄과 진행률(0~100, 음수면 "진행 중" 표시만).</summary>
    public void Report(string text, int percent)
    {
        if (IsDisposed) return;
        _status.Text = text;
        _bar.Indeterminate = percent < 0;
        _bar.Value = Math.Clamp(percent, 0, 100);
    }

    /// <summary>더 이상 취소할 수 없는 단계(적용 중). 취소 버튼을 잠근다.</summary>
    public void Finish(string text)
    {
        _done = true;
        Report(text, 100);
        _cancel.Enabled = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_done && e.CloseReason == CloseReason.UserClosing) Cancelled?.Invoke();
        base.OnFormClosing(e);
    }
}

/// <summary>
/// Windows 11 모양의 얇은 진행 막대. 기본 <see cref="ProgressBar"/> 는 테마 엔진이 초록·파랑으로 그려
/// 어두운 테마와 어울리지 않으므로 직접 그린다. <see cref="Indeterminate"/> 면 작은 조각이 좌우로 흐른다.
/// </summary>
sealed class ProgressStrip : ThemedControl
{
    const int Height1 = 4;       // 막대 두께(논리 픽셀)
    const int SweepMs = 1400;    // "진행 중" 조각이 한 번 지나가는 시간
    int _value;
    bool _indeterminate;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 32 };
    long _start = Environment.TickCount64;

    public ProgressStrip()
    {
        TabStop = false;
        Height = 10;
        _timer.Tick += (_, _) => Invalidate();
    }

    public int Value
    {
        get => _value;
        set { int v = Math.Clamp(value, 0, 100); if (v == _value) return; _value = v; Invalidate(); }
    }

    public bool Indeterminate
    {
        get => _indeterminate;
        set
        {
            if (value == _indeterminate) return;
            _indeterminate = value;
            if (value) { _start = Environment.TickCount64; _timer.Start(); } else _timer.Stop();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        float h = Px(Height1);
        var track = new RectangleF(0, (Height - h) / 2f, Width, h);
        using (var path = Rounded(track, h / 2f))
        using (var brush = new SolidBrush(Alpha(p.Track, 90)))
            g.FillPath(brush, path);

        RectangleF fill;
        if (_indeterminate)
        {
            float w = Math.Max(h * 4, Width * 0.3f);
            float t = (Environment.TickCount64 - _start) % SweepMs / (float)SweepMs;
            fill = new RectangleF(track.Left + t * (Width + w) - w, track.Top, w, h);
            fill = RectangleF.Intersect(fill, track);
        }
        else fill = new RectangleF(track.Left, track.Top, Width * _value / 100f, h);

        if (fill.Width < h / 2f) return;
        using (var path = Rounded(fill, h / 2f))
        using (var brush = new SolidBrush(p.Accent))
            g.FillPath(brush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
