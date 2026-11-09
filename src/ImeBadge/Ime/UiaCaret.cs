using System;
using System.Drawing;
using System.Text;

namespace ImeBadge;

/// <summary>UI Automation으로 caret 위치 찾기 (Win32 caret이 없는 앱용: Chrome/Edge/Electron/UWP).</summary>
static class UiaCaret
{
    /// <summary>
    /// caret 사각형(화면 좌표)과, 실험 기능이 물은 입력칸 정보(<paramref name="query"/>). 정확한 caret을 못 찾으면 입력창 왼쪽 아래 1x1(높이 0)로 근사.
    /// caret 을 못 찾으면 입력칸 정보도 묻지 않는다(배지를 띄우지 않으므로 쓸 일이 없다).
    /// </summary>
    public static A11yHit Find(FocusQuery query, StringBuilder? dump)
    {
        Uia.IUIAutomationElement? el = null;
        object? valuePat = null, textPat = null;
        Uia.IUIAutomationTextRangeArray? sel = null;
        Uia.IUIAutomationTextRange? r = null, prev = null;
        try
        {
            el = Uia.Client.GetFocusedElement();
            if (el is null) { dump?.Append(" uia:nofocus"); return new A11yHit(null, Failure: "nofocus"); }

            valuePat = el.GetCurrentPattern(Uia.UIA_ValuePatternId);
            if (valuePat is Uia.IUIAutomationValuePattern v && v.get_CurrentIsReadOnly())
            {
                dump?.Append(" uia:readonly");
                return new A11yHit(null, NotInput: true);   // 웹 페이지 본문처럼 읽기만 하는 곳: 배지가 없는 것이 맞다
            }

            Rectangle? caret = null;
            bool selection = false, unverified = false, noSelection = false;
            textPat = el.GetCurrentPattern(Uia.UIA_TextPatternId);
            if (textPat is Uia.IUIAutomationTextPattern tp)
            {
                sel = tp.GetSelection();
                noSelection = sel is null || sel.get_Length() == 0;   // 글 영역이지만 커서가 없다(웹 페이지 본문처럼 읽기만 하는 곳)
                if (sel is not null && sel.get_Length() > 0)
                {
                    r = sel.GetElement(0);
                    // 실험 기능 "선택 영역 덮어쓰기 표시": 커서 한 점으로 접기 전에 시작과 끝이 다른지 본다. 크롬 주소창처럼 선택이 없어도
                    // "문서 처음~커서" 를 주는 컨트롤이 있어, 아래에서 커서 사각형을 믿을 수 있을 때(CaretRejected 가 아님)만 선택으로 본다.
                    bool spans = (query & FocusQuery.Selection) != 0 && Spans(r, dump);
                    // 크롬 주소창 등은 "문서 처음~커서" 범위를 준다. 시작점을 끝점으로 옮겨 커서 한 점으로 접는다.
                    r.MoveEndpointByRange(Uia.TextPatternRangeEndpoint.Start, r, Uia.TextPatternRangeEndpoint.End);
                    // 후보 셋을 모아 TextCaret.Pick 이 고른다.
                    // 1) 넓이 0인 커서 범위 그대로. 대개 가장 정확하지만 크롬 주소창은 커서가 어디 있든 입력칸 맨 앞을 준다.
                    var at = FirstRect(r);
                    // 2) 바로 앞 글자. 1)이 맞는지 대조하는 데 쓰고, 1)이 비었거나 틀렸으면 그 오른쪽 끝을 커서로 쓴다.
                    //    커서가 글 끝에 있으면(타이핑 중 대부분) 뒤에 글자가 없어 3)이 실패하거나 마지막 글자의 왼쪽(한 칸 앞)을 주므로
                    //    3)보다 먼저 본다. 앞 글자가 줄바꿈이면 윗줄 끝이라 쓰지 않는다.
                    //    이 호출들을 지원하지 않는 앱도 있으므로(E_NOTIMPL 등) 실패하면 없는 것으로 본다.
                    RectangleF? before = null;
                    try
                    {
                        prev = sel.GetElement(0);
                        prev.MoveEndpointByRange(Uia.TextPatternRangeEndpoint.Start, prev, Uia.TextPatternRangeEndpoint.End);
                        if (prev.MoveEndpointByUnit(Uia.TextPatternRangeEndpoint.Start, Uia.TextUnit.Character, -1) != 0
                            && !IsLineBreak(prev.GetText(2)))
                            before = FirstRect(prev);
                    }
                    catch (Exception ex) { dump?.Append($" uia:prev-EXC 0x{ex.HResult:X8}"); }
                    // 3) 커서 뒤 글자 한 칸으로 넓혀 그 왼쪽. 1)·2)로 정해지지 않을 때만 묻는다(UIA 호출이 한 번 더 든다).
                    Uia.IUIAutomationTextRange collapsed = r;
                    var pick = TextCaret.Pick(at, before, () =>
                    {
                        try
                        {
                            collapsed.ExpandToEnclosingUnit(Uia.TextUnit.Character);
                            return FirstRect(collapsed);
                        }
                        catch (Exception ex) { dump?.Append($" uia:next-EXC 0x{ex.HResult:X8}"); return null; }
                    });
                    if (pick.From != TextCaretSource.None)
                    {
                        // 버린 커서 사각형도 남긴다: "uia:caret-off(300,38) uia:text-prev(372,38)" 이면 1)이 엉뚱한 자리를 줬던 것.
                        if (pick.CaretRejected && at is { } off) dump?.Append($" uia:caret-off({off.Left:F0},{off.Bottom:F0})");
                        var c = pick.Rect;
                        string from = pick.From switch
                        {
                            TextCaretSource.Caret => "text",
                            TextCaretSource.Prev => "text-prev",
                            _ => "text-next",
                        };
                        // "uia:text?(300,38)" 이면 글자 사각형이 망가져 대조하지 못한 값이다(A11yCaret 이 MSAA 로 바꿔 본다).
                        dump?.Append($" uia:{from}{(pick.Unverified ? "?" : "")}({c.Left:F0},{c.Bottom:F0})");
                        caret = new Rectangle((int)c.Left, (int)c.Top, 1, (int)c.Height);
                        unverified = pick.Unverified;
                        selection = spans && !pick.CaretRejected && !pick.Unverified;
                    }
                }
            }

            if (caret is null)
            {
                int ct = el.GetCurrentPropertyValue(Uia.UIA_ControlTypePropertyId) is int i ? i : 0;
                if ((ct == Uia.UIA_EditControlTypeId || ct == Uia.UIA_ComboBoxControlTypeId)
                    // BoundingRectangle 속성은 (left, top, width, height) double 4개
                    && el.GetCurrentPropertyValue(Uia.UIA_BoundingRectanglePropertyId) is double[] b && b.Length >= 4 && b[2] > 0)
                {
                    double left = b[0], bottom = b[1] + b[3];
                    dump?.Append($" uia:elem({left:F0},{bottom:F0})");
                    caret = new Rectangle((int)left, (int)bottom, 1, 0);   // 높이 0 = 근사 위치
                }
                else
                {
                    // 묶음(Group)은 어느 UI 프레임워크가 그렸는지 봐야 입력칸인지 가린다(InputSurface.MayTakeText). 그 밖에는 로그를 남길 때만 묻는다.
                    bool hasText = textPat is not null;
                    string fw = dump is not null || (ct == InputSurface.Group && !hasText)
                        ? el.GetCurrentPropertyValue(Uia.UIA_FrameworkIdPropertyId) as string ?? ""
                        : "";
                    if (dump is not null)
                    {
                        // 어떤 컨트롤이라서 못 찾았는지 남긴다. (컨트롤 종류 ID, 클래스, UI 프레임워크, TextPattern 유무)
                        string cls = el.GetCurrentPropertyValue(Uia.UIA_ClassNamePropertyId) as string ?? "";
                        bool focus = el.GetCurrentPropertyValue(Uia.UIA_HasKeyboardFocusPropertyId) is bool f && f;
                        dump.Append($" uia:none(ct={ct} class='{cls}' fw={fw} text={(hasText ? 1 : 0)} focus={(focus ? 1 : 0)})");
                    }
                    // 버튼·목록 항목처럼 글자를 받지 않는 컨트롤이면 배지가 없는 것이 맞다. Pane·Custom 등은 직접 글자를 그리는 입력칸일 수 있다.
                    // Chromium 웹 화면의 TextPattern 없는 묶음도 입력칸이 아니다(키보드 포커스만 받는 단추 같은 요소).
                    // 글 영역(TextPattern)인데 커서·선택이 아예 없으면 편집 중이 아니다(입력칸이면 커서가 늘 있다).
                    return new A11yHit(null, NotInput: noSelection || !InputSurface.MayTakeText(ct, fw, hasText), ControlType: ct);
                }
            }
            return new A11yHit(caret, query == FocusQuery.None ? default : Facts(el, query, selection, dump), unverified);
        }
        catch (Exception ex)
        {
            dump?.Append($" uia:EXC {ex.GetType().Name} 0x{ex.HResult:X8}");
            return new A11yHit(null, Failure: $"0x{ex.HResult:X8}");
        }
        finally
        {
            Uia.Release(prev); Uia.Release(r); Uia.Release(sel); Uia.Release(textPat); Uia.Release(valuePat); Uia.Release(el);
        }
    }

