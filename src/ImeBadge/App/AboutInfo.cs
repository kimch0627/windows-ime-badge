using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>설정 창의 "정보" 페이지와 트레이 메뉴·오류 대화상자가 함께 쓰는 도우미: 웹 페이지·폴더 열기, 진단 정보.</summary>
static class AboutInfo
{
    /// <summary>버그 신고에 필요한 환경 요약. 개인 정보(사용자 이름이 든 경로)는 %APPDATA% 식으로 줄인다.</summary>
    /// <param name="missed">"배지가 안 뜬 앱" 목록. 있으면 앱 이름과 까닭을 한 줄로 덧붙인다.</param>
    public static string Diagnostics(AppPaths paths, Settings settings, int dpi, IReadOnlyList<MissedApp>? missed = null)
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
        if (missed is not null)
            sb.AppendLine(Strings.Format("diag.missed", !settings.TrackMissedApps ? Strings.Get("diag.off")
                : missed.Count == 0 ? Strings.Get("diag.none")
                : string.Join(", ", missed.Select(m => $"{m.Process}({m.Reason})"))));
        sb.AppendLine(Strings.Format("diag.exe", Shorten(Environment.ProcessPath)));
        sb.AppendLine(Strings.Format("diag.settingsFile", Shorten(paths.SettingsFile)));
        sb.AppendLine(Strings.Format("diag.logDir", Shorten(paths.LogDir)));
        return sb.ToString();
    }

    /// <summary>
    /// "배지가 안 뜬 앱" 보고서(이슈에 붙여 넣는 글). 앱마다 까닭, 한 번도 안 떴는지, 센 순간 수, 마지막 시각, 창 클래스 이름·UI Automation 컨트롤 종류.
    /// 창 제목과 입력한 글은 들어가지 않는다.
    /// </summary>
    public static string MissedReport(IReadOnlyList<MissedApp> apps)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppInfo.ProductName} {AppVersion.Display} — {Strings.Format("missed.report.title", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        if (apps.Count == 0) sb.AppendLine(Strings.Get("diag.none"));
        foreach (var app in apps)
            sb.AppendLine(Strings.Format("missed.report.line", app.Process, Strings.Get("missed.reason." + app.Reason), app.Reason,
                Strings.Get(app.NeverShown ? "missed.never" : "missed.partial"), app.MissedSeconds, app.ShownSeconds,
                app.LastMissed.ToString("HH:mm", CultureInfo.InvariantCulture), app.Detail.Length > 0 ? app.Detail : "-"));
        return sb.ToString();
    }

    /// <summary>켜 둔 실험 기능. 이슈에서 "실험 기능 때문인가" 를 바로 가릴 수 있게 영문 이름으로 적는다.</summary>
    static string Experiments(Settings s)
    {
        var on = new List<string>();
        if (s.CaretSonar) on.Add($"sonar({s.CaretSonarHotkey}{(s.CaretSonarOnSwitch ? ", on switch" : "")})");
        if (s.KeepImeMode) on.Add("keep mode");
        if (s.FocusStealWarning) on.Add("focus steal");
        if (s.ShowSelection) on.Add("selection");
        if (s.PasswordWarning) on.Add("password");
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
