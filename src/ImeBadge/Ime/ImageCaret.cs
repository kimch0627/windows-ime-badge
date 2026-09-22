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
/// <para>PrintWindow(PW_RENDERFULLCONTENT)로 대상 창만 그리므로, 그 위에 겹친 우리 배지나 다른 창이 캡처에 섞이지 않는다
/// (섞이면 배지 움직임이 다시 차이로 잡혀 서로를 쫓는 되먹임이 생긴다). 커서 깜빡임 한 주기(약 0.5초)를 잡으려면
/// 여러 번 캡처해야 하므로, 한 번 찾은 위치는 잠깐 동안 재사용한다. 화면 출력이 많으면 차이가 넓어 못 찾고 null 을 돌려준다
/// (그러면 부르는 쪽이 모서리 고정으로 되돌아간다).</para>
/// </summary>
static class ImageCaret
{
    const uint PW_RENDERFULLCONTENT = 0x2;
    const int MinIntervalMs = 120;   // 캡처 간격(너무 자주 하면 CPU)
    const int HoldMs = 1500;         // 마지막으로 찾은 커서를 이만큼까지 재사용
    const int DiffThreshold = 90;    // 한 픽셀이 "바뀌었다"고 볼 채널 합 차이(0~765)

    static IntPtr _hwnd;
    static int _w, _h, _stride;
    static byte[]? _prev;
    static long _lastCapture, _lastFound;
    static Rectangle _last;

    /// <summary>
    /// 대상 창의 커서 사각형(화면 좌표). 못 찾으면 최근 값이 신선한 동안 그걸 쓰고, 그마저 없으면 null.
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

        byte[]? cur = Capture(hwnd, w, h, out int stride);
        if (cur is null) return Hold(dump);

        Rectangle? found = null;
        if (_prev is not null && _prev.Length == cur.Length && _stride == stride)
            found = Diff(_prev, cur, w, h, stride, dpi);
        _prev = cur; _stride = stride;

        if (found is { } r)
        {
            var tl = new Native.POINT(rc.Left + r.Left, rc.Top + r.Top);
            Native.ClientToScreen(hwnd, ref tl);
            _last = new Rectangle(tl.X, tl.Y, r.Width, r.Height);
            _lastFound = now;
            dump?.Append($" caret:img({tl.X},{tl.Y},{r.Width}x{r.Height})");
            return _last;
        }
        return Hold(dump);
    }

    static Rectangle? Hold(StringBuilder? dump)
    {
        if (_lastFound != 0 && Environment.TickCount64 - _lastFound < HoldMs) { dump?.Append(" caret:img-hold"); return _last; }
        return null;
    }

    static void Reset(IntPtr hwnd, int w, int h)
    {
        _hwnd = hwnd; _w = w; _h = h; _prev = null; _last = Rectangle.Empty; _lastFound = 0;
    }

    /// <summary>프로그램 종료 시 캐시를 비운다.</summary>
    public static void Clear() => Reset(IntPtr.Zero, 0, 0);

    /// <summary>대상 창을 32bpp 픽셀 배열로 캡처한다. 위에 겹친 창은 포함되지 않는다(PrintWindow 가 대상만 그린다).</summary>
    static byte[]? Capture(IntPtr hwnd, int w, int h, out int stride)
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
        catch (Exception ex) { Log.WriteIfChanged($"image caret capture failed: {ex.GetType().Name}"); return null; }
        finally
        {
            if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
            if (hbmp != IntPtr.Zero) Native.DeleteObject(hbmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>두 프레임에서 밝기가 많이 바뀐 픽셀의 경계 상자와 개수를 모아 <see cref="CursorBlink"/> 로 판정한다. 2픽셀 간격으로 표본 스캔.</summary>
    static Rectangle? Diff(byte[] a, byte[] b, int w, int h, int stride, float dpi)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
        for (int y = 0; y < h; y += 2)
        {
            int row = y * stride;
            for (int x = 0; x < w; x += 2)
            {
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
        // 2픽셀 간격이라 표본 수를 4배로 환산해 실제 픽셀 수에 맞춘다. 커서 한도는 대략 글자 세 칸 / 한 줄.
        return CursorBlink.Evaluate(minX, minY, maxX, maxY, count * 4,
            maxCellW: (int)Math.Round(42 * dpi), maxCellH: (int)Math.Round(46 * dpi), minCount: 6);
    }
}
