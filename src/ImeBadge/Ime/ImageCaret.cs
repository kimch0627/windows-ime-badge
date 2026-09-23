using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace ImeBadge;

/// <summary>
/// 이미지 기반 커서 추적(opt-in). Win32 caret 도 UI Automation 도 없는 앱(Xshell 등)에서, 대상 창을 짧은 간격으로
/// 두 번 캡처해 그 차이로 깜빡이는 텍스트 커서 위치를 찾는다. 판정 규칙은 순수 로직(<see cref="CursorBlink"/>)에 있다.
///
/// <para>캡처는 두 단계다. 먼저 PrintWindow(PW_RENDERFULLCONTENT)로 대상 창만 그린다(겹친 창이 섞이지 않는다).
/// 그런데 GPU 로 그리는 자식 뷰(터미널 등)는 PrintWindow 가 아무것도 안 그린 빈 화면을 주기도 한다. 그러면 화면 캡처로
/// 전환하고, 그때는 우리 배지가 캡처에 섞여 배지 움직임을 다시 커서로 오인하는 되먹임이 생기므로 배지 사각형
/// (<see cref="Ignore"/>)을 비교에서 뺀다.</para>
///
/// <para>화면 캡처에서의 되먹임 방지. 배지를 옮긴 직후에는 화면 합성(DWM)이 늦어 캡처에 배지가 옛 자리에 남아 있을 수 있다.
/// 그러면 다음 프레임에서 "옛 자리에서 배지가 사라진 흔적"이 커서처럼 보이고, 배지가 그리로 옮겨 가 또 흔적을 남기며 깜빡인다.
/// 그래서 (1) 배지가 움직인 직후 프레임은 비교하지 않고 새 기준으로만 삼고, (2) 최근 몇 프레임의 배지 자리를 모두 비교에서 빼며,
/// (3) 그래도 찾은 위치가 최근 배지 자리와 겹치면 커서로 인정하지 않는다(<see cref="CursorBlink.OverlapsAny"/>).</para>
///
/// <para>마우스 버튼이 눌려 있는 동안(드래그·선택)은 캡처하지 않는다. PrintWindow 는 대상 창을 강제로 다시 그리게 해
/// 진행 중인 드래그를 방해할 수 있고, 드래그 중의 화면 변화는 어차피 커서가 아니다.</para>
///
/// <para>커서는 "변할 때"(깜빡임·타이핑)만 보이므로, 한 번 찾은 위치는 같은 창·같은 크기인 동안 계속 기억해 돌려준다.
/// 커서가 깜빡이지 않는 설정이면 타이핑으로 움직일 때만 갱신된다. 스크롤·긴 출력처럼 화면이 크게 바뀌면
/// (<see cref="CursorBlink.IsWide"/>) 옛 위치를 믿을 수 없으니 기억을 버리고 null 을 돌려준다(그러면 부르는 쪽이 모서리
/// 고정으로 되돌아가고, 커서가 다시 깜빡이면 새 자리를 찾는다).</para>
/// </summary>
static class ImageCaret
{
    const uint PW_RENDERFULLCONTENT = 0x2;
    const int MinIntervalMs = 120;   // 캡처 간격(너무 자주 하면 CPU)
    const int DiffThreshold = 90;    // 한 픽셀이 "바뀌었다"고 볼 채널 합 차이(0~765)
    const int BadgeMargin = 4;       // 배지 사각형을 그림자·가장자리까지 덮도록 넓히는 폭(px)
    const int RecentBadges = 3;      // 비교에서 뺄 최근 배지 자리 수(지금 자리 제외). DWM 합성이 한두 프레임 늦는 것을 덮는다

    /// <summary>우리 배지 창의 화면 사각형. 화면 캡처 비교에서 제외한다(BadgeForm 이 배지를 놓을 때마다 갱신).</summary>
    public static Rectangle Ignore;

