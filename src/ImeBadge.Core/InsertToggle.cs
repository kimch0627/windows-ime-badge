using System;
using System.Collections.Generic;

namespace ImeBadge;

/// <summary>
/// Insert 키로 켠 "겹쳐 쓰기" 를 창마다 추적한다. 폴링할 때마다 Windows 의 Insert 토글 비트, 보조키 눌림, 지금 활성 창을 넘긴다.
/// <para>
/// 토글 비트(<c>GetKeyState(VK_INSERT)</c> 의 최하위 비트)를 그대로 보여 주지 않는 이유: 겹쳐 쓰기는 Windows 가 아니라 앱마다
/// 따로 기억하고, 앱은 늘 삽입 모드로 시작한다. 토글 비트는 어느 앱에서 눌렀든 하나뿐이라 한 앱에서 켜면 다른 앱에서도 켜진 것으로
/// 보인다. 또 Ctrl+Insert(복사)·Shift+Insert(붙여넣기)도 이 비트를 바꾼다.
/// </para>
/// <para>
/// 그래서 비트가 바뀐 것(지난 폴링 뒤 Insert 를 홀수 번 누름)만 보고 그때 활성인 창의 상태를 뒤집는다. 두 폴링 사이에 두 번 누르면
/// 비트가 그대로라 뒤집지 않는데, 앱도 두 번 바뀌어 제자리이므로 맞다. 지금이나 직전 폴링에 Ctrl·Shift·Alt·Win 이 눌려 있었으면
/// 단축키로 보고 세지 않는다. 키보드 훅 없이 폴링하므로, 보조키를 폴링 간격보다 짧게 누른 아주 빠른 단축키는 놓칠 수 있다.
/// </para>
/// Win32 없이 단위 테스트가 가능하도록 창은 숫자(HWND 값)로 받는다.
/// </summary>
public sealed class InsertToggle
{
    bool? _lastBit;
    bool _modifierWasDown;
    readonly HashSet<long> _overtype = new();

    /// <summary>겹쳐 쓰기로 기억하고 있는 창 수.</summary>
    public int Count => _overtype.Count;

    /// <param name="toggled">지금 Insert 토글 비트가 켜져 있는가.</param>
    /// <param name="modifierDown">지금 Ctrl·Shift·Alt·Win 중 하나라도 눌려 있는가.</param>
    /// <param name="window">지금 활성 창. 0 이면 없음.</param>
    /// <returns><paramref name="window"/> 가 겹쳐 쓰기 상태인가.</returns>
    public bool Update(bool toggled, bool modifierDown, long window)
    {
        bool shortcut = modifierDown || _modifierWasDown;
        if (_lastBit is bool last && last != toggled && !shortcut && window != 0)
        {
            if (!_overtype.Remove(window)) _overtype.Add(window);
        }
        _lastBit = toggled;
        _modifierWasDown = modifierDown;
        return window != 0 && _overtype.Contains(window);
    }

    /// <summary>닫힌 창을 잊는다. HWND 값은 다시 쓰일 수 있으므로 새 창이 옛 창의 겹쳐 쓰기를 물려받지 않게 한다.</summary>
    public void Forget(Func<long, bool> isGone) => _overtype.RemoveWhere(w => isGone(w));
}
