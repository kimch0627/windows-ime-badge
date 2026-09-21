using System;
using System.IO;

namespace ImeBadge;

/// <summary>
/// 파일 로그. 두 종류가 있다.
///  - 디버그 로그(imebadge.log): <c>--debug</c> 로 실행했을 때만 기록. 활성 창·caret 탐색 경로·IME 원시 값.
///  - 오류 로그(errors.log): 항상 기록. 예외와 복구 불가 상황. 사용자가 문제를 신고할 때 첨부하는 파일.
/// 파일이 <see cref="MaxBytes"/> 를 넘으면 <c>.1</c> 로 한 번 밀어 두고 새로 쓴다(단순 회전).
/// </summary>
public static class Log
{
    public const long MaxBytes = 1024 * 1024;

    public static bool Enabled;
    static string? _debugPath, _errorPath;
    static string _lastLine = "";
    static readonly object Gate = new();

    public static void Configure(AppPaths paths)
    {
        try { Directory.CreateDirectory(paths.LogDir); } catch { }
        _debugPath = paths.DebugLogFile;
        _errorPath = paths.ErrorLogFile;
    }

    public static void Write(string line)
    {
        if (!Enabled) return;
        Append(_debugPath, line);
    }

    public static void WriteIfChanged(string line)
    {
        if (!Enabled || line == _lastLine) return;
        _lastLine = line;
        Write(line);
    }

    /// <summary>항상 기록되는 오류 로그. 디버그 모드면 디버그 로그에도 남긴다.</summary>
    public static void Error(string message, Exception? ex = null)
    {
        string line = ex is null ? "ERROR " + message : $"ERROR {message}: {ex}";
        Append(_errorPath, line);
        if (Enabled) Append(_debugPath, line);
    }

    static void Append(string? path, string line)
    {
        if (path is null) return;
        lock (Gate)
        {
            try
            {
                Rotate(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            catch { }
        }
    }

    internal static void Rotate(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length < MaxBytes) return;
        string old = path + ".1";
        try { File.Delete(old); } catch { }
        try { File.Move(path, old); } catch { }
    }
}
