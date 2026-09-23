using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 직접 그리는 컨트롤의 공통 기반. WinForms 기본 컨트롤(체크박스·TrackBar·Button)은 테마 엔진이 그려서 어두운 배경과 어울리지 않고
/// Windows 11 모양과도 다르다. 여기 컨트롤들은 <see cref="Theme.Palette"/> 색으로 Windows 11 설정 앱과 같은 모양을 그린다.
/// 마우스 올림(Hot)·누름(Pressed)·포커스 상태를 관리하고, 키보드 포커스 표시(focus ring)는 키보드로 왔을 때만 그린다.
/// </summary>
abstract class ThemedControl : Control
{
    Theme.Palette _palette = Theme.Current;
    public Theme.Palette Palette { get => _palette; set { _palette = value; Invalidate(); } }

    protected bool Hot { get; private set; }
    protected bool Pressed { get; private set; }
    protected float Dpi => DeviceDpi / 96f;
    protected int Px(float logical) => (int)Math.Round(logical * Dpi);

    protected ThemedControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hot = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected static GraphicsPath Rounded(RectangleF r, float radius)
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

    protected static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    /// <summary>키보드로 포커스가 왔을 때만 보이는 2px 테두리(Windows 11 방식).</summary>
    protected void DrawFocusRing(Graphics g, RectangleF r, float radius)
    {
        if (!Focused || !ShowFocusCues) return;
        r.Inflate(Px(2), Px(2));
        using var path = Rounded(r, radius + Px(2));
        using var pen = new Pen(Palette.Text, Px(2));
        g.DrawPath(pen, path);
    }

    protected TextFormatFlags TextFlags(TextFormatFlags extra = TextFormatFlags.Default) =>
        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | extra | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix);

    protected static Color Alpha(Color c, int alpha) => Color.FromArgb(alpha, c);
    protected static float Ease(float t) => 1f - (1f - t) * (1f - t);
}

/// <summary>Windows 11 토글 스위치. 켜짐은 강조색, 손잡이는 짧게 미끄러진다(Windows "애니메이션 효과"가 꺼져 있으면 즉시).</summary>
sealed class ToggleSwitch : ThemedControl
{
    const int TrackW = 40, TrackH = 20, Gap = 10, AnimMs = 120;
    bool _checked;
    float _pos;   // 0 = 왼쪽(꺼짐), 1 = 오른쪽(켜짐)
    long _animStart = -1;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (Native.AnimationsEnabled() && IsHandleCreated && Visible) { _animStart = Environment.TickCount64; _timer.Start(); }
            else { _pos = value ? 1f : 0f; Invalidate(); }
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToggleSwitch()
    {
        Cursor = Cursors.Hand;
        _timer.Tick += (_, _) =>
        {
            float t = Math.Clamp((Environment.TickCount64 - _animStart) / (float)AnimMs, 0f, 1f);
            float target = _checked ? 1f : 0f, from = _checked ? 0f : 1f;
            _pos = from + (target - from) * Ease(t);
            if (t >= 1f) _timer.Stop();
            Invalidate();
        };
    }

