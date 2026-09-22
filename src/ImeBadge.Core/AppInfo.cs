using System;
using System.IO;

namespace ImeBadge;

/// <summary>제품 이름·저장소 주소처럼 여러 곳에서 같이 쓰는 상수.</summary>
public static class AppInfo
{
    public const string ProductName = "ImeBadge";
    /// <summary>사용자에게 보이는 이름. 언어를 따르므로 <see cref="Strings"/> 의 "app.name" 을 쓴다.</summary>
    public static string DisplayName => Strings.Get("app.name");
    public const string RepoOwner = "kimch0627";
    public const string RepoName = "windows-ime-badge";
    public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;
    public const string ReleasesUrl = RepoUrl + "/releases";
    public const string LatestReleaseApi = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";

    /// <summary>두 번째 인스턴스가 첫 인스턴스에게 "설정 창을 열어 달라"고 보낼 때 쓰는 창 메시지 이름.</summary>
    public const string ShowSettingsMessageName = "ImeBadge.ShowSettings";

    /// <summary>중복 실행 방지용 뮤텍스 이름. 설치 프로그램(Inno Setup, AppMutex)도 같은 이름으로 실행 중인지 확인한다.</summary>
    public const string SingleInstanceMutexName = "ImeBadge.SingleInstance";
}

/// <summary>
/// 설정·로그 파일이 놓이는 폴더.
/// exe 옆(AppContext.BaseDirectory)은 Program Files 에 설치되면 쓰기 권한이 없어 쓸 수 없다.
/// 설정은 %APPDATA%\ImeBadge (사용자 프로필을 따라다님), 로그는 %LOCALAPPDATA%\ImeBadge\logs (로컬에만).
/// </summary>
public sealed class AppPaths
{
    public string SettingsDir { get; }
    public string LogDir { get; }
    public string SettingsFile => Path.Combine(SettingsDir, "settings.json");
    public string DebugLogFile => Path.Combine(LogDir, "imebadge.log");
    public string ErrorLogFile => Path.Combine(LogDir, "errors.log");

    /// <summary>예전 버전(0.x)이 exe 옆에 남긴 설정 파일. 있으면 첫 실행 때 새 위치로 옮겨 온다.</summary>
    public string? LegacySettingsFile { get; }

    public AppPaths(string settingsDir, string logDir, string? legacySettingsFile = null)
    {
        SettingsDir = settingsDir;
        LogDir = logDir;
        LegacySettingsFile = legacySettingsFile;
    }

    public static AppPaths Default()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        return new AppPaths(
            Path.Combine(roaming, AppInfo.ProductName),
            Path.Combine(local, AppInfo.ProductName, "logs"),
            Path.Combine(AppContext.BaseDirectory, "imebadge.settings.json"));
    }
}
