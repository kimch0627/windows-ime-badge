using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImeBadge;

public enum BadgeStyle { Box, Pill, Dot, Underline, DotFlash }
public enum BadgePlacement { AboveRight, BelowRight, AboveLeft, BelowLeft }

/// <summary>
/// 사용자 설정. JSON 으로 저장된다(<see cref="SettingsStore"/>).
/// 새 항목을 더할 때는 기본값을 반드시 주어야 예전 설정 파일도 그대로 읽힌다.
/// </summary>
public sealed class Settings
{
    // ── 모양 ──
    public BadgeStyle Style { get; set; } = BadgeStyle.Pill;
    public BadgePlacement Placement { get; set; } = BadgePlacement.AboveRight;
    public int SizePercent { get; set; } = 100;
    public int OpacityPercent { get; set; } = 100;
    /// <summary>한글 상태 배지 색. "#RRGGBB".</summary>
    public string HangulColor { get; set; } = DefaultHangulColor;
    /// <summary>영문 상태 배지 색. "#RRGGBB".</summary>
    public string EnglishColor { get; set; } = DefaultEnglishColor;
    /// <summary>나타날 때 페이드인, 한/영이 바뀔 때 잠깐 커졌다 작아지는 효과. Windows 의 "애니메이션 효과" 가 꺼져 있으면 무시된다.</summary>
    public bool Animate { get; set; } = true;
    /// <summary>영문 모드에서 Caps Lock 이 켜져 있으면 배지 글자를 "A" 대신 "ABC" 로 보여 준다(트레이 아이콘은 "A" 밑에 줄).</summary>
    public bool ShowCapsLock { get; set; } = true;

    // ── 동작 ──
    /// <summary>활성 창이 모니터 전체를 덮는(게임·전체 화면 동영상) 경우 배지를 숨긴다.</summary>
    public bool HideOnFullscreen { get; set; } = true;
    /// <summary>배지를 띄우지 않을 프로세스 이름 목록. 확장자 없이("mstsc"), 끝에 * 허용("Unreal*").</summary>
    public List<string> ExcludedProcesses { get; set; } = new();
    /// <summary>
    /// caret 을 못 찾아도(자체 커서를 그리는 터미널 등) 포커스 창의 왼쪽 아래 모서리에 배지를 띄울 프로세스 목록.
    /// 모든 앱에 적용하면 작업 표시줄처럼 글자를 입력하지 않는 곳에도 배지가 뜨므로 목록으로 고른다. 형식은 <see cref="ExcludedProcesses"/> 와 같다.
    /// </summary>
    public List<string> CornerBadgeProcesses { get; set; } = new(DefaultCornerBadgeProcesses);

    /// <summary>Xshell 은 MFC 뷰에 커서를 직접 그려 Win32 caret 도 UI Automation 텍스트 정보도 없다.</summary>
    public static readonly string[] DefaultCornerBadgeProcesses = { "Xshell*" };
    /// <summary>단축키로 일시 중지를 켜고 끈다.</summary>
    public bool HotkeyEnabled { get; set; } = true;
    /// <summary>일시 중지 단축키. "Ctrl+Alt+H" 형식(<see cref="HotkeySpec"/>). 읽을 수 없으면 기본값으로 돌아간다.</summary>
    public string Hotkey { get; set; } = HotkeySpec.Default.ToString();
    /// <summary>폴링 주기(ms). 50~1000.</summary>
    public int PollIntervalMs { get; set; } = 100;
    /// <summary>트레이 아이콘에 현재 한/영 상태를 보여 준다("한"/"A"). 끄면 항상 기본 아이콘.</summary>
    public bool TrayShowsState { get; set; } = true;
    /// <summary>설정 창·트레이 메뉴·알림의 언어. Auto 면 Windows 표시 언어를 따른다(<see cref="Strings"/>).</summary>
    public UiLanguage Language { get; set; } = UiLanguage.Auto;

    // ── 업데이트 ──
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }
    /// <summary>"이 버전 건너뛰기"를 누른 릴리스 태그("v1.2.3"). 이 버전은 자동 알림을 띄우지 않는다.</summary>
    public string? SkippedUpdateTag { get; set; }

    /// <summary>Windows 11 기본 강조색. 흰 글자와의 대비가 5.7:1 로 WCAG AA(4.5:1)를 여유 있게 넘는다.</summary>
    public const string DefaultHangulColor = "#0067C0";
    public const string DefaultEnglishColor = "#3C3C3C";

    /// <summary>범위를 벗어난 값을 안전한 값으로 되돌린다. 손으로 고친 설정 파일을 방어한다.</summary>
    public void Normalize()
    {
        SizePercent = Math.Clamp(SizePercent, 50, 300);
        OpacityPercent = Math.Clamp(OpacityPercent, 30, 100);
        PollIntervalMs = Math.Clamp(PollIntervalMs, 50, 1000);
        if (!Enum.IsDefined(Style)) Style = BadgeStyle.Pill;
        if (!Enum.IsDefined(Placement)) Placement = BadgePlacement.AboveRight;
        if (!Enum.IsDefined(Language)) Language = UiLanguage.Auto;
        if (!ColorHex.TryParse(HangulColor, out _)) HangulColor = DefaultHangulColor;
        if (!ColorHex.TryParse(EnglishColor, out _)) EnglishColor = DefaultEnglishColor;
        Hotkey = HotkeySpec.TryParse(Hotkey, out var hk) ? hk.ToString() : HotkeySpec.Default.ToString();
        ExcludedProcesses ??= new();
        ExcludedProcesses.RemoveAll(string.IsNullOrWhiteSpace);
        CornerBadgeProcesses ??= new();
        CornerBadgeProcesses.RemoveAll(string.IsNullOrWhiteSpace);
    }

    public Settings Clone()
    {
        var c = (Settings)MemberwiseClone();
        c.ExcludedProcesses = new List<string>(ExcludedProcesses);
        c.CornerBadgeProcesses = new List<string>(CornerBadgeProcesses);
        return c;
    }

    public void CopyFrom(Settings other)
    {
        Style = other.Style; Placement = other.Placement;
        SizePercent = other.SizePercent; OpacityPercent = other.OpacityPercent;
        HangulColor = other.HangulColor; EnglishColor = other.EnglishColor; Animate = other.Animate; ShowCapsLock = other.ShowCapsLock;
        HideOnFullscreen = other.HideOnFullscreen;
        ExcludedProcesses = new List<string>(other.ExcludedProcesses);
        CornerBadgeProcesses = new List<string>(other.CornerBadgeProcesses);
        HotkeyEnabled = other.HotkeyEnabled; Hotkey = other.Hotkey; PollIntervalMs = other.PollIntervalMs; TrayShowsState = other.TrayShowsState;
        Language = other.Language;
        CheckForUpdates = other.CheckForUpdates; LastUpdateCheckUtc = other.LastUpdateCheckUtc;
        SkippedUpdateTag = other.SkippedUpdateTag;
    }
}

