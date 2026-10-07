namespace ImeBadge;

/// <summary>
/// 실험 기능 "모든 프로그램에서 한/영 유지"의 판정. Windows 는 한/영 모드를 프로그램(스레드)마다 따로 기억해서, 메모장에서 한글로 바꾸고
/// 크롬으로 가면 크롬은 마지막으로 쓰던 영문 그대로다. 이 클래스는 사용자가 마지막으로 고른 모드(<see cref="Desired"/>)를 기억했다가,
/// 다른 창에 왔을 때 그 창의 모드가 다르면 바꾸라고 알려 준다. 실제로 바꾸는 일(IME 에 요청, 한/영 키 보내기)은 앱이 한다.
/// <para>
/// 사용자가 고른 모드와 프로그램이 스스로 바꾼 모드를 가르는 기준은 "맞춰졌는가(settled)" 다. 창에 와서 기억한 모드와 같아진 뒤에
/// 모드가 바뀌면 사용자가 한/영 키를 누른 것이라 새 모드를 기억한다. 맞추기 전(바꾸는 중)의 변화는 사용자 선택으로 보지 않는다.
/// 비유하면 여러 방을 오가는 사람의 실내화다: 방마다 다른 실내화를 두지 않고, 방을 옮길 때 신던 실내화를 그대로 들고 간다.
/// </para>
/// 창은 HWND 값, 시각은 Environment.TickCount64 의 ms. Win32 없이 테스트한다.
/// </summary>
public sealed class ImeModeKeeper
{
    /// <summary>바꾸라고 알린 뒤 결과를 기다리는 시간(ms). 이 뒤에도 그대로면 다음 방법으로 한 번 더.</summary>
    public const int RetryMs = 300;

    /// <summary>창 하나에서 바꾸려고 시도하는 최대 횟수(IME 에 요청 → 한/영 키). 그래도 안 되면 그 창은 그대로 둔다.</summary>
    public const int MaxAttempts = 2;

    bool? _desired;
    long _window;
    bool? _lastSeen;
    bool _settled = true;
    int _attempts;
    long _requestedAt = long.MinValue;

    /// <summary>유지할 모드. 한글 true, 영문 false, 아직 모르면 null.</summary>
    public bool? Desired => _desired;

    /// <summary>지금 창에서 바꾸려고 한 횟수(방금 알린 것 포함). 1 이면 첫 시도.</summary>
    public int Attempts => _attempts;

    /// <summary>배지를 갱신할 때마다 지금 상태를 넘긴다.</summary>
    /// <param name="window">지금 활성 창.</param>
    /// <param name="hangul">지금 한글이면 true, 영문이면 false, 다른 언어·모름·제외 앱이면 null.</param>
    /// <param name="canSwitch">지금 바꿔도 되는가(입력칸이 있다). 아니면 입력칸이 잡힐 때까지 기다린다.</param>
    /// <param name="learn">여기서 사용자가 바꾼 모드를 기억해도 되는가. 비밀번호 칸에서 영문으로 바꾼 것을 다른 프로그램까지 끌고 가지 않게 false.</param>
    /// <returns>지금 바꿀 모드(한글 true / 영문 false). 할 일이 없으면 null.</returns>
    public bool? Observe(long window, bool? hangul, bool canSwitch, bool learn, long nowMs)
    {
        if (window != _window)
        {
            _window = window;
            _attempts = 0;
            _requestedAt = long.MinValue;
            _lastSeen = hangul;
            if (_desired is null && hangul is bool first && learn) _desired = first;
            _settled = hangul is null || _desired is null || hangul == _desired;
        }
        else if (hangul != _lastSeen)
        {
            _lastSeen = hangul;
            if (hangul is bool now)
            {
                if (now == _desired) _settled = true;                       // 맞춰졌다(우리가 바꿨거나 사용자가 같은 쪽으로 바꿨다)
                else if (learn && (_settled || _desired is null))           // 맞춰진 뒤에 바뀌었다 = 사용자가 한/영 키를 눌렀다
                {
                    _desired = now;
                    _settled = true;
                }
            }
        }

        if (_settled || !canSwitch || hangul is not bool current || _desired is not bool want || current == want) return null;
        if (_attempts >= MaxAttempts) return null;
        if (_requestedAt != long.MinValue && nowMs - _requestedAt < RetryMs) return null;
        _attempts++;
        _requestedAt = nowMs;
        return want;
    }

    /// <summary>방금 알린 것을 보내지 못했다(보조키를 누르고 있어 미룸 등). 다음 갱신에 같은 시도를 다시 한다.</summary>
    public void NotSent()
    {
        if (_attempts > 0) _attempts--;
        _requestedAt = long.MinValue;
    }

    /// <summary>이 창에서는 더 시도하지 않는다. 한/영 키는 누를 때마다 뒤집으므로 한 번 보낸 뒤에는 되풀이하지 않는다.</summary>
    public void GiveUp() => _attempts = MaxAttempts;

    /// <summary>모두 잊는다(설정을 껐다 켬). 다음에 본 모드를 유지할 모드로 삼는다.</summary>
    public void Reset()
    {
        _desired = null;
        _window = 0;
        _lastSeen = null;
        _settled = true;
        _attempts = 0;
        _requestedAt = long.MinValue;
    }
}
