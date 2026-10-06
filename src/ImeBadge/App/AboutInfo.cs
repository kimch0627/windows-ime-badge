using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>설정 창의 "정보" 페이지와 트레이 메뉴·오류 대화상자가 함께 쓰는 도우미: 웹 페이지·폴더 열기, 진단 정보.</summary>
static class AboutInfo
{
    /// <summary>버그 신고에 필요한 환경 요약. 개인 정보(사용자 이름이 든 경로)는 %APPDATA% 식으로 줄인다.</summary>
    public static string Diagnostics(AppPaths paths, Settings settings, int dpi)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppInfo.ProductName} {AppVersion.Display}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine(Strings.Format("diag.dpi", dpi, dpi * 100 / 96, Screen.AllScreens.Length));
        sb.AppendLine(Strings.Format("diag.theme",
            Strings.Get(Theme.IsDark ? "diag.dark" : "diag.light"),
            SystemInformation.HighContrast ? Strings.Get("diag.highContrast") : "",
            Strings.Get(Native.AnimationsEnabled() ? "diag.on" : "diag.off"),
            $"{Strings.Code} ({settings.Language})"));
        sb.AppendLine(Strings.Format("diag.settings", settings.Style, settings.Placement, settings.SizePercent, settings.OpacityPercent,
            settings.PollIntervalMs, settings.HotkeyEnabled ? settings.Hotkey : Strings.Get("diag.off"), settings.ExcludedProcesses.Count));
        sb.AppendLine(Strings.Format("diag.experimental", Experiments(settings)));
        sb.AppendLine(Strings.Format("diag.exe", Shorten(Environment.ProcessPath)));
        sb.AppendLine(Strings.Format("diag.settingsFile", Shorten(paths.SettingsFile)));
        sb.AppendLine(Strings.Format("diag.logDir", Shorten(paths.LogDir)));
        return sb.ToString();
    }

    /// <summary>켜 둔 실험 기능. 이슈에서 "실험 기능 때문인가" 를 바로 가릴 수 있게 영문 이름으로 적는다.</summary>
    static string Experiments(Settings s)
    {
        var on = new List<string>();
        if (s.CaretSonar) on.Add($"sonar({s.CaretSonarHotkey}{(s.CaretSonarOnSwitch ? ", on switch" : "")})");
        if (s.RememberFieldMode) on.Add("field memory");
        if (s.FocusStealWarning) on.Add("focus steal");
        if (s.ShowSelection) on.Add("selection");
        return on.Count == 0 ? Strings.Get("diag.none") : string.Join(", ", on);
    }

    static string Shorten(string? path)
    {
        if (string.IsNullOrEmpty(path)) return Strings.Get("diag.unknown");
        foreach (var (name, folder) in new[]
        {
            ("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            ("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            ("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        })
            if (folder.Length > 0 && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                return name + path[folder.Length..];
        return path;
    }

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("open url failed", ex); }
    }

    public static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("open folder failed", ex); }
    }
}
