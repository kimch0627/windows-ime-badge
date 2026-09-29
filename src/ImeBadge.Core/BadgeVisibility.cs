using System;

namespace ImeBadge;

/// <summary>
/// 배지를 언제 얼마나 보일지(표시 방식). 설정 파일에는 문자열로 저장한다. 열거형 값을 늘리면 구버전으로 되돌렸을 때
/// 모르는 이름 때문에 설정 전체가 초기화되므로 <see cref="Settings.Theme"/> 처럼 문자열로 둔다.
/// 비유하면 현관 센서등의 모드다: 늘 켜 두기, 사람이 지나갈 때(타이핑 중) 어둡게, 문이 열릴 때(한/영이 바뀔 때)만 잠깐 켜기.
/// </summary>
public static class BadgeVisibility
{
    /// <summary>늘 보인다(예전과 같음, 기본).</summary>
    public const string Always = "always";

    /// <summary>글자를 치는 동안(caret 이 움직이는 동안) 옅게, 멈추면 다시 또렷하게. 배지가 옆 글자를 가리는 것을 줄인다.</summary>
    public const string DimWhileTyping = "dimWhileTyping";

    /// <summary>한/영·Caps Lock 이 바뀌거나 입력칸에 처음 들어갔을 때만 잠깐 보이고 사라진다.</summary>
    public const string OnChange = "onChange";

    public static readonly string[] All = { Always, DimWhileTyping, OnChange };

    /// <summary>타이핑 중으로 보는 시간(ms). 마지막으로 caret 이 움직인 뒤 이만큼 조용하면 다시 또렷해진다.</summary>
    public const int TypingQuietMs = 800;

    /// <summary>타이핑 중 배지 짙기(설정한 불투명도에 곱한다). 위치는 보이되 글자를 덜 가린다.</summary>
    public const float TypingFactor = 0.25f;

    /// <summary>"바뀔 때만" 에서 바뀐 뒤 또렷하게 보이는 시간(ms)과 그 뒤 사라지는 시간(ms).</summary>
    public const int ShowMs = 2000, FadeOutMs = 400;

    /// <summary>또렷해지거나 옅어질 때 걸리는 시간(ms). 폴링 간격(기본 100ms)마다 한 단계씩 바뀐다.</summary>
    public const int RampMs = 200;

    /// <summary>모르는 값(손으로 고친 설정 파일, 새 버전의 값)은 <see cref="Always"/>.</summary>
    public static string Normalize(string? value)
    {
        foreach (var v in All)
            if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) return v;
        return Always;
    }

    /// <summary>
    /// 지금 배지 짙기 배율(0~1). 1 이면 설정한 불투명도 그대로, 0 이면 보이지 않는다.
    /// 시각은 모두 같은 단조 시계의 ms(Environment.TickCount64). 아직 없었던 일은 <see cref="long.MinValue"/>.
    /// </summary>
    /// <param name="lastTypingMs">마지막으로 caret 이 움직인(글자를 친) 시각.</param>
    /// <param name="lastChangeMs">마지막으로 한/영·Caps Lock 이 바뀌었거나 배지가 새로 나타난 시각.</param>
    public static float Factor(string mode, long nowMs, long lastTypingMs, long lastChangeMs)
    {
        if (mode == DimWhileTyping)
        {
            // 바뀐 직후에는 타이핑 중이어도 또렷하게: 한/영을 바꾸고 바로 치기 시작해도 바뀐 것은 보여야 한다.
            if (Since(nowMs, lastChangeMs) < ShowMs) return 1f;
            long quiet = Since(nowMs, lastTypingMs);
            if (quiet < TypingQuietMs) return TypingFactor;
            return Lerp(TypingFactor, 1f, (quiet - TypingQuietMs) / (float)RampMs);
        }
        if (mode == OnChange)
        {
            long since = Since(nowMs, lastChangeMs);
            if (since < ShowMs) return 1f;
            return Lerp(1f, 0f, (since - ShowMs) / (float)FadeOutMs);
        }
        return 1f;
    }

    static long Since(long now, long then) => then == long.MinValue ? long.MaxValue : Math.Max(0, now - then);

    static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
}
