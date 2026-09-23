using System;
using System.Collections.Generic;

namespace ImeBadge;

/// <summary>
/// 지금 돌고 있는 프로그램이 어떤 형태로 설치되었는가. 릴리스에서 어떤 파일을 받아 어떻게 적용할지가 이것으로 갈린다.
/// 비유하면 같은 책의 "양장본 / 문고판"이다. 내용은 같지만 갈아 끼우는 방법이 다르다.
/// </summary>
public enum UpdateFlavor
{
    /// <summary>설치 프로그램(ImeBadge-Setup-*.exe)으로 설치됨. 새 설치 프로그램을 조용히(/SILENT) 다시 돌린다.</summary>
    Installer,
    /// <summary>무설치 self-contained exe(런타임 포함). 새 exe 를 받아 제자리에서 갈아 끼운다.</summary>
    SelfContainedExe,
    /// <summary>무설치 framework-dependent exe(.NET 8 런타임 필요). 갈아 끼우는 방법은 self-contained 와 같다.</summary>
    FrameworkExe,
}

/// <summary>GitHub 릴리스에 붙은 파일 하나(asset).</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>
/// 릴리스에 붙은 파일 중 "내 설치 형태에 맞는 것"을 고르고, SHA256SUMS.txt 로 무결성을 확인하는 규칙.
/// 네트워크·Windows 를 건드리지 않는 순수 로직이라 단위 테스트로 검사한다(파일 이름 규칙은 build.yml 과 짝을 맞춰야 한다).
/// </summary>
public static class UpdatePackage
{
    /// <summary>릴리스에 함께 올라가는 체크섬 목록 파일. "&lt;sha256&gt;␣␣&lt;파일 이름&gt;" 줄의 모음.</summary>
    public const string SumsFileName = "SHA256SUMS.txt";
    /// <summary>설치 프로그램 이름. 정식 릴리스는 버전이 붙고(ImeBadge-Setup-1.2.3.exe), 롤링 사전 릴리스는 붙지 않는다.</summary>
    public const string InstallerPrefix = "ImeBadge-Setup";
    public const string SelfContainedName = "ImeBadge-win-x64-selfcontained.exe";
    public const string FrameworkName = "ImeBadge-win-x64.exe";

    /// <summary>내려받기 상한. 이보다 큰 파일은 받지 않는다(self-contained exe 가 약 19MB, 설치 프로그램이 약 19MB).</summary>
    public const long MaxAssetBytes = 200L * 1024 * 1024;
    /// <summary>SHA256SUMS.txt 내려받기 상한. 몇 줄짜리 텍스트라 아주 작다.</summary>
    public const int MaxSumsBytes = 64 * 1024;

    /// <summary>이 설치 형태가 받아야 할 파일. 릴리스에 그 파일이 없으면 null(그때는 다운로드 페이지를 여는 쪽으로 물러난다).</summary>
    public static ReleaseAsset? Pick(IEnumerable<ReleaseAsset>? assets, UpdateFlavor flavor)
    {
        if (assets is null) return null;
        ReleaseAsset? versioned = null, bare = null, exact = null;
        foreach (var a in assets)
        {
            if (a is null || string.IsNullOrWhiteSpace(a.Name) || !SafeUrl.IsGitHubHttps(a.Url)) continue;
            if (a.Size > MaxAssetBytes) continue;
            switch (flavor)
            {
                case UpdateFlavor.Installer:
                    if (!a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) break;
                    // "ImeBadge-Setup-1.2.3.exe"(정식) 를 "ImeBadge-Setup.exe"(롤링) 보다 앞세운다.
                    if (a.Name.StartsWith(InstallerPrefix + "-", StringComparison.OrdinalIgnoreCase)) versioned ??= a;
                    else if (a.Name.Equals(InstallerPrefix + ".exe", StringComparison.OrdinalIgnoreCase)) bare ??= a;
                    break;
                case UpdateFlavor.SelfContainedExe:
                    if (a.Name.Equals(SelfContainedName, StringComparison.OrdinalIgnoreCase)) exact ??= a;
                    break;
                case UpdateFlavor.FrameworkExe:
                    if (a.Name.Equals(FrameworkName, StringComparison.OrdinalIgnoreCase)) exact ??= a;
                    break;
            }
        }
        return flavor == UpdateFlavor.Installer ? versioned ?? bare : exact;
    }

    /// <summary>체크섬 목록 파일(SHA256SUMS.txt).</summary>
    public static ReleaseAsset? Sums(IEnumerable<ReleaseAsset>? assets)
    {
        if (assets is null) return null;
        foreach (var a in assets)
            if (a is not null && SumsFileName.Equals(a.Name, StringComparison.OrdinalIgnoreCase)
                && SafeUrl.IsGitHubHttps(a.Url) && a.Size <= MaxSumsBytes)
                return a;
        return null;
    }

    /// <summary>
    /// SHA256SUMS.txt 를 파일 이름 → 소문자 해시 로 읽는다. 형식은 sha256sum 과 같은 "&lt;해시&gt; &lt;공백&gt; [*]&lt;이름&gt;".
    /// 해시가 64자리 16진수가 아닌 줄은 버린다.
    /// </summary>
    public static Dictionary<string, string> ParseSums(string? text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return map;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.AsSpan().Trim();
            if (line.IsEmpty || line[0] == '#') continue;
            int sp = line.IndexOfAny(' ', '\t');
            if (sp <= 0) continue;
            var hash = line[..sp];
            if (!IsSha256Hex(hash)) continue;
            var name = line[sp..].Trim();
            if (!name.IsEmpty && name[0] == '*') name = name[1..].Trim();   // 이진 모드 표시
            if (name.IsEmpty) continue;
            map[name.ToString()] = hash.ToString().ToLowerInvariant();
        }
        return map;
    }

    /// <summary>이 파일 이름에 대해 기대되는 해시. 목록에 없으면 null(검증할 수 없으므로 적용하지 않는다).</summary>
    public static string? ExpectedHash(string? sumsText, string? assetName) =>
        !string.IsNullOrWhiteSpace(assetName) && ParseSums(sumsText).TryGetValue(assetName, out var h) ? h : null;

    /// <summary>두 해시가 같은가(대소문자 무시). 둘 중 하나라도 비어 있으면 false.</summary>
    public static bool HashMatches(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(actual)
        && expected.Trim().Equals(actual.Trim(), StringComparison.OrdinalIgnoreCase);

    static bool IsSha256Hex(ReadOnlySpan<char> s)
    {
        if (s.Length != 64) return false;
        foreach (char c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        return true;
    }

    /// <summary>
    /// 내려받은 파일을 놓아 둘 이름. 릴리스 태그를 같이 붙여 두면 남아 있는 파일을 보고 어느 버전인지 알 수 있다.
    /// 태그·파일 이름에 경로 문자가 섞여 들어오더라도 폴더를 벗어나지 못하게 안전한 글자만 남긴다.
    /// </summary>
    public static string StagedFileName(string? tag, string assetName)
    {
        string name = Sanitize(assetName);
        if (name.Length == 0) name = "download.exe";
        string t = Sanitize(tag);
        return t.Length == 0 ? name : t + "-" + name;
    }

    static string Sanitize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var chars = new List<char>(s.Length);
        foreach (char c in s.Trim())
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') chars.Add(c);
        // 앞쪽 점은 숨김 파일·상위 폴더(..)로 읽히지 않게 떼어 낸다.
        int i = 0;
        while (i < chars.Count && chars[i] == '.') i++;
        return new string(chars.ToArray(), i, chars.Count - i);
    }
}
