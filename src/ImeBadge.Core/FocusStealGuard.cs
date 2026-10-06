using System;

namespace ImeBadge;

/// <summary>
/// 실험 기능 "포커스 뺏김 경고"의 판정. 글자를 치는 도중에 다른 창이 스스로 앞으로 나와(메신저 창, 업데이트 알림, 오류 대화상자 등)
/// 치던 글자가 그 창으로 들어가기 시작하면 알린다. 키 입력 훅 없이 판단한다: 어떤 키를 눌렀는지는 보지 않고, Windows 가 알려 주는
/// "마지막 입력 시각"(GetLastInputInfo)만 본다.
/// <list type="number">
/// <item>활성 창이 바뀌기 직전(<see cref="TypingRecentMs"/> 안)까지 글자를 치고 있었다.</item>
/// <item>그 직전(<see cref="UserSwitchRecentMs"/> 안)에 마우스를 쓰거나 Alt·Win·Ctrl 을 누르지 않았다(Alt+Tab, 클릭, 단축키로 바꾼 것이 아니다).</item>
/// <item>바뀐 뒤 <see cref="WatchMs"/> 안에 마우스를 움직이지 않은 채 입력이 이어졌다. 손이 아직 치던 흐름에 있다는 뜻이다.
///   사람이 새 창을 보고 치기 시작하기까지는 이보다 오래 걸리므로, Enter 로 프로그램을 열고 기다렸다 치는 경우는 대개 걸리지 않는다.</item>
/// </list>
/// 시각은 모두 같은 단조 시계(Environment.TickCount64)의 ms 이고, 아직 없었던 일은 <see cref="long.MinValue"/>. 창은 HWND 값.
/// </summary>
public sealed class FocusStealGuard
{
    public const int TypingRecentMs = 1000;
    public const int UserSwitchRecentMs = 1000;
    public const int WatchMs = 700;

    /// <summary>바뀐 직후의 입력은 창을 바꾼 그 입력(키를 뗀 것 등)일 수 있어 세지 않는다.</summary>
    public const int GraceMs = 40;

    long _armedAt = long.MinValue;
    long _window;

    /// <summary>창이 바뀐 뒤 입력이 이어지는지 지켜보는 중이다.</summary>
    public bool Armed => _armedAt != long.MinValue;

    /// <summary>활성 창이 바뀌었다. 지켜보기 시작했으면 true.</summary>
    /// <param name="newWindow">새 활성 창.</param>
    /// <param name="typingWindow">글자를 치던 창(배지가 마지막으로 떠 있던 창).</param>
    /// <param name="lastTypingMs">마지막으로 글자를 친(caret 이 움직인) 시각.</param>
    /// <param name="lastUserSwitchMs">마지막으로 마우스를 쓰거나 Alt·Win·Ctrl 을 누른 시각.</param>
    public bool OnForegroundChanged(long nowMs, long newWindow, long typingWindow, long lastTypingMs, long lastUserSwitchMs)
    {
        _armedAt = long.MinValue;
        if (newWindow == 0 || typingWindow == 0 || newWindow == typingWindow) return false;
        if (!Within(nowMs, lastTypingMs, TypingRecentMs)) return false;
        if (Within(nowMs, lastUserSwitchMs, UserSwitchRecentMs)) return false;
        _armedAt = nowMs;
        _window = newWindow;
        return true;
    }

    /// <summary>지켜보는 동안 갱신할 때마다 부른다. 경고할 때가 되면 한 번 true 를 돌려주고 지켜보기를 끝낸다.</summary>
    /// <param name="foreground">지금 활성 창. 그사이 또 바뀌었으면 끝낸다.</param>
    /// <param name="lastInputMs">마지막 입력(키보드·마우스) 시각.</param>
    /// <param name="pointerUsed">지켜보기 시작한 뒤 마우스를 움직였거나 누르고 있다. 사용자가 이미 알아챘다고 보고 끝낸다.</param>
    public bool Check(long nowMs, long foreground, long lastInputMs, bool pointerUsed)
    {
        if (!Armed) return false;
        if (foreground != _window || pointerUsed) { Disarm(); return false; }
        if (lastInputMs != long.MinValue && lastInputMs >= _armedAt + GraceMs && lastInputMs <= _armedAt + WatchMs)
        {
            Disarm();
            return true;
        }
        if (nowMs - _armedAt > WatchMs) Disarm();
        return false;
    }

    public void Disarm() => _armedAt = long.MinValue;

    static bool Within(long now, long then, int ms) => then != long.MinValue && now >= then && now - then <= ms;
}
