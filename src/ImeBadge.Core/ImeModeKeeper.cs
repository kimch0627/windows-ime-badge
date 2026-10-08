namespace ImeBadge;

/// <summary>
/// 실험 기능 "모든 프로그램에서 한/영 유지"의 판정. Windows 는 한/영 모드를 프로그램(스레드)마다 따로 기억해서, 메모장에서 한글로 바꾸고
/// 크롬으로 가면 크롬은 마지막으로 쓰던 영문 그대로다. 이 클래스는 사용자가 마지막으로 고른 모드(<see cref="Desired"/>)를 기억했다가,
/// 다른 창에 왔을 때 그 창의 모드가 다르면 바꾸라고 알려 준다. 실제로 바꾸는 일(IME 에 요청, 한/영 키 보내기)은 앱이 한다.
/// <para>
/// 어려운 점은 사용자가 고른 모드와 프로그램이 스스로 바꾼 모드를 가르는 일이다. 기준은 셋이다.
/// 1) 포커스가 다른 칸(창 핸들)으로 옮겨 가면 새 칸으로 본다. 칸마다 IME 상태가 따로 있어서 Xshell 처럼 활성화될 때 포커스가
/// 입력칸 → 메인 창 → 입력칸으로 오가면 읽은 모드도 따라 오가는데, 이것은 한/영 키가 아니다. 새 칸의 모드가 기억과 다르면 맞춘다.
/// 2) 창에 들어온 뒤 <see cref="ArrivalGraceMs"/> 안에 같은 칸에서 바뀐 것은 프로그램이 스스로 바꾼 것으로 보고 되돌린다
/// (Xshell 은 입력칸이 잡히고 수십 ms 뒤 영문으로 바꾼다).
/// 3) 그 뒤 기억한 모드와 같아진("맞춰진") 상태에서 바뀌면 사용자가 한/영 키를 누른 것이라 새 모드를 기억한다.
/// 비유하면 여러 방을 오가는 사람의 실내화다: 방마다 다른 실내화를 두지 않고, 방을 옮길 때 신던 실내화를 그대로 들고 간다.
/// </para>
/// 창·칸은 HWND 값, 시각은 Environment.TickCount64 의 ms. Win32 없이 테스트한다.
/// </summary>
public sealed class ImeModeKeeper
{
    /// <summary>바꾸라고 알린 뒤 결과를 기다리는 시간(ms). 이 뒤에도 그대로면 다음 방법으로 한 번 더.</summary>
    public const int RetryMs = 300;

    /// <summary>칸 하나에서 바꾸려고 시도하는 최대 횟수(IME 에 요청 → 한/영 키). 그래도 안 되면 그 칸은 그대로 둔다.</summary>
    public const int MaxAttempts = 2;

    /// <summary>
    /// 창에 들어온 뒤 이 시간(ms) 안에 바뀐 모드는 프로그램이 스스로 바꾼 것으로 보고 기억하지 않고 되돌린다.
    /// 이 안에 사용자가 한/영 키를 눌러도 되돌리므로, 너무 길면 막 들어온 창에서 고른 모드가 무시된다.
    /// </summary>
    public const int ArrivalGraceMs = 1000;

    bool? _desired;
    long _window, _focus;
    long _arrivedAt;
    bool? _lastSeen;            // 지금 칸에서 마지막으로 읽은 한/영. 아직 못 읽었으면 null
    bool _settled = true;       // 지금 칸이 기억한 모드와 같아졌다(또는 할 일이 없다)
    int _attempts;
    bool _keyUsed;              // 이 창에서 한/영 키를 이미 보냈다
    long _requestedAt = long.MinValue;

    /// <summary>유지할 모드. 한글 true, 영문 false, 아직 모르면 null.</summary>
    public bool? Desired => _desired;

    /// <summary>지금 칸에서 바꾸려고 한 횟수(방금 알린 것 포함). 1 이면 첫 시도(IME 에 요청), 2 면 한/영 키.</summary>
    public int Attempts => _attempts;

