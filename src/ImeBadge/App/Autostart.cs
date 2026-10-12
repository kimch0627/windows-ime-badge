using System;
using Microsoft.Win32;

namespace ImeBadge;

/// <summary>
/// 로그인 시 자동 시작. HKCU\...\Run 에 exe 경로를 적는 가장 단순한 방식이라 관리자 권한이 필요 없고,
/// 제거 프로그램(Inno Setup)이 같은 값을 지운다.
/// </summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = AppInfo.ProductName;

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch { return false; }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) k.SetValue(ValueName, Command);
            else k.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Write($"autostart {(enabled ? "on" : "off")}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("autostart change failed", ex);
            return false;
        }
    }

    /// <summary>Run 키에 적힌 명령 그대로(꺼져 있으면 null). 설정 창의 [되돌리기]가 원래 값을 그대로 되살리는 데 쓴다.</summary>
    public static string? ReadCommand()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string s && s.Length > 0 ? s : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// <see cref="ReadCommand"/> 로 읽어 둔 값으로 되돌린다. 켜는 쪽도 지금 exe 경로가 아니라 원래 명령을 다시 적는다:
    /// 설치본 경로가 적혀 있던 PC 에서 개발 빌드 창으로 껐다가 되돌려도 설치본 경로가 남는다(<see cref="RefreshIfEnabled"/> 와 같은 이유).
    /// </summary>
    public static bool Restore(string? command)
    {
        if (command is null) return Set(false);
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            k.SetValue(ValueName, command);
            Log.Write("autostart restored");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("autostart restore failed", ex);
            return false;
        }
    }

    /// <summary>
    /// 켜져 있는데 exe 가 다른 곳으로 옮겨졌으면(업데이트·재설치) 경로를 현재 exe 로 고쳐 둔다.
    /// 개발 빌드에서는 부르지 않는다(Program). 설치본을 쓰는 개발자가 개발 빌드를 한 번 띄우기만 해도 경로가 바뀌기 때문이다.
    /// </summary>
    public static void RefreshIfEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (k?.GetValue(ValueName) is string s && s.Length > 0 && !string.Equals(s, Command, StringComparison.OrdinalIgnoreCase))
            {
                k.SetValue(ValueName, Command);
                Log.Write($"autostart path refreshed: {s} -> {Command}");
            }
        }
        catch (Exception ex) { Log.Error("autostart refresh failed", ex); }
    }
}
