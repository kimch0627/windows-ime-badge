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
        Uia.IUIAutomationTextRange? r = null, prev = null;
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
                    // 1) 넓이 0인 커서 범위 그대로
                    if (FirstRect(r) is { } c)
                    {
                        dump?.Append($" uia:text({c.Left:F0},{c.Top + c.Height:F0})");
                        return new Rectangle((int)c.Left, (int)c.Top, 1, (int)c.Height);
                    }
                    // 2) 바로 앞 글자의 오른쪽 끝. 커서가 글 끝에 있으면(타이핑 중 대부분) 뒤에 글자가 없어 3)이 실패하거나
                    //    마지막 글자의 왼쪽(한 칸 앞)을 주므로 이쪽을 먼저 본다. 앞 글자가 줄바꿈이면 윗줄 끝이라 쓰지 않는다.
                    //    이 호출들을 지원하지 않는 앱도 있으므로(E_NOTIMPL 등) 실패하면 3)으로 넘어간다.
                    (double Left, double Top, double Width, double Height)? prevRect = null;
                    try
                    {
                        prev = sel.GetElement(0);
                        prev.MoveEndpointByRange(Uia.TextPatternRangeEndpoint.Start, prev, Uia.TextPatternRangeEndpoint.End);
                        if (prev.MoveEndpointByUnit(Uia.TextPatternRangeEndpoint.Start, Uia.TextUnit.Character, -1) != 0
                            && !IsLineBreak(prev.GetText(2)))
                            prevRect = FirstRect(prev);
                    }
                    catch (Exception ex) { dump?.Append($" uia:prev-EXC 0x{ex.HResult:X8}"); }
                    if (prevRect is { } p)
                    {
                        double right = p.Left + p.Width;
                        dump?.Append($" uia:text-prev({right:F0},{p.Top + p.Height:F0})");
                        return new Rectangle((int)right, (int)p.Top, 1, (int)p.Height);
                    }
                    // 3) 커서 뒤 글자 한 칸으로 넓혀 그 왼쪽
                    r.ExpandToEnclosingUnit(Uia.TextUnit.Character);
                    if (FirstRect(r) is { } n)
                    {
                        dump?.Append($" uia:text-next({n.Left:F0},{n.Top + n.Height:F0})");
                        return new Rectangle((int)n.Left, (int)n.Top, 1, (int)n.Height);
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
            Uia.Release(prev); Uia.Release(r); Uia.Release(sel); Uia.Release(textPat); Uia.Release(valuePat); Uia.Release(el);
        }
        return null;
    }

    /// <summary>텍스트 범위의 첫 사각형(화면 좌표). 없거나 높이가 0이면 null.</summary>
    static (double Left, double Top, double Width, double Height)? FirstRect(Uia.IUIAutomationTextRange range)
    {
        var rects = range.GetBoundingRectangles();   // [l, t, w, h, l, t, w, h, ...]
        if (rects is null || rects.Length < 4 || rects[3] <= 0) return null;
        return (rects[0], rects[1], rects[2], rects[3]);
    }

    static bool IsLineBreak(string? s) => s is "\n" or "\r" or "\r\n";
}
