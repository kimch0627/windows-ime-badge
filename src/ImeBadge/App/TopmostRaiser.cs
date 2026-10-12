using System;

namespace ImeBadge;

/// <summary>
/// 클릭 통과 레이어드 창(배지·커서 옆 말풍선·소나)을 최상위 창들 중 맨 위로 올린다. 활성 창이 자기도 최상위(TopMost)인 경우(FlowLauncher,
/// 작업 관리자 "항상 위" 등) 나중에 뜬 쪽이 위에 오므로, 보일 때마다 실제 z-order 를 확인해 그 창보다 위로 올린다(VW-05 에서 배지의 로직을 뺐다).
/// 보통의 SetWindowPos 로 안 되면(Windows 는 마지막 입력을 받지 않은 프로세스가 활성 창 위로 창을 올리는 것을 막는다) 활성 창 스레드의
/// 입력 큐에 잠깐 붙어 그 권한을 빌린 뒤 바로 떼어 낸다. 안전장치: 응답 없는 창에는 붙지 않고(같이 멈출 수 있음), 같은 창에 세 번 실패하면
/// 그 창에 대해서는 포기한다. 마우스 버튼이 눌려 있으면(드래그 중) 붙지 않는다. 입력 큐를 붙였다 떼면 그 스레드의 마우스 캡처가 풀려 드래그가 끊긴다.
/// </summary>
sealed class TopmostRaiser
{
    readonly Func<IntPtr> _handle;
    readonly string _name;
    IntPtr _failFg;
    int _failCount;

    /// <param name="handle">올릴 창의 핸들(창이 다시 만들어질 수 있어 그때그때 읽는다).</param>
    /// <param name="name">로그에 쓸 이름.</param>
    public TopmostRaiser(Func<IntPtr> handle, string name) { _handle = handle; _name = name; }

    /// <summary>위치·크기는 그대로 두고 z-order만 최상위 창들 중 맨 위로 올린다. 포커스는 건드리지 않는다.</summary>
    public void RaiseToTop() =>
        Native.SetWindowPos(_handle(), Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    /// <summary>활성 창이 최상위이고 이 창보다 위에 있으면 그 위로 올린다. 아니면 아무것도 하지 않는다.</summary>
    public void KeepAboveForeground()
    {
        var fg = Native.GetForegroundWindow();
        if (Native.IsTopmost(fg) && Native.IsAbove(fg, _handle())) RaiseAbove(fg);
    }

    /// <summary>활성 창 <paramref name="fg"/> 보다 위로 올린다(위 설명의 절차).</summary>
    public void RaiseAbove(IntPtr fg)
    {
        IntPtr me = _handle();
        RaiseToTop();
        if (!Native.IsAbove(fg, me)) return;
        if (Native.IsMouseButtonDown()) { Log.WriteIfChanged($"raise {_name}: skip attach while mouse button down fg='{Native.ClassName(fg)}'"); return; }
        if (_failFg == fg && _failCount >= 3) return;
        if (Native.IsHungAppWindow(fg)) { Log.WriteIfChanged($"raise {_name}: skip hung fg='{Native.ClassName(fg)}'"); return; }

        uint fgTid = Native.GetWindowThreadProcessId(fg, out _), self = Native.GetCurrentThreadId();
        if (fgTid == 0 || fgTid == self) return;
        if (!Native.AttachThreadInput(self, fgTid, true)) { NoteFailure(fg); Log.WriteIfChanged($"raise {_name}: AttachThreadInput failed fg='{Native.ClassName(fg)}'"); return; }
        try { RaiseToTop(); }
        finally { Native.AttachThreadInput(self, fgTid, false); }

        bool ok = !Native.IsAbove(fg, me);
        if (ok) { _failFg = IntPtr.Zero; _failCount = 0; } else NoteFailure(fg);
        Log.WriteIfChanged($"raise {_name}: via AttachThreadInput fg='{Native.ClassName(fg)}' ok={ok}");
    }

    void NoteFailure(IntPtr fg)
    {
        if (_failFg == fg) _failCount++;
        else { _failFg = fg; _failCount = 1; }
    }
}