    static IntPtr _hwnd;
    static int _w, _h, _stride;
    static byte[]? _prev;
    static long _lastCapture;
    static Rectangle _last;          // 마지막으로 찾은 커서(클라이언트 좌표: 창이 움직여도 따라간다). 비어 있으면 아직 모름
    // 최근 프레임들을 찍을 때의 배지 자리(화면 좌표, [0] 이 직전). 배지가 옮겨 가면 옛 자리도 "변화"로 잡히므로 함께 뺀다
    static readonly Rectangle[] _recent = new Rectangle[RecentBadges];
    static bool _useScreen;          // PrintWindow 가 빈 화면을 준 창 → 화면 캡처로 전환

    /// <summary>
    /// 대상 창의 커서 사각형(화면 좌표). 이번에 못 찾으면 기억해 둔 마지막 위치를 쓰고, 그마저 없거나 화면이 크게 바뀌어
    /// 기억을 버렸으면 null.
    /// </summary>
    public static Rectangle? Find(IntPtr hwnd, float dpi, StringBuilder? dump)
    {
        if (hwnd == IntPtr.Zero || !Native.GetClientRect(hwnd, out var rc)) return Hold(dump);
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (w < 8 || h < 8 || w > 8000 || h > 8000) return Hold(dump);

        long now = Environment.TickCount64;
        if (hwnd != _hwnd || w != _w || h != _h) Reset(hwnd, w, h);   // 창·크기가 바뀌면 기준 프레임부터 새로
        if (now - _lastCapture < MinIntervalMs) return Hold(dump);
        _lastCapture = now;

        // 드래그·선택 중: 캡처하지 않고, 드래그 전 프레임과 비교하지 않도록 기준 프레임도 버린다.
        if (Native.IsMouseButtonDown()) { _prev = null; dump?.Append(" img:mouse"); return Hold(dump); }

        var origin = new Native.POINT(0, 0);
        Native.ClientToScreen(hwnd, ref origin);

        bool wasScreen = _useScreen;
        byte[]? cur = Capture(hwnd, origin, w, h, out int stride);
        if (cur is null) { dump?.Append(" img:capture-fail"); return Hold(dump); }

        Rectangle? found = null;
        string why = "first";
        bool wide = false;
        // 화면 캡처인데 지난 캡처 뒤로 배지가 움직였으면(나타남·이동), 또는 이번에 화면 캡처로 막 전환했으면(이전 프레임은
        // PrintWindow 의 빈 화면) 이번 프레임은 비교하지 않고 새 기준으로만 삼는다.
        bool settle = _useScreen && (!wasScreen || Ignore != _recent[0]);
        if (settle) why = "settle";
        else if (_prev is not null && _prev.Length == cur.Length && _stride == stride)
        {
            // 화면 캡처일 때만 우리 배지가 섞이므로 지금 자리와 최근 프레임들의 자리를 모두 뺀다(클라이언트 좌표로 옮겨서).
            var skip = _useScreen ? RecentBadgeRects(origin) : Array.Empty<Rectangle>();
            found = Diff(_prev, cur, w, h, stride, dpi, skip, out why, out wide);
        }
        var badges = _useScreen ? BadgeHistory() : Array.Empty<Rectangle>();   // 기록을 밀기 전에 지금·최근 배지 자리를 챙긴다
        _prev = cur; _stride = stride;
        Array.Copy(_recent, 0, _recent, 1, RecentBadges - 1);
        _recent[0] = Ignore;

        // 찾은 자리가 우리 배지의 지금·최근 자리와 겹치면 배지가 남긴 흔적이다(되먹임). 커서로 인정하지 않는다.
        if (found is { } f && badges.Length > 0 && CursorBlink.OverlapsAny(ToScreen(f, origin), badges, BadgeMargin))
        {
            found = null;
            why = "self";
        }

        if (found is { } r)
        {
            _last = r;
            var screen = ToScreen(r, origin);
            dump?.Append($" caret:img({screen.X},{screen.Y},{r.Width}x{r.Height}{(_useScreen ? ",scr" : "")})");
            return screen;
        }
        if (wide) _last = Rectangle.Empty;   // 스크롤·긴 출력: 옛 위치는 못 믿는다 → 모서리로, 커서가 다시 보이면 새로
        dump?.Append($" img:{why}{(_useScreen ? ",scr" : "")}");
        return Hold(dump);
    }

