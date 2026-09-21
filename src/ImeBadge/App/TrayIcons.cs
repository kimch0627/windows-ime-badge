using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ImeBadge;

/// <summary>
/// 상태별 트레이 아이콘을 그려서 캐시한다. Windows 의 한국어 IME 표시기가 트레이에 "한"/"A" 를 띄우는 것과 같은 발상이라,
/// 배지가 뜨지 못하는 앱(터미널, UWP)에서도 트레이만 보면 지금 상태를 알 수 있다.
/// <list type="bullet">
/// <item>입력 중(한글/영문/다른 언어): 배지와 같은 색의 둥근 사각형에 글자</item>
/// <item>입력 위치 없음: 기본 앱 아이콘(커서 + 배지)</item>
/// <item>일시 중지: 회색 앱 아이콘 오른쪽 아래에 일시 정지 표시(‖). 색만으로 구분하지 않도록 모양을 더한다</item>
/// </list>
/// GDI+ 비트맵에서 만든 HICON 은 Icon 이 소유하지 않으므로 여기서 <see cref="Native.DestroyIcon"/> 로 직접 해제한다.
/// </summary>
sealed class TrayIcons : IDisposable
{
    readonly Dictionary<(ImeState state, bool paused, int size, int hangul, int english), (Icon icon, IntPtr handle)> _cache = new();

    /// <summary>
    /// 상태에 맞는 아이콘. <paramref name="size"/> 는 트레이가 쓰는 픽셀 크기(SmallIconSize: 100% 에서 16, 150% 에서 24).
    /// 돌려준 아이콘은 이 객체가 소유한다. 호출자가 Dispose 하면 안 된다.
    /// </summary>
    public Icon Get(ImeState state, bool paused, int size, in BadgeTheme theme)
    {
        if (!paused && state == ImeState.Unknown) return Icons.App;

        var key = (state, paused, size, theme.Hangul.ToArgb(), theme.English.ToArgb());
        if (_cache.TryGetValue(key, out var hit)) return hit.icon;

        using var bmp = paused ? RenderPaused(size) : RenderState(state, size, theme);
        IntPtr h = bmp.GetHicon();
        var icon = Icon.FromHandle(h);
        _cache[key] = (icon, h);
        return icon;
    }

    /// <summary>캐시를 비운다. 색 설정이나 DPI 가 바뀌었을 때. 비우기 전에 트레이가 다른 아이콘을 가리키게 해야 한다.</summary>
    public void Clear()
    {
        foreach (var (icon, handle) in _cache.Values)
        {
            icon.Dispose();
            Native.DestroyIcon(handle);
        }
        _cache.Clear();
    }

    public void Dispose() => Clear();

    static Bitmap NewCanvas(int size, out Graphics g)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        return bmp;
    }

    /// <summary>배지와 같은 색의 둥근 사각형에 "한"/"A"/"?". 글자색은 배지와 같은 규칙으로 고른다.</summary>
    static Bitmap RenderState(ImeState state, int size, in BadgeTheme theme)
    {
        var (text, color) = BadgeRenderer.Look(state, theme);
        var bmp = NewCanvas(size, out var g);
        using (g)
        {
            var rect = new RectangleF(0.5f, 0.5f, size - 1, size - 1);
            using var path = RoundedRect(rect, size * 0.22f);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);

            // 글자는 상자의 약 70%. "한"은 획이 많아 "A"보다 조금 작게 그려야 16px 에서 뭉개지지 않는다.
            float px = size * (text == "A" ? 0.78f : 0.68f);
            using var font = new Font(BadgeRenderer.FontFamily, px, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(BadgeRenderer.TextColorOn(color));
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, new RectangleF(0, 0, size, size), sf);
        }
        return bmp;
    }

    /// <summary>회색 앱 아이콘 + 오른쪽 아래 일시 정지 표시.</summary>
    static Bitmap RenderPaused(int size)
    {
        var bmp = NewCanvas(size, out var g);
        using (g)
        {
            using (var baseIcon = new Icon(Icons.Paused, size, size))
            using (var baseBmp = baseIcon.ToBitmap())
                g.DrawImage(baseBmp, new Rectangle(0, 0, size, size), new Rectangle(0, 0, baseBmp.Width, baseBmp.Height), GraphicsUnit.Pixel);

            // 지름은 아이콘의 60%. 진한 원 위에 밝은 막대 둘.
            float d = size * 0.6f;
            var circle = new RectangleF(size - d, size - d, d, d);
            using var dark = new SolidBrush(Color.FromArgb(0x2B, 0x2B, 0x2B));
            using var ring = new Pen(Color.White, Math.Max(1f, size / 16f));
            g.FillEllipse(dark, circle);
            g.DrawEllipse(ring, circle);

            float barW = Math.Max(1.5f, d * 0.16f), barH = d * 0.46f, gap = d * 0.14f;
            float cx = circle.Left + d / 2, cy = circle.Top + d / 2;
            g.FillRectangle(Brushes.White, cx - gap / 2 - barW, cy - barH / 2, barW, barH);
            g.FillRectangle(Brushes.White, cx + gap / 2, cy - barH / 2, barW, barH);
        }
        return bmp;
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
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
}
