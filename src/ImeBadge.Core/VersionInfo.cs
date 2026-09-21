using System;

namespace ImeBadge;

/// <summary>
/// "v1.2.3", "1.2.3-dev.abc1234", "1.2.3+sha" 같은 문자열에서 숫자 버전만 뽑아 비교한다.
/// 사전 릴리스 꼬리표("-dev")가 붙은 쪽은 같은 숫자의 정식 버전보다 낮다고 본다.
/// </summary>
public static class VersionInfo
{
    public static bool TryParse(string? text, out Version version, out bool isPrerelease)
    {
        version = new Version(0, 0, 0);
        isPrerelease = false;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.AsSpan().Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];

        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        int dash = s.IndexOf('-');
        if (dash >= 0) { isPrerelease = true; s = s[..dash]; }

        if (!Version.TryParse(s, out var v)) return false;
        version = new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
        return true;
    }

    /// <summary>latest 가 current 보다 새 버전인가. 둘 중 하나라도 못 읽으면 false(업데이트 알림을 띄우지 않음).</summary>
    public static bool IsNewer(string? current, string? latest)
    {
        if (!TryParse(current, out var cur, out bool curPre) || !TryParse(latest, out var lat, out bool latPre)) return false;
        int cmp = lat.CompareTo(cur);
        if (cmp != 0) return cmp > 0;
        return curPre && !latPre;   // 0.0.0-dev 로 돌리는 중이면 0.0.0 정식이 더 새것
    }

    /// <summary>"1.2.3+abcdef" → "1.2.3". 표시용.</summary>
    public static string Display(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return "0.0.0";
        int plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