    /// <summary>화면 좌표의 배지 사각형을 캡처(클라이언트) 좌표로 옮기고, 그림자·가장자리까지 덮도록 조금 넓힌다.</summary>
    static Rectangle ToClient(Rectangle r, Native.POINT origin) =>
        r.IsEmpty ? Rectangle.Empty
                  : Rectangle.Inflate(new Rectangle(r.X - origin.X, r.Y - origin.Y, r.Width, r.Height), BadgeMargin, BadgeMargin);

    /// <summary>지금 배지 자리와 최근 프레임들의 배지 자리(화면 좌표, 빈 것 포함).</summary>
    static Rectangle[] BadgeHistory()
    {
        var all = new Rectangle[RecentBadges + 1];
        all[0] = Ignore;
        Array.Copy(_recent, 0, all, 1, RecentBadges);
        return all;
    }

    /// <summary>비교에서 뺄 배지 사각형들(클라이언트 좌표, 빈 것은 뺀다).</summary>
    static Rectangle[] RecentBadgeRects(Native.POINT origin) =>
        Array.FindAll(Array.ConvertAll(BadgeHistory(), r => ToClient(r, origin)), r => !r.IsEmpty);

    static Rectangle ToScreen(Rectangle client, Native.POINT origin) =>
        new(origin.X + client.X, origin.Y + client.Y, client.Width, client.Height);

    /// <summary>
    /// 기억해 둔 마지막 커서 위치(화면 좌표로 바꿔서). 같은 창·같은 크기인 동안 유효하며, 화면이 크게 바뀌면 비워진다.
    /// 창이 옮겨졌을 수 있으니 매번 지금 창 원점으로 환산한다.
    /// </summary>
    static Rectangle? Hold(StringBuilder? dump)
    {
        if (_last.IsEmpty || _hwnd == IntPtr.Zero) return null;
        var origin = new Native.POINT(0, 0);
        if (!Native.ClientToScreen(_hwnd, ref origin)) return null;
        dump?.Append(" caret:img-hold");
        return ToScreen(_last, origin);
    }

    static void Reset(IntPtr hwnd, int w, int h)
    {
        _hwnd = hwnd; _w = w; _h = h; _prev = null; _last = Rectangle.Empty; _useScreen = false;
        Array.Clear(_recent);
    }

    /// <summary>프로그램 종료 시 캐시를 비운다.</summary>
    public static void Clear() => Reset(IntPtr.Zero, 0, 0);

    /// <summary>대상 창을 32bpp 픽셀 배열로 캡처한다. PrintWindow 가 빈 화면을 주면 그 창은 화면 캡처로 전환한다.</summary>
    static byte[]? Capture(IntPtr hwnd, Native.POINT origin, int w, int h, out int stride)
    {
        stride = 0;
        if (!_useScreen)
        {
            var frame = CapturePrintWindow(hwnd, w, h, out stride);
            if (frame is not null && !IsUniform(frame, w, h, stride)) return frame;
            _useScreen = true;   // 빈 화면(전부 같은 색) → 이 창은 PrintWindow 로 못 그린다
            Log.WriteIfChanged($"image caret: PrintWindow gave a blank frame for '{Native.ClassName(hwnd)}'; switching to screen capture");
        }
        return CaptureScreen(origin, w, h, out stride);
    }

