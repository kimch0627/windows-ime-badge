namespace ImeBadge;

/// <summary>
/// 사용자가 한/영 키를 눌렀는지 폴링으로 알아챈다(실험 기능 "모든 프로그램에서 한/영 유지" 가 사용자의 선택과 프로그램이 스스로 바꾼 것을
/// 가르는 데 쓴다, <see cref="ImeModeKeeper"/>). 키보드 훅 없이 두 가지만 본다(<see cref="InsertToggle"/> 과 같은 방법).
/// <list type="number">
/// <item>토글 비트: Windows 는 키를 누를 때마다 그 키의 토글 비트(GetKeyState 의 최하위 비트)를 뒤집는다. 지난 폴링 뒤 바뀌었으면 홀수 번 눌렀다.
///   두 폴링 사이에 두 번 누르면 그대로라 모르지만, 한/영도 제자리라 상관없다.</item>
/// <item>눌림: 지금 실제로 눌려 있고(GetAsyncKeyState) 직전 폴링에는 아니었다. 토글 비트는 이 프로그램이 메시지를 처리한 뒤에야 따라오므로,
///   IME 가 바뀌자마자 불린 폴링에서는 이쪽이 먼저 보인다. 떼지 않고 누르고 있는 것은 처음 한 번만 센다.</item>
/// </list>
/// 이 프로그램이 보낸 한/영 키(<see cref="SentOwn"/>)도 토글 비트를 뒤집으므로 그 한 번은 세지 않는다. 보낸 키는 눌렀다 바로 떼므로 눌림으로는 보이지 않는다.
/// Win32 없이 단위 테스트가 가능하도록 시각은 부르는 쪽이 넘긴다.
/// </summary>
public sealed class KeyPressWatch
{
    /// <summary>보낸 키가 토글 비트에 나타나기를 기다리는 시간(ms). 이 안에 안 바뀌면(사용자도 함께 눌러 상쇄됨 등) 잊는다.</summary>
    public const int OwnKeyMs = 500;

    bool? _lastBit;
    bool _wasDown;
    long _ownAt = long.MinValue;

    /// <param name="toggled">지금 토글 비트.</param>
    /// <param name="down">지금 실제로 눌려 있는가.</param>
    /// <param name="nowMs">단조 증가 시각(ms). <c>Environment.TickCount64</c>.</param>
    /// <returns>지난 폴링 뒤 사용자가 눌렀다.</returns>
    public bool Update(bool toggled, bool down, long nowMs)
    {
        bool flipped = _lastBit is bool last && last != toggled;
        bool pressed = down && !_wasDown;
        _lastBit = toggled;
        _wasDown = down;
        if (_ownAt != long.MinValue && (flipped || nowMs - _ownAt > OwnKeyMs))
        {
            _ownAt = long.MinValue;
            flipped = false;   // 이 프로그램이 보낸 키가 뒤집은 것
        }
        return flipped || pressed;
    }

    /// <summary>이 프로그램이 방금 그 키를 보냈다. 다음에 보이는 토글 비트 변화 한 번은 사용자가 누른 것이 아니다.</summary>
    public void SentOwn(long nowMs) => _ownAt = nowMs;

    /// <summary>모두 잊는다(기능을 껐다 켬). 다음 폴링을 처음으로 본다.</summary>
    public void Reset()
    {
        _lastBit = null;
        _wasDown = false;
        _ownAt = long.MinValue;
    }
}