    public override Size GetPreferredSize(Size proposed)
    {
        var ts = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(Px(TrackW) + Px(Gap) + ts.Width + Px(2), Math.Max(Px(TrackH) + Px(4), ts.Height + Px(4)));
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); _pos = _checked ? 1f : 0f; if (AutoSize) Size = GetPreferredSize(Size.Empty); }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { Checked = !Checked; e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override bool ProcessMnemonic(char charCode)
    {
        if (!Enabled || !Visible || !IsMnemonic(charCode, Text)) return false;
        Focus(); Checked = !Checked; return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        int alpha = Enabled ? 255 : 90;

        var track = new RectangleF(Px(1), (Height - Px(TrackH)) / 2f, Px(TrackW), Px(TrackH));
        float knobR = Px(Hot ? 7 : 6);
        if (Pressed) knobR = Px(7);
        float cx = track.Left + Px(10) + _pos * (track.Width - Px(20));
        float cy = track.Top + track.Height / 2f;

        // 켜짐과 꺼짐 사이를 손잡이 위치에 맞춰 섞는다(켜짐: 강조색 채움, 꺼짐: 테두리만).
        using (var path = Rounded(track, track.Height / 2f))
        {
            var on = Hot ? p.AccentHover : p.Accent;
            using var fill = new SolidBrush(Alpha(on, (int)(alpha * _pos)));
            using var offFill = new SolidBrush(Alpha(p.Hover, (int)(alpha * (Hot ? 1f : 0f) * (1f - _pos))));
            using var border = new Pen(Alpha(p.Track, (int)(alpha * (1f - _pos))), Px(1));
            g.FillPath(offFill, path);
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
        var knobColor = _pos > 0.5f ? p.OnAccent : p.Text;
        using (var knob = new SolidBrush(Alpha(knobColor, alpha)))
            g.FillEllipse(knob, cx - knobR, cy - knobR, knobR * 2, knobR * 2);

        var textRect = new Rectangle((int)track.Right + Px(Gap), 0, Width - (int)track.Right - Px(Gap), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Alpha(p.Text, alpha), TextFlags(TextFormatFlags.Left));
        DrawFocusRing(g, track, track.Height / 2f);
    }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}

/// <summary>Windows 11 슬라이더. 얇은 트랙, 강조색 채움, 흰 원 안에 강조색 점이 있는 손잡이.</summary>
sealed class AccentSlider : ThemedControl
{
    const int ThumbR = 10, TrackH = 4;
    int _min, _max = 100, _value, _small = 1, _large = 10;
    bool _drag;

    public event EventHandler? ValueChanged;

    public int Minimum { get => _min; set { _min = value; Value = _value; } }
    public int Maximum { get => _max; set { _max = value; Value = _value; } }
    public int SmallChange { get => _small; set => _small = Math.Max(1, value); }
    public int LargeChange { get => _large; set => _large = Math.Max(1, value); }

    /// <summary>값. SmallChange 단위로 맞추고 범위 안으로 자른다.</summary>
    public int Value
    {
        get => _value;
        set
        {
            int v = (int)Math.Round((value - _min) / (double)_small) * _small + _min;
            v = Math.Clamp(v, _min, _max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public AccentSlider() { Size = new Size(170, 28); Cursor = Cursors.Hand; }

    float Ratio => _max > _min ? (_value - _min) / (float)(_max - _min) : 0f;
    float TrackLeft => Px(ThumbR) + 1;
    float TrackRight => Width - Px(ThumbR) - 1;

    int ValueAt(int x)
    {
        float r = Math.Clamp((x - TrackLeft) / Math.Max(1f, TrackRight - TrackLeft), 0f, 1f);
        return _min + (int)Math.Round(r * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _drag = true; Capture = true;
        Value = ValueAt(e.X);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag) Value = ValueAt(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Capture = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value += e.Delta > 0 ? _small : -_small; }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value -= _small; e.Handled = true; break;
            case Keys.Right or Keys.Up: Value += _small; e.Handled = true; break;
            case Keys.PageDown: Value -= _large; e.Handled = true; break;
            case Keys.PageUp: Value += _large; e.Handled = true; break;
            case Keys.Home: Value = _min; e.Handled = true; break;
            case Keys.End: Value = _max; e.Handled = true; break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        int alpha = Enabled ? 255 : 90;
        float cy = Height / 2f;
        float x = TrackLeft + Ratio * (TrackRight - TrackLeft);

        var track = new RectangleF(TrackLeft, cy - Px(TrackH) / 2f, TrackRight - TrackLeft, Px(TrackH));
        using (var path = Rounded(track, track.Height / 2f))
        using (var rest = new SolidBrush(Alpha(p.Track, alpha)))
            g.FillPath(rest, path);
        var filled = new RectangleF(track.Left, track.Top, Math.Max(track.Height, x - track.Left), track.Height);
        using (var path = Rounded(filled, track.Height / 2f))
        using (var fill = new SolidBrush(Alpha(p.Accent, alpha)))
            g.FillPath(fill, path);

        // 손잡이: 카드색 원 + 테두리 + 강조색 점(올리면 커지고 누르면 작아진다)
        float r = Px(ThumbR);
        using (var outer = new SolidBrush(Alpha(p.Card, alpha)))
        using (var ring = new Pen(Alpha(p.Border, alpha), Px(1)))
        {
            g.FillEllipse(outer, x - r, cy - r, r * 2, r * 2);
            g.DrawEllipse(ring, x - r, cy - r, r * 2, r * 2);
        }
        float dot = Px(Pressed || _drag ? 5 : Hot ? 7 : 6);
        using (var inner = new SolidBrush(Alpha(p.Accent, alpha)))
            g.FillEllipse(inner, x - dot, cy - dot, dot * 2, dot * 2);
        DrawFocusRing(g, new RectangleF(x - r, cy - r, r * 2, r * 2), r);
    }
}

/// <summary>Windows 11 버튼. <see cref="Primary"/> 면 강조색 채움, 아니면 표면색 + 테두리. 확인/취소 기본 버튼(IButtonControl)으로 쓸 수 있다.</summary>
sealed class AccentButton : ThemedControl, IButtonControl
{
    bool _default;
    public bool Primary { get; set; }
    public DialogResult DialogResult { get; set; } = DialogResult.None;

    public AccentButton() { Cursor = Cursors.Hand; Size = new Size(88, 32); }

    public void NotifyDefault(bool value) { _default = value; Invalidate(); }
    public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

    public override Size GetPreferredSize(Size proposed)
    {
        var ts = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(Math.Max(Px(88), ts.Width + Px(32)), Px(32));
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); }

    protected override void OnClick(EventArgs e)
    {
        if (DialogResult != DialogResult.None && FindForm() is { } form) form.DialogResult = DialogResult;
        base.OnClick(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { PerformClick(); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override bool ProcessMnemonic(char charCode)
    {
        if (!Enabled || !Visible || !IsMnemonic(charCode, Text)) return false;
        PerformClick(); return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        int alpha = Enabled ? 255 : 90;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = Rounded(r, Px(Palette.Radius / 2));

        Color fill, text, border;
        if (Primary)
        {
            fill = Pressed ? p.AccentPressed : Hot ? p.AccentHover : p.Accent;
            text = p.OnAccent;
            border = Alpha(Color.Black, p.Dark ? 0 : 30);
        }
        else
        {
            fill = Pressed ? p.Hover : Hot ? p.Hover : p.Elevated;
            text = p.Text;
            border = _default ? p.Accent : p.Border;
        }
        using (var b = new SolidBrush(Alpha(fill, alpha))) g.FillPath(b, path);
        using (var pen = new Pen(Alpha(border, alpha), Px(1))) g.DrawPath(pen, path);
        // 아래쪽 가장자리를 한 톤 어둡게: 살짝 떠 있는 느낌(Windows 11 의 elevation border)
        if (!Primary && Enabled && !Pressed)
            using (var bottom = new Pen(Alpha(p.Dark ? Color.Black : Color.Black, p.Dark ? 60 : 24), Px(1)))
                g.DrawLine(bottom, r.Left + Px(4), r.Bottom, r.Right - Px(4), r.Bottom);

        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), Alpha(text, alpha), TextFlags(TextFormatFlags.HorizontalCenter));
        DrawFocusRing(g, r, Px(Palette.Radius / 2));
    }
}

/// <summary>
/// 그림으로 고르는 타일 선택기. 각 타일은 값·이름·그리기 함수를 가지며, 배지 모양이나 위치처럼 "말보다 그림이 빠른" 선택에 쓴다.
/// 선택된 타일은 강조색 테두리, 마우스를 올리면 살짝 밝아진다. 키보드 좌우로 옮길 수 있다.
/// </summary>
sealed class TilePicker : ThemedControl
{
    public sealed record Tile(object Value, string Label, Action<Graphics, RectangleF, Theme.Palette> Draw);

    readonly List<Tile> _tiles = new();
    int _selected = -1, _hot = -1;
    public Size TileSize { get; set; } = new(64, 58);
    public int Gap { get; set; } = 8;
    const int LabelH = 18;

    public event EventHandler? SelectedChanged;

    public object? SelectedValue
    {
        get => _selected >= 0 && _selected < _tiles.Count ? _tiles[_selected].Value : null;
        set
        {
            int i = _tiles.FindIndex(t => Equals(t.Value, value));
            if (i == _selected) return;
            _selected = i;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // 크기는 레이아웃 엔진이 GetPreferredSize 로 정한다(AutoSize). 폼의 DPI 자동 배율이 지나간 뒤에도 다시 물어보므로
    // 여기서 Px() 로 계산한 물리 픽셀 크기가 이중으로 곱해지지 않는다.
    public TilePicker() { AutoSize = true; }

    public void SetTiles(IEnumerable<Tile> tiles)
    {
        _tiles.Clear();
        _tiles.AddRange(tiles);
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposed) =>
        new(_tiles.Count * Px(TileSize.Width) + Math.Max(0, _tiles.Count - 1) * Px(Gap) + Px(4), Px(TileSize.Height) + Px(4));

    RectangleF TileRect(int i) => new(Px(2) + i * (Px(TileSize.Width) + Px(Gap)), Px(2), Px(TileSize.Width), Px(TileSize.Height));

    int HitTest(Point pt)
    {
        for (int i = 0; i < _tiles.Count; i++) if (TileRect(i).Contains(pt)) return i;
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
        if (h >= 0) SelectedValue = _tiles[h].Value;
    }
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_tiles.Count == 0) return;
        int i = _selected;
        switch (e.KeyCode)
        {
            case Keys.Left: i = Math.Max(0, i - 1); break;
            case Keys.Right: i = Math.Min(_tiles.Count - 1, i + 1); break;
            case Keys.Home: i = 0; break;
            case Keys.End: i = _tiles.Count - 1; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
        SelectedValue = _tiles[i].Value;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        using var labelFont = new Font(Font.FontFamily, Font.Size * 0.9f);
        for (int i = 0; i < _tiles.Count; i++)
        {
            var r = TileRect(i);
            bool sel = i == _selected, hot = i == _hot;
            using var path = Rounded(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Px(Palette.Radius - 2));
            using (var fill = new SolidBrush(hot && !sel ? p.Hover : p.Input)) g.FillPath(fill, path);
            using (var pen = new Pen(sel ? p.Accent : p.Border, Px(sel ? 2 : 1))) g.DrawPath(pen, path);

            var art = new RectangleF(r.X + Px(6), r.Y + Px(6), r.Width - Px(12), r.Height - Px(12) - Px(LabelH));
            var state = g.Save();
            g.SetClip(art);
            _tiles[i].Draw(g, art, p);
            g.Restore(state);

            var labelRect = new Rectangle((int)r.X, (int)(r.Bottom - Px(LabelH) - Px(4)), (int)r.Width, Px(LabelH));
            TextRenderer.DrawText(g, _tiles[i].Label, labelFont, labelRect, sel ? p.Text : p.SubtleText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (sel) DrawFocusRing(g, r, Px(Palette.Radius - 2));
        }
    }
}

/// <summary>
/// 색 견본 팔레트. 고른 디자인 테마의 견본 11개(클래식은 Windows 강조색 계열)와 "사용자 지정"(색 대화상자) 하나.
/// 선택된 견본은 글자색 고리로 표시한다. 선택된 색이 견본에 없으면 사용자 지정 견본이 그 색으로 칠해진다.
/// </summary>
sealed class ColorSwatches : ThemedControl
{
    string[] _presets = DesignThemes.Classic.Swatches.ToArray();
    const int Swatch = 22, Gap = 6;
    string _hex = DesignThemes.Classic.Swatches[0];

    /// <summary>견본 색 목록. 테마를 바꾸면 그 테마의 견본으로 갈아 끼운다. 개수가 바뀌면 크기도 다시 잡는다.</summary>
    public IReadOnlyList<string> Presets
    {
        get => _presets;
        set
        {
            if (value.Count == 0 || value.SequenceEqual(_presets)) return;
            _presets = value.ToArray();
            Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }
    }
    int _hot = -1;

    public event EventHandler? ColorChanged;

    public string Hex
    {
        get => _hex;
        set
        {
            string v = ColorHex.TryParse(value, out int argb) ? ColorHex.ToHex(argb) : _presets[0];
            if (string.Equals(v, _hex, StringComparison.OrdinalIgnoreCase)) return;
            _hex = v;
            Invalidate();
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ColorSwatches() { Cursor = Cursors.Hand; AutoSize = true; Size = GetPreferredSize(Size.Empty); }   // 크기는 레이아웃이 GetPreferredSize 로 다시 정한다

    int Count => _presets.Length + 1;
    public override Size GetPreferredSize(Size proposed) => new(Count * Px(Swatch) + (Count - 1) * Px(Gap) + Px(6), Px(Swatch) + Px(6));

    RectangleF Rect(int i) => new(Px(3) + i * (Px(Swatch) + Px(Gap)), Px(3), Px(Swatch), Px(Swatch));
    int PresetIndex => Array.FindIndex(_presets, x => string.Equals(x, _hex, StringComparison.OrdinalIgnoreCase));
    int SelectedIndex => PresetIndex >= 0 ? PresetIndex : _presets.Length;

    int HitTest(Point pt)
    {
        for (int i = 0; i < Count; i++) if (Rect(i).Contains(pt)) return i;
        return -1;
    }

    void Pick(int i)
    {
        if (i < 0) return;
        if (i < _presets.Length) { Hex = _presets[i]; return; }
        using var dlg = new ColorDialog { Color = Color.FromArgb(ColorHex.TryParse(_hex, out int a) ? a : unchecked((int)0xFF000000)), FullOpen = true };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Hex = ColorHex.ToHex(dlg.Color.ToArgb());
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h != _hot) { _hot = h; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { _hot = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) Pick(HitTest(e.Location)); }
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int i = SelectedIndex;
        switch (e.KeyCode)
        {
            case Keys.Left: Pick(Math.Max(0, Math.Min(i, _presets.Length - 1) - 1)); e.Handled = true; break;
            case Keys.Right: Pick(Math.Min(_presets.Length - 1, i + 1)); e.Handled = true; break;
            case Keys.Space or Keys.Enter: Pick(_presets.Length); e.Handled = true; break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Prepare(g);
        g.Clear(BackColor);
        var p = Palette;
        int sel = SelectedIndex;
        for (int i = 0; i < Count; i++)
        {
            var r = Rect(i);
            if (i == _hot && i != sel) r.Inflate(Px(1), Px(1));
            if (i < _presets.Length)
            {
                ColorHex.TryParse(_presets[i], out int argb);
                using var b = new SolidBrush(Color.FromArgb(argb));
                g.FillEllipse(b, r);
            }
            else
            {
                // 사용자 지정: 견본에 없는 색이면 그 색, 아니면 무지개 고리
                if (sel == i && ColorHex.TryParse(_hex, out int custom))
                {
                    using var b = new SolidBrush(Color.FromArgb(custom));
                    g.FillEllipse(b, r);
                }
                else
                {
                    var hues = new[] { Color.FromArgb(0xE7, 0x48, 0x56), Color.FromArgb(0xFF, 0xB9, 0x00), Color.FromArgb(0x10, 0x89, 0x3E),
                                       Color.FromArgb(0x00, 0xB2, 0x94), Color.FromArgb(0x00, 0x67, 0xC0), Color.FromArgb(0x74, 0x4D, 0xA9) };
                    for (int k = 0; k < hues.Length; k++)
                    {
                        using var b = new SolidBrush(hues[k]);
                        g.FillPie(b, r.X, r.Y, r.Width, r.Height, k * 60 - 90, 60);
                    }
                    using var hole = new SolidBrush(BackColor);
                    var inner = r; inner.Inflate(-Px(6), -Px(6));
                    g.FillEllipse(hole, inner);
                }
            }
            using (var edge = new Pen(Alpha(Color.Black, p.Dark ? 60 : 30), Px(1))) g.DrawEllipse(edge, r);
            if (i == sel)
            {
                var ring = r; ring.Inflate(Px(3), Px(3));
                using var pen = new Pen(p.Text, Px(2));
                g.DrawEllipse(pen, ring);
            }
        }
        if (Focused && ShowFocusCues)
        {
            var r = Rect(sel); r.Inflate(Px(5), Px(5));
            using var pen = new Pen(p.Text, Px(1)) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(pen, r);
        }
    }
}
