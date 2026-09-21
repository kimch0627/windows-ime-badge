using System;
using System.Collections.Generic;

namespace ImeBadge;

/// <summary>제외 앱 목록과 프로세스 이름을 맞춰 본다. 대소문자·".exe" 유무를 무시하고, 끝의 '*' 는 접두어 일치.</summary>
public static class ProcessFilter
{
    public static bool IsExcluded(IEnumerable<string>? patterns, string? processName)
    {
        if (patterns is null || string.IsNullOrEmpty(processName)) return false;
        string name = Normalize(processName);
        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string p = Normalize(raw);
            if (p.EndsWith('*'))
            {
                if (name.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (string.Equals(p, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>"C:\x\Foo.EXE" → "foo". 경로와 .exe 를 떼고 소문자로.</summary>
    public static string Normalize(string s)
    {
        s = s.Trim();
        int slash = s.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) s = s[(slash + 1)..];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s.ToLowerInvariant();
    }
}
