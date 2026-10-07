using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 커서 옆에 잠깐 띄우는 그림 창(소나·말풍선). 배지 창(<see cref="BadgeForm"/>)과 같은 레이어드 창이라 픽셀마다 투명도가 있고,
/// 클릭은 아래 창으로 통과하며, 떠도 입력 중인 창의 포커스를 가져가지 않고, 작업 표시줄·Alt+Tab 에 나타나지 않는다.
/// </summary>
sealed class OverlayWindow : Form
{
    bool _allowShow;

    public OverlayWindow(string name)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Size = new Size(1, 1);
        Text = name;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            return cp;
        }
    }
    protected override bool ShowWithoutActivation => true;
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(_allowShow && value);
    protected override void OnPaintBackground(PaintEventArgs e) { }

    /// <summary>그림을 화면의 <paramref name="pos"/> 에 창 전체 알파 <paramref name="alpha"/> 로 올린다. 처음 보일 때 최상위 창들 중 맨 위로.</summary>
    public void Present(Bitmap bmp, Point pos, byte alpha)
    {
        bool appearing = !Visible;
        _allowShow = true;
        if (appearing) Show();
        if (Size != bmp.Size) Size = bmp.Size;
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
            var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = Native.AC_SRC_ALPHA };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
            if (hBmp != IntPtr.Zero) Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
        if (appearing)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void Conceal()
    {
        if (Visible) Hide();
    }
}

/// <summary>
/// 커서 소나 재생기(실험 기능). <see cref="Play"/> 하면 <see cref="Sonar.DurationMs"/> 동안 16ms 마다 한 장씩 그려 올리고 사라진다.
/// 애니메이션 효과를 끈 사용자에게는 움직이지 않는 한 장을 같은 시간만큼 보여 준다.
/// </summary>
sealed class SonarPlayer : IDisposable
{
    readonly OverlayWindow _win = new("ImeBadge sonar");
    readonly System.Windows.Forms.Timer _timer = new() { Interval = BadgeForm.FrameMs };
    Rectangle _caret;
    Color _color, _halo;
    float _scale;
    bool _animate, _stillShown;
    long _start;

    public SonarPlayer() => _timer.Tick += (_, _) => Frame();

    /// <param name="caret">커서 사각형(화면 좌표).</param>
    /// <param name="color">원의 색(지금 한/영 배지 색).</param>
    /// <param name="halo">원 둘레 테두리 색. 보통 흰색이라 어두운 화면에서도 원이 보인다.</param>
    public void Play(Rectangle caret, Color color, Color halo, bool animate)
    {
        _caret = caret; _color = color; _halo = halo; _animate = animate; _stillShown = false;
        _scale = Native.DpiScaleAt(caret.Location);
        _start = Environment.TickCount64;
        _timer.Start();
        Frame();
    }

    void Frame()
    {
        long elapsed = Environment.TickCount64 - _start;
        if (elapsed >= Sonar.DurationMs) { Stop(); return; }
        if (!_animate && _stillShown) return;
        try
        {
            using var bmp = OverlayRenderer.Sonar(_animate ? elapsed / (float)Sonar.DurationMs : Sonar.StillT, _caret, _scale, _color, _halo, out var origin);
            _win.Present(bmp, origin, 255);
            _stillShown = true;
        }
        catch (Exception ex) { Stop(); Log.Error("sonar frame failed", ex); }
    }

    public void Stop()
    {
        _timer.Stop();
        _win.Conceal();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _win.Dispose();
    }
}

/// <summary>
/// 커서 옆 말풍선(실험 기능의 안내·경고). 한 번에 하나만 보이고, 새 말풍선이 오면 바꿔 단다. <see cref="Tag"/> 로 누가 띄운 것인지 구별해
/// 띄운 쪽만 내린다(입력칸 기억의 안내를 비밀번호 칸 경고가 덮었으면 기억 쪽에서 내리지 않는다).
/// </summary>
sealed class CalloutPlayer : IDisposable
{
    const int FadeInMs = 120, FadeOutMs = 220;
    readonly OverlayWindow _win = new("ImeBadge callout");
    readonly System.Windows.Forms.Timer _timer = new() { Interval = BadgeForm.FrameMs };
    Bitmap? _bmp;
    Point _pos;
    long _shownAt, _hideAt;
    bool _animate;
    byte _alpha;