/// <summary>
/// JSON 직렬화 코드를 컴파일 시점에 생성(source generator)한다. 리플렉션 기반 직렬화는 트리밍(trimming)하면
/// 프로퍼티가 잘려 나가거나 아예 꺼지므로(IsReflectionEnabledByDefault=false) 쓰면 안 된다.
/// 열거형(enum)은 예전 설정 파일과 호환되도록 계속 이름 문자열("Pill")로 저장한다.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Settings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext { }

/// <summary>설정 파일을 읽고 쓴다. 예전 위치(exe 옆)의 파일이 있으면 첫 실행 때 새 위치로 옮겨 온다.</summary>
public sealed class SettingsStore
{
    public string FilePath { get; }
    public string? LegacyFilePath { get; }

    public SettingsStore(string filePath, string? legacyFilePath = null)
    {
        FilePath = filePath;
        LegacyFilePath = legacyFilePath;
    }

    public SettingsStore(AppPaths paths) : this(paths.SettingsFile, paths.LegacySettingsFile) { }

    public Settings Load()
    {
        var s = TryRead(FilePath);
        if (s is null && LegacyFilePath is not null && File.Exists(LegacyFilePath))
        {
            s = TryRead(LegacyFilePath);
            if (s is not null)
            {
                Log.Write($"settings migrated from {LegacyFilePath}");
                Save(s);   // 새 위치에 복사해 둔다. 예전 파일은 지우지 않는다(Program Files 는 지울 권한이 없을 수 있음).
            }
        }
        s ??= new Settings();
        s.Normalize();
        return s;
    }

    public bool Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings));
            File.Move(tmp, FilePath, overwrite: true);   // 쓰다 말고 꺼져도 반쪽짜리 파일이 남지 않게
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("settings save failed", ex);
            return false;
        }
    }

    static Settings? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.Settings);
        }
        catch (Exception ex)
        {
            Log.Error($"settings load failed ({path})", ex);
            return null;
        }
    }
}

/// <summary>"#RRGGBB" / "#AARRGGBB" 문자열과 ARGB 정수 사이 변환. System.Drawing 없이 쓰려고 따로 둔다.</summary>
public static class ColorHex
{
    public static bool TryParse(string? text, out int argb)
    {
        argb = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var span = text.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#') span = span[1..];
        if (span.Length != 6 && span.Length != 8) return false;
        if (!uint.TryParse(span, System.Globalization.NumberStyles.HexNumber, null, out uint v)) return false;
        if (span.Length == 6) v |= 0xFF000000;
        argb = unchecked((int)v);
        return true;
    }

    public static string ToHex(int argb) => "#" + (argb & 0xFFFFFF).ToString("X6");

    /// <summary>
    /// WCAG 상대 휘도(relative luminance). 0(검정)~1(흰색). 사람 눈이 느끼는 밝기라서 단순 RGB 평균과 다르다.
    /// 예: 순수 파랑 #0000FF 는 0.07 로 아주 어둡고, 순수 초록 #00FF00 은 0.72 로 밝다.
    /// </summary>
    public static double RelativeLuminance(int argb)
    {
        static double Channel(int v)
        {
            double c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel((argb >> 16) & 0xFF) + 0.7152 * Channel((argb >> 8) & 0xFF) + 0.0722 * Channel(argb & 0xFF);
    }

    /// <summary>WCAG 대비율(contrast ratio). 1(같음)~21(흰/검). 본문 글자는 4.5 이상이 권장(AA).</summary>
    public static double ContrastRatio(int a, int b)
    {
        double la = RelativeLuminance(a), lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// 이 배경색 위에 글자를 놓을 때 흰 글자가 나으면 true, 검은 글자가 나으면 false. 사용자가 고른 어떤 색에도 글자가 보이게 한다.
    /// 대비율을 단순 비교하면 중간 톤 파랑(#0078D7)에서 검은 글자가 아주 근소하게 이기지만 실제로는 흰 글자가 더 잘 읽힌다.
    /// 그래서 휘도 임계값을 쓴다. 0.36 은 Windows 강조색 규칙과 맞는 값이다(파랑·빨강·청록 → 흰 글자, 노랑·금색 → 검은 글자).
    /// </summary>
    public static bool PrefersWhiteText(int argb) => RelativeLuminance(argb) < 0.36;
}
