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

    /// <summary>켜져 있는데 exe 가 다른 곳으로 옮겨졌으면(업데이트·재설치) 경로를 현재 exe 로 고쳐 둔다.</summary>
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