    /// <summary>지금 보이는 말풍선을 띄운 쪽. 보이는 것이 없으면 null.</summary>
    public string? Tag { get; private set; }

    public CalloutPlayer() => _timer.Tick += (_, _) => Tick();

    /// <param name="tag">띄운 쪽 이름. <see cref="Hide"/> 에 같은 이름을 주어야 내려간다.</param>
    /// <param name="anchor">가리킬 커서 사각형(화면 좌표).</param>
    /// <param name="preferBelow">자리가 있으면 커서 아래에(배지가 커서 위에 있을 때).</param>
    /// <param name="holdMs">보이는 시간. 0 이하면 <see cref="Hide"/> 할 때까지.</param>
    public void Show(string tag, CalloutContent content, Rectangle anchor, bool preferBelow, int holdMs, bool animate)
    {
        float scale = Native.DpiScaleAt(anchor.Location);
        var body = OverlayRenderer.MeasureCallout(content, scale);
        var area = Screen.FromPoint(anchor.Location).WorkingArea;
        var place = CalloutLayout.Compute(anchor, body, OverlayRenderer.TailHeight(scale), (int)Math.Round(3 * scale), area, preferBelow,
            OverlayRenderer.TailInset(scale));
        var bmp = OverlayRenderer.Callout(content, scale, body, place.Below, place.TailX, out var offset);
        _bmp?.Dispose();
        _bmp = bmp;
        _pos = new Point(place.Location.X - offset.X, place.Location.Y - offset.Y);
        bool already = Tag is not null;
        Tag = tag;
        _animate = animate;
        long now = Environment.TickCount64;
        _shownAt = already ? now - FadeInMs : now;   // 바꿔 달 때는 다시 페이드인하지 않는다
        _hideAt = holdMs > 0 ? now + holdMs : long.MaxValue;
        _alpha = 0;
        _timer.Start();
        Tick();
    }

    /// <summary><paramref name="tag"/> 가 띄운 말풍선이면 내린다(서서히). null 이면 무엇이든.</summary>
    public void Hide(string? tag = null)
    {
        if (Tag is null || (tag is not null && tag != Tag)) return;
        long now = Environment.TickCount64;
        if (!_animate) { Stop(); return; }
        if (_hideAt > now) _hideAt = now;
        _timer.Start();   // 내릴 때까지 그대로였으면 쉬고 있었다
    }

    void Tick()
    {
        if (_bmp is null) { Stop(); return; }
        long now = Environment.TickCount64;
        float a;
        if (now >= _hideAt)
        {
            if (!_animate) { Stop(); return; }
            float p = (now - _hideAt) / (float)FadeOutMs;
            if (p >= 1f) { Stop(); return; }
            a = 1f - p;
        }
        else a = _animate ? Math.Min(1f, (now - _shownAt) / (float)FadeInMs) : 1f;
        byte alpha = (byte)Math.Round(255 * a);
        if (alpha == _alpha && _win.Visible)
        {
            if (_hideAt == long.MaxValue) _timer.Stop();   // 내릴 때까지 그대로: 타이머를 쉰다
            return;
        }
        try
        {
            _win.Present(_bmp, _pos, alpha);
            _alpha = alpha;
        }
        catch (Exception ex) { Stop(); Log.Error("callout frame failed", ex); }
    }

    void Stop()
    {
        _timer.Stop();
        _win.Conceal();
        Tag = null;
        _bmp?.Dispose();
        _bmp = null;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _win.Dispose();
        _bmp?.Dispose();
    }
}