    static byte[]? CapturePrintWindow(IntPtr hwnd, int w, int h, out int stride)
    {
        stride = 0;
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hbmp = Native.CreateCompatibleBitmap(screenDc, w, h);
        IntPtr old = hbmp != IntPtr.Zero ? Native.SelectObject(memDc, hbmp) : IntPtr.Zero;
        try
        {
            if (hbmp == IntPtr.Zero || !Native.PrintWindow(hwnd, memDc, PW_RENDERFULLCONTENT)) return null;
            using var image = Image.FromHbitmap(hbmp);
            return Pixels(image, w, h, out stride);
        }
        catch (Exception ex) { Log.WriteIfChanged($"image caret PrintWindow failed: {ex.GetType().Name}"); return null; }
        finally
        {
            if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
            if (hbmp != IntPtr.Zero) Native.DeleteObject(hbmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>화면에서 그 창의 클라이언트 영역을 그대로 복사한다. 위에 겹친 창(우리 배지 포함)이 섞인다.</summary>
    static byte[]? CaptureScreen(Native.POINT origin, int w, int h, out int stride)
    {
        stride = 0;
        try
        {
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            return Pixels(bmp, w, h, out stride);
        }
        catch (Exception ex) { Log.WriteIfChanged($"image caret screen capture failed: {ex.GetType().Name}"); return null; }
    }

    static byte[] Pixels(Bitmap image, int w, int h, out int stride)
    {
        var data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            stride = data.Stride;
            var buf = new byte[stride * h];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            return buf;
        }
        finally { image.UnlockBits(data); }
    }

    /// <summary>프레임이 한 가지 색뿐인가(PrintWindow 가 아무것도 안 그린 경우). 성긴 표본으로 판단한다.</summary>
    static bool IsUniform(byte[] f, int w, int h, int stride)
    {
        int b = f[0], g = f[1], r = f[2];
        for (int y = 0; y < h; y += 8)
        {
            int row = y * stride;
            for (int x = 0; x < w; x += 8)
            {
                int i = row + x * 4;
                if (f[i] != b || f[i + 1] != g || f[i + 2] != r) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 두 프레임에서 밝기가 많이 바뀐 픽셀의 경계 상자와 개수를 모아 <see cref="CursorBlink"/> 로 판정한다. 2픽셀 간격으로 표본 스캔.
    /// <paramref name="skip"/> 안의 픽셀(우리 배지의 지금·최근 자리)은 세지 않는다.
    /// <paramref name="why"/> 는 못 찾은 이유(로그용), <paramref name="wide"/> 는 화면이 크게 바뀌어 옛 커서 위치를 버려야 하는지.
    /// </summary>
    static Rectangle? Diff(byte[] a, byte[] b, int w, int h, int stride, float dpi, Rectangle[] skip, out string why, out bool wide)
    {
        wide = false;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
        for (int y = 0; y < h; y += 2)
        {
            int row = y * stride;
            for (int x = 0; x < w; x += 2)
            {
                if (skip.Length > 0 && InAny(skip, x, y)) continue;
                int i = row + x * 4;
                int d = Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
                if (d < DiffThreshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                count++;
            }
        }
        if (count == 0) { why = "nodiff"; return null; }
        // 2픽셀 간격이라 표본 수를 4배로 환산해 실제 픽셀 수에 맞춘다. 커서 한도는 대략 글자 세 칸 / 한 줄.
        int cellW = (int)Math.Round(42 * dpi), cellH = (int)Math.Round(46 * dpi);
        var r = CursorBlink.Evaluate(minX, minY, maxX, maxY, count * 4, cellW, cellH, minCount: 6);
        if (r is not null) { why = "ok"; return r; }
        wide = CursorBlink.IsWide(minX, minY, maxX, maxY, count * 4, cellW, cellH);
        why = $"{(wide ? "wide" : "reject")}({maxX - minX + 1}x{maxY - minY + 1})";
        return null;
    }

    static bool InAny(Rectangle[] rects, int x, int y)
    {
        foreach (var r in rects)
            if (r.Contains(x, y)) return true;
        return false;
    }
}
