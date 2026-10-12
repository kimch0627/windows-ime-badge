namespace ImeBadge;

/// <summary>
/// 실험 기능의 경고 말풍선(비밀번호 칸 경고·포커스 뺏김 경고)이 붙은 포커스 자리. 포커스가 그 자리를 벗어나면 정해 둔 시간을 기다리지 않고
/// 바로 내린다. 배지는 포커스가 입력칸을 벗어나면 곧바로 숨는데, 경고만 몇 초씩 남아 있으면 이미 떠난 칸에 대한 경고가 엉뚱한 곳에 떠 있게 된다.
/// <list type="bullet">
/// <item>비밀번호 칸 경고(<c>needsCaret: true</c>): 띄울 때의 활성 창·포커스 창에서, 배지가 caret 을 찾는 동안만 유효하다. 다른 창·다른 칸으로
/// 옮기면 바로, 입력칸 밖을 눌러 배지가 숨으면 caret 을 <see cref="MissesToDrop"/> 번 잇달아 못 찾았을 때 내린다(한 번 놓친 것은 웹 페이지의
/// UI Automation 이 잠깐 caret 을 못 준 것일 수 있다. 그때마다 내리면 다시 들어온 것으로 보고 경고가 깜빡이며 다시 뜬다).</item>
/// <item>포커스 뺏김 경고(<c>focus: 0</c>, <c>needsCaret: false</c>): 끼어든 창이 앞에 있는 동안만 유효하다. 그 창에는 입력칸이 없을 수도 있고
/// 뜨자마자 스스로 포커스를 옮기기도 하므로, 활성 창이 다시 바뀔 때(Alt+Tab 으로 돌아가는 등)만 내린다.</item>
/// </list>
/// 창·포커스는 HWND 값(long). 0 은 "보지 않음".
/// </summary>
public sealed class CalloutFocus
{
    /// <summary>caret 을 이만큼 잇달아 못 찾으면 입력칸을 벗어난 것으로 본다(기본 반응 속도 0.1초에서 약 0.2초).</summary>
    public const int MissesToDrop = 2;

    readonly record struct Anchor(string Tag, long Foreground, long Focus, bool NeedsCaret);

    Anchor? _anchor;
    int _misses;

    /// <summary>지금 붙어 있는 경고의 태그. 없으면 null.</summary>
    public string? Tag => _anchor?.Tag;

    /// <summary>경고를 띄우면서 그 자리를 기억한다(이전 경고의 자리는 잊는다).</summary>
    /// <param name="focus">포커스 창. 0 이면 포커스 창이 바뀌어도 내리지 않는다.</param>
    /// <param name="needsCaret">배지가 caret 을 잃으면(입력칸 밖) 내린다.</param>
    public void Pin(string tag, long foreground, long focus, bool needsCaret)
    {
        _anchor = new(tag, foreground, focus, needsCaret);
        _misses = 0;
    }

    public void Clear()
    {
        _anchor = null;
        _misses = 0;
    }

    /// <summary>
    /// 갱신할 때마다 부른다. 포커스가 경고의 자리를 벗어났으면 그 경고의 태그를 돌려주고 잊는다(부르는 쪽이 바로 내린다). 아니면 null.
    /// </summary>
    /// <param name="hasCaret">배지가 caret 을 찾아 보이는 중인가.</param>
    public string? Check(long foreground, long focus, bool hasCaret)
    {
        if (_anchor is not { } a) return null;
        _misses = a.NeedsCaret && !hasCaret ? _misses + 1 : 0;
        bool left = foreground != a.Foreground
            || (a.Focus != 0 && focus != a.Focus)
            || _misses >= MissesToDrop;
        if (!left) return null;
        Clear();
        return a.Tag;
    }
}
