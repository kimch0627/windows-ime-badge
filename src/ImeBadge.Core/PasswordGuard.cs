using System;

namespace ImeBadge;

/// <summary>비밀번호 칸에서 입력이 틀릴 위험. 한글 입력 중이거나 Caps Lock 이 켜져 있다(둘 다일 수 있다).</summary>
[Flags]
public enum PasswordRisk { None = 0, Hangul = 1, CapsLock = 2 }

/// <summary>
/// 실험 기능 "비밀번호 칸 경고"의 판정. 비밀번호 칸에 들어왔을 때 위험이 있으면 알리고, 그 칸에 머무는 동안 위험이 바뀌면(Caps Lock 을 켬)
/// 새로 알리고, 위험이 없어지면(영문으로 바꿈) 내린다. 같은 위험을 되풀이해 알리지 않는다. 비유하면 문 앞 센서등이다: 사람이 들어올 때
/// 한 번 켜지고, 상황이 바뀔 때만 다시 반응한다.
/// </summary>
public sealed class PasswordGuard
{
    bool _inField;
    long _window;
    PasswordRisk _shown;

    /// <summary>배지를 갱신할 때마다 부른다.</summary>
    /// <param name="inPasswordField">지금 포커스가 비밀번호 칸에 있는가.</param>
    /// <param name="window">지금 활성 창(다른 창의 비밀번호 칸으로 옮기면 새로 들어온 것으로 본다).</param>
    /// <param name="risk">지금의 위험.</param>
    /// <returns>할 일. null 이면 그대로 둔다, <see cref="PasswordRisk.None"/> 이면 경고를 내린다, 그 밖이면 그 위험으로 경고를 띄운다.</returns>
    public PasswordRisk? Update(bool inPasswordField, long window, PasswordRisk risk)
    {
        if (!inPasswordField)
        {
            bool shown = _inField && _shown != PasswordRisk.None;
            _inField = false;
            _shown = PasswordRisk.None;
            return shown ? PasswordRisk.None : null;
        }
        bool entered = !_inField || window != _window;
        _inField = true;
        _window = window;
        if (!entered && risk == _shown) return null;
        var before = _shown;
        _shown = risk;
        if (risk != PasswordRisk.None) return risk;
        return before != PasswordRisk.None ? PasswordRisk.None : null;
    }

    /// <summary>잊는다(설정을 끔, 일시 중지). 다음에 비밀번호 칸에 있으면 새로 들어온 것으로 본다.</summary>
    public void Reset()
    {
        _inField = false;
        _shown = PasswordRisk.None;
    }
}
