namespace ImeBadge;

/// <summary>
/// Shift 를 "계속 누르고 있는가" 판정. 폴링할 때마다 지금 눌림 여부를 넘기면, 떼지 않고 <see cref="ThresholdMs"/> 이상
/// 눌려 있을 때부터 true 를 돌려준다. 대문자 한 글자를 칠 때처럼 짧게 누른 Shift 로는 배지가 바뀌지 않게 한다.
/// Win32 없이 단위 테스트가 가능하도록 시각은 부르는 쪽이 넘긴다.
/// </summary>
public sealed class ShiftHold
{
    public const int ThresholdMs = 300;

    long? _downSince;

    /// <param name="down">지금 Shift 가 (다른 보조키 없이) 눌려 있는가.</param>
    /// <param name="nowMs">단조 증가 시각(ms). <c>Environment.TickCount64</c>.</param>
    public bool Update(bool down, long nowMs)
    {
        if (!down) { _downSince = null; return false; }
        _downSince ??= nowMs;
        return nowMs - _downSince.Value >= ThresholdMs;
    }
}