    /// <summary>
    /// 이 창에서 한/영 키를 보내도 되는가. 한/영 키는 누를 때마다 뒤집으므로 창마다 한 번만 보낸다
    /// (포커스가 다른 칸으로 옮겨 가도, 프로그램이 다시 바꿔도 IME 요청만 되풀이한다).
    /// </summary>
    public bool KeyAllowed => !_keyUsed;

    /// <summary>배지를 갱신할 때마다 지금 상태를 넘긴다.</summary>
    /// <param name="window">지금 활성 창.</param>
    /// <param name="focus">활성 창 안에서 포커스를 가진 창(칸). 없으면 0.</param>
    /// <param name="hangul">지금 한글이면 true, 영문이면 false, 다른 언어·모름·제외 앱이면 null.</param>
    /// <param name="canSwitch">지금 바꿔도 되는가(입력칸이 있다). 아니면 입력칸이 잡힐 때까지 기다린다.</param>
    /// <param name="learn">여기서 사용자가 바꾼 모드를 기억해도 되는가. 비밀번호 칸에서 영문으로 바꾼 것을 다른 프로그램까지 끌고 가지 않게 false.</param>
    /// <returns>지금 바꿀 모드(한글 true / 영문 false). 할 일이 없으면 null.</returns>
    public bool? Observe(long window, long focus, bool? hangul, bool canSwitch, bool learn, long nowMs)
    {
        if (window != _window)
        {
            _window = window;
            _arrivedAt = nowMs;
            _keyUsed = false;
            EnterField(focus);
        }
        else if (focus != _focus) EnterField(focus);

        if (_desired is null && learn && hangul is bool seen) _desired = seen;   // 처음 본 모드를 기억

        // 모름(null)은 건너뛴다: 읽기가 잠깐 실패했다가 같은 값이 다시 읽혀도 바뀐 것으로 세지 않는다.
        if (hangul is bool now && now != _lastSeen)
        {
            bool first = _lastSeen is null;
            _lastSeen = now;
            if (now == _desired) _settled = true;                       // 맞춰졌다(우리가 바꿨거나 사용자가 같은 쪽으로 바꿨다)
            else if (first) _settled = false;                           // 이 칸은 처음부터 다르다: 맞춘다
            else if (nowMs - _arrivedAt < ArrivalGraceMs)               // 막 들어온 프로그램이 스스로 바꿨다: 되돌린다
            {
                _settled = false;
                _attempts = 0;
                _requestedAt = long.MinValue;
            }
            else if (learn && _settled) _desired = now;                 // 맞춰진 뒤에 바뀌었다 = 사용자가 한/영 키를 눌렀다
        }

        if (_settled || !canSwitch || hangul is not bool current || _desired is not bool want || current == want) return null;
        if (_attempts >= MaxAttempts || (_attempts >= 1 && _keyUsed)) return null;
        if (_requestedAt != long.MinValue && nowMs - _requestedAt < RetryMs) return null;
        _attempts++;
        _requestedAt = nowMs;
        return want;
    }

    /// <summary>새 칸에 들어왔다. 그 칸의 모드는 아직 모른다(처음 읽힌 모드를 기억과 견준다).</summary>
    void EnterField(long focus)
    {
        _focus = focus;
        _lastSeen = null;
        _settled = true;
        _attempts = 0;
        _requestedAt = long.MinValue;
    }

    /// <summary>방금 알린 것을 보내지 못했다(보조키를 누르고 있어 미룸 등). 다음 갱신에 같은 시도를 다시 한다.</summary>
    public void NotSent()
    {
        if (_attempts > 0) _attempts--;
        _requestedAt = long.MinValue;
    }

    /// <summary>
    /// 이 칸에서는 더 시도하지 않고, 이 창에서는 한/영 키를 다시 보내지 않는다. 한/영 키를 보냈거나(뒤집기라 되풀이하면 반대가 된다)
    /// 보낼 수 없을 때 부른다.
    /// </summary>
    public void GiveUp()
    {
        _attempts = MaxAttempts;
        _keyUsed = true;
    }

    /// <summary>모두 잊는다(설정을 껐다 켬). 다음에 본 모드를 유지할 모드로 삼는다.</summary>
    public void Reset()
    {
        _desired = null;
        _window = 0;
        _arrivedAt = 0;
        _keyUsed = false;
        EnterField(0);
    }
}
