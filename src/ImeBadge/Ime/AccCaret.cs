using System;
using System.Drawing;
using System.Text;

namespace ImeBadge;

/// <summary>
/// MSAA 의 가상 caret(OBJID_CARET)으로 커서 위치 찾기. 크롬·엣지 주소창처럼 Win32 caret 이 없고 UI Automation 도
/// 입력칸 사각형만 주는(커서 위치는 모르는) 경우에 쓴다. Chromium 은 화면 돋보기가 커서를 따라가도록 포커스 창에
/// 이 객체를 내보내고, 커서가 움직일 때마다 EVENT_OBJECT_LOCATIONCHANGE(OBJID_CARET)를 보낸다(BadgeForm 의 훅이 받는다).
///
/// 비유하면 Win32 caret 은 "창에 붙은 공식 명패", 이 가상 caret 은 "돋보기 사용자를 위해 따로 세운 안내판"이다.
/// 명패가 없는 앱도 안내판은 세워 두는 경우가 있다.
/// </summary>
static class AccCaret
{
    /// <summary>
    /// 창 <paramref name="hwnd"/> 의 가상 caret 사각형(화면 좌표). 없거나, 숨겨졌거나, 창 밖(옛 위치가 남은 것)이면 null.
    /// </summary>
    public static Rectangle? Find(IntPtr hwnd, StringBuilder? dump)
    {
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var wr)) return null;
        // AccessibleObjectFromWindow 는 대상 창에 WM_GETOBJECT 를 보내 답을 기다린다. 응답 없는 창에 묻지 않는다.
        if (Native.IsHungAppWindow(hwnd)) { dump?.Append(" acc:hung"); return null; }

        Acc.IAccessible? acc = null;
        try
        {
            acc = Acc.FromWindow(hwnd, Native.OBJID_CARET);
            if (acc is null) { dump?.Append(" acc:none"); return null; }

            var self = Acc.Variant.I4(Acc.CHILDID_SELF);
            if (acc.get_accState(self, out var state) >= 0 && state.AsI4 is int s && (s & Acc.STATE_SYSTEM_INVISIBLE) != 0)
            {
                dump?.Append(" acc:invisible");
                return null;
            }
            int hr = acc.accLocation(out int x, out int y, out int w, out int h, self);
            if (hr < 0 || h <= 0) { dump?.Append($" acc:empty(0x{hr:X8},h={h})"); return null; }

            var r = new Rectangle(x, y, Math.Max(w, 1), h);
            // 창 밖이면 이전 입력칸의 위치가 남아 있는 것으로 보고 쓰지 않는다.
            if (!Rectangle.Inflate(wr.ToRectangle(), 4, 4).IntersectsWith(r)) { dump?.Append($" acc:outside({x},{y})"); return null; }
            dump?.Append($" caret:acc({x},{y},{w}x{h})");
            return r;
        }
        catch (Exception ex) { dump?.Append($" acc:EXC {ex.GetType().Name} 0x{ex.HResult:X8}"); return null; }
        finally { Uia.Release(acc); }
    }
}
