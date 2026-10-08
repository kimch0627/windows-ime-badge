using System;
using System.Drawing;
using System.Text;
using System.Threading;

namespace ImeBadge;

/// <summary>
/// Win32 caret 이 없는 앱(Chrome·Edge·Electron·UWP)에서 접근성 호출(UI Automation → 필요하면 MSAA)로 caret 을 찾되,
/// UI 스레드를 막지 않게 작업 스레드(<see cref="DeadlineWorker{T}"/>)에서 한다.
/// <para>
/// 이 호출들은 대상 앱의 UI 스레드가 답해야 끝난다. 앱이 바쁘면(도움말·새 창을 여는 중 등) 호출 하나가 UI Automation 기본 제한(20초)까지
/// 기다리고, caret 을 한 번 찾는 데 여러 번 불러 몇 분이 될 수 있다. 예전에는 UI 스레드에서 불러 그동안 배지와 트레이 메뉴가 모두 멈췄다.
/// 이제 UI 스레드는 <see cref="WaitMs"/> 까지만 기다리고, 넘기면 바로 전에 같은 입력칸에서 찾은 caret 을 잠깐 그대로 쓰거나(배지가 깜빡이지 않게)
/// 이번에는 caret 없음으로 넘어간다. 그 일이 끝날 때까지는 새로 묻지 않는다.
/// </para>
/// </summary>
static class A11yCaret
{
    /// <summary>UI 스레드가 한 번에 기다리는 최대 시간. 보통은 수 ms ~ 수십 ms 에 끝난다.</summary>
    public const int WaitMs = 400;
    /// <summary>이보다 오래 묶인 작업 스레드는 버리고 새로 만든다(다른 앱으로 옮겨도 다시 찾게).</summary>
    const int AbandonAfterMs = 3000;
    const int MaxAbandoned = 3;
    /// <summary>시간을 넘겼을 때 이만큼 지나지 않은, 같은 입력칸의 caret 은 그대로 쓴다.</summary>
    const int ReuseMs = 1000;
    /// <summary>한 번 찾는 데 이보다 오래 걸리면 오류 로그에 남긴다(디버그 모드가 아니어도). 멈춤 신고를 추적하려고.</summary>
    const int SlowLogMs = 2000;

    static readonly DeadlineWorker<A11yHit> Worker = new("ImeBadge caret (UIA)", WaitMs, AbandonAfterMs, MaxAbandoned,
        wait: (h, ms) => Native.WaitForSingleObject(h.SafeWaitHandle.DangerousGetHandle(), (uint)ms) == Native.WAIT_OBJECT_0,
        configure: t => t.SetApartmentState(ApartmentState.MTA));   // UI Automation 클라이언트는 MTA 에서 부르는 것이 권장

    // 마지막으로 찾은 caret 과 입력칸 정보 (UI 스레드에서만 읽고 쓴다)
    static IntPtr _lastFocus;
    static A11yHit _last;
    static long _lastMs;

    /// <param name="focus">포커스 창(없으면 활성 창). MSAA 가상 caret 을 물을 창이자, 시간을 넘겼을 때 같은 입력칸인지 보는 기준.</param>
    /// <param name="process">로그용 프로세스 이름.</param>
    /// <param name="query">실험 기능이 함께 물을 입력칸 정보.</param>
    public static A11yHit Find(IntPtr focus, string process, FocusQuery query, StringBuilder? dump)
    {
        var local = dump is null ? null : new StringBuilder();   // 작업 스레드가 쓴다. 시간을 넘기면 계속 쓰고 있을 수 있어 읽지 않는다
        var outcome = Worker.Run(() => Query(focus, process, query, local), out var hit);
        long now = Environment.TickCount64;
        if (outcome == DeadlineWorker<A11yHit>.Outcome.Done)
        {
            dump?.Append(local);
            _lastFocus = focus; _last = hit; _lastMs = now;
            return hit;
        }
        dump?.Append(outcome == DeadlineWorker<A11yHit>.Outcome.TimedOut ? " a11y:timeout" : " a11y:busy");
        if (focus == _lastFocus && now - _lastMs <= ReuseMs) { dump?.Append(" a11y:reuse"); return _last; }
        return default;
    }

    /// <summary>작업 스레드에서 돈다. UI Automation 이 입력칸 사각형만 주면(높이 0 = 근사 위치) MSAA 가상 caret 으로 정확한 위치를 찾아본다.</summary>
    static A11yHit Query(IntPtr focus, string process, FocusQuery query, StringBuilder? dump)
    {
        long t0 = Environment.TickCount64;
        var hit = UiaCaret.Find(query, dump);
        // 입력칸이 아닐 때(null)는 묻지 않는다. 가상 caret 에는 다른 곳에 있던 옛 위치가 남아 있을 수 있다.
        if (hit.Caret is { Height: 0 } && AccCaret.Find(focus, dump) is { } acc) hit = hit with { Caret = acc };
        long ms = Environment.TickCount64 - t0;
        if (ms >= SlowLogMs) Log.Warn($"slow caret query {ms} ms (UI Automation/MSAA): {process} '{Native.ClassName(focus)}'");
        return hit;
    }
}