    /// <summary>범위의 시작과 끝이 다른가(글이 선택돼 있다). 지원하지 않는 앱이면 false.</summary>
    static bool Spans(Uia.IUIAutomationTextRange range, StringBuilder? dump)
    {
        try { return range.CompareEndpoints(Uia.TextPatternRangeEndpoint.Start, range, Uia.TextPatternRangeEndpoint.End) != 0; }
        catch (Exception ex) { dump?.Append($" uia:sel-EXC 0x{ex.HResult:X8}"); return false; }
    }

    /// <summary>실험 기능이 물은 입력칸 정보: 비밀번호 칸인지(물었을 때만)와 선택 영역(<see cref="Find"/> 이 알아낸 것).</summary>
    static FocusFacts Facts(Uia.IUIAutomationElement el, FocusQuery query, bool selection, StringBuilder? dump)
    {
        bool password = false;
        if ((query & FocusQuery.Password) != 0)
        {
            try { password = el.GetCurrentPropertyValue(Uia.UIA_IsPasswordPropertyId) is bool p && p; }
            catch (Exception ex) { dump?.Append($" uia:facts-EXC 0x{ex.HResult:X8}"); }
        }
        return new FocusFacts(password, selection);
    }

    /// <summary>텍스트 범위의 첫 사각형(화면 좌표). 없거나 높이가 0이면 null.</summary>
    static RectangleF? FirstRect(Uia.IUIAutomationTextRange range)
    {
        var rects = range.GetBoundingRectangles();   // [l, t, w, h, l, t, w, h, ...]
        if (rects is null || rects.Length < 4 || rects[3] <= 0) return null;
        return new RectangleF((float)rects[0], (float)rects[1], (float)rects[2], (float)rects[3]);
    }

    static bool IsLineBreak(string? s) => s is "\n" or "\r" or "\r\n";
}
