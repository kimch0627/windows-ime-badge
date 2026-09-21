using System;
using System.Drawing;
using System.Text;

namespace ImeBadge;

/// <summary>UI Automation으로 caret 위치 찾기 (Win32 caret이 없는 앱용: Chrome/Edge/Electron/UWP).</summary>
static class UiaCaret
{
    /// <summary>caret 사각형(화면 좌표). 정확한 caret을 못 찾으면 입력창 왼쪽 아래 1x1(높이 0)로 근사.</summary>
    public static Rectangle? Find(StringBuilder? dump)
    {
        Uia.IUIAutomationElement? el = null;
        object? valuePat = null, textPat = null;
        Uia.IUIAutomationTextRangeArray? sel = null;
        Uia.IUIAutomationTextRange? r = null;
        try
        {
            el = Uia.Client.GetFocusedElement();
            if (el is null) return null;

            valuePat = el.GetCurrentPattern(Uia.UIA_ValuePatternId);
            if (valuePat is Uia.IUIAutomationValuePattern v && v.get_CurrentIsReadOnly())
            {
                dump?.Append(" uia:readonly");
                return null;
            }

            textPat = el.GetCurrentPattern(Uia.UIA_TextPatternId);
            if (textPat is Uia.IUIAutomationTextPattern tp)
            {
                sel = tp.GetSelection();
                if (sel is not null && sel.get_Length() > 0)
                {
                    r = sel.GetElement(0);
                    // 크롬 주소창 등은 "문서 처음~커서" 범위를 준다. 시작점을 끝점으로 옮겨 커서 한 점으로 접는다.
                    r.MoveEndpointByRange(Uia.TextPatternRangeEndpoint.Start, r, Uia.TextPatternRangeEndpoint.End);
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        var rects = r.GetBoundingRectangles();   // [l, t, w, h, l, t, w, h, ...]
                        if (rects.Length >= 4 && rects[3] > 0)
                        {
                            double left = rects[0], top = rects[1], height = rects[3];
                            dump?.Append($" uia:text({left:F0},{top + height:F0})");
                            return new Rectangle((int)left, (int)top, 1, (int)height);
                        }
                        r.ExpandToEnclosingUnit(Uia.TextUnit.Character);   // 넓이 0인 커서 범위 → 글자 한 칸으로
                    }
                }
            }

            int ct = el.GetCurrentPropertyValue(Uia.UIA_ControlTypePropertyId) is int i ? i : 0;
            if (ct == Uia.UIA_EditControlTypeId || ct == Uia.UIA_ComboBoxControlTypeId)
            {
                // BoundingRectangle 속성은 (left, top, width, height) double 4개
                if (el.GetCurrentPropertyValue(Uia.UIA_BoundingRectanglePropertyId) is double[] b && b.Length >= 4 && b[2] > 0)
                {
                    double left = b[0], bottom = b[1] + b[3];
                    dump?.Append($" uia:elem({left:F0},{bottom:F0})");
                    return new Rectangle((int)left, (int)bottom, 1, 0);   // 높이 0 = 근사 위치
                }
            }
            if (dump is not null)
            {
                // 어떤 컨트롤이라서 못 찾았는지 남긴다. (컨트롤 종류 ID, 클래스, UI 프레임워크, TextPattern 유무)
                string cls = el.GetCurrentPropertyValue(Uia.UIA_ClassNamePropertyId) as string ?? "";
                string fw = el.GetCurrentPropertyValue(Uia.UIA_FrameworkIdPropertyId) as string ?? "";
                bool focus = el.GetCurrentPropertyValue(Uia.UIA_HasKeyboardFocusPropertyId) is bool f && f;
                dump.Append($" uia:none(ct={ct} class='{cls}' fw={fw} text={(textPat is not null ? 1 : 0)} focus={(focus ? 1 : 0)})");
            }
        }
        catch (Exception ex) { dump?.Append($" uia:EXC {ex.GetType().Name} 0x{ex.HResult:X8}"); }
        finally
        {
            Uia.Release(r); Uia.Release(sel); Uia.Release(textPat); Uia.Release(valuePat); Uia.Release(el);
        }
        return null;
    }
}
