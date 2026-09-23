using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace ImeBadge;

/// <summary>
/// 자동 업그레이드의 "적용" 담당. 내려받기·검증은 <see cref="UpdateChecker"/> 와 <see cref="UpdatePackage"/> 가 하고,
/// 여기서는 받은 파일을 실제로 갈아 끼운다.
///
/// 두 가지 길이 있다.
///  - 설치본: 새 설치 프로그램을 <c>/SILENT</c> 로 돌린다. 설치가 끝나면 설치 프로그램이 새 exe 를 다시 띄운다(<c>/RESTARTAPP</c>).
///  - 무설치 exe: 실행 중인 exe 는 자기 자신을 덮어쓸 수 없다. 그래서 새로 받은 exe 를
///    <c>--apply-update</c> 모드로 띄워 두고 이쪽이 먼저 끝난다. 새 exe 가 우리가 죽기를 기다렸다가
///    제 몸을 원래 자리에 복사하고 그 자리의 exe 를 실행한다. 비유하면 교대 근무자가 문 앞에서 기다렸다가
///    내가 나간 뒤 들어와 명패를 바꿔 달고 근무를 시작하는 것이다.
/// </summary>
static class Updater
{
    /// <summary>새 exe 를 "옛 exe 자리로 옮겨 놓는 일꾼" 으로 띄울 때 쓰는 명령줄 스위치.</summary>
    public const string ApplySwitch = "--apply-update";
    const string TargetSwitch = "--target";
    const string PidSwitch = "--pid";
    const string RestartSwitch = "--restart";

    /// <summary>Inno Setup 이 만드는 제거 정보 키. AppId 뒤에 _is1 이 붙는다(installer\ImeBadge.iss 의 AppId 와 같아야 한다).</summary>
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7E2C1D7A-9B1E-4E63-9A2B-5F1C0B7D3A21}_is1";

    /// <summary>이 exe 가 놓여 있는 설치 위치. <c>AllUsers</c> 면 모든 사용자용 설치(관리자 권한 필요).</summary>
    public sealed record InstallSite(bool AllUsers, string Dir);

    static InstallSite? _site;
    static bool _siteChecked;

    /// <summary>설치 프로그램으로 설치된 것이면 그 위치, 무설치(압축만 풀어 쓰는) exe 면 null.</summary>
    public static InstallSite? Install
    {
        get
        {
            if (_siteChecked) return _site;
            _siteChecked = true;
            _site = FindInstall();
            Log.Write($"install site: {(_site is null ? "portable" : $"{_site.Dir} (allUsers={_site.AllUsers})")}");
            return _site;
        }
    }

    /// <summary>지금 돌고 있는 프로그램의 형태. 릴리스에서 받을 파일이 이것으로 갈린다.</summary>
    public static UpdateFlavor Flavor => Install is not null ? UpdateFlavor.Installer : PortableFlavor;

    // self-contained 로 publish 할 때만 정의되는 상수(ImeBadge.csproj). 무설치 사용자에게 자기가 쓰던 것과
    // 같은 종류의 exe 를 주기 위한 것이다(런타임 포함 19MB vs .NET 8 런타임이 필요한 2MB).
    static UpdateFlavor PortableFlavor =>
#if SELF_CONTAINED
        UpdateFlavor.SelfContainedExe;
#else
        UpdateFlavor.FrameworkExe;
#endif

    static InstallSite? FindInstall()
    {
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(exeDir)) return null;
        foreach (var (root, allUsers) in new[] { (Registry.CurrentUser, false), (Registry.LocalMachine, true) })
        {
            try
            {
                using var key = root.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string dir && SameDir(dir, exeDir))
                    return new InstallSite(allUsers, Normalize(dir));
            }
            catch (Exception ex) { Log.Error("install location lookup failed", ex); }
        }
        return null;
    }

    static string Normalize(string dir) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));

    static bool SameDir(string? a, string? b)
    {
        try { return a is not null && b is not null && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    // ── 받아 둔 파일 치우기 ──

    /// <summary>지난 업그레이드가 남긴 파일을 지운다(평소 실행 때 한 번). 아직 쓰이는 중이면 조용히 넘어간다.</summary>
    public static void CleanStaging(AppPaths paths)
    {
        try
        {
            if (!Directory.Exists(paths.UpdateDir)) return;
            foreach (var f in Directory.EnumerateFiles(paths.UpdateDir))
                try { File.Delete(f); } catch { }
        }
        catch (Exception ex) { Log.Error("update staging cleanup failed", ex); }
    }

    // ── 적용 ──

    /// <summary>
    /// 검증까지 끝난 <paramref name="stagedPath"/> 를 적용한다. true 면 바깥쪽에서 곧바로 프로그램을 끝내야 한다
    /// (설치 프로그램·일꾼 exe 가 우리가 나가기를 기다리고 있다).
    /// </summary>
    public static bool Apply(string stagedPath, AppPaths paths)
    {
        try
        {
            return Install is { } site ? RunInstaller(stagedPath, site, paths) : ReplaceExe(stagedPath);
        }
        catch (Exception ex)
        {
            Log.Error("update apply failed", ex);
            return false;
        }
    }

    /// <summary>설치본: 새 설치 프로그램을 조용히 돌린다. 설치가 끝나면 설치 프로그램이 새 exe 를 띄운다.</summary>
    static bool RunInstaller(string setupPath, InstallSite site, AppPaths paths)
    {
        string log = Path.Combine(paths.LogDir, "update-setup.log");
        string args = $"/SILENT /SUPPRESSMSGBOXES /NOCANCEL /NORESTART /RESTARTAPP " +
                      $"{(site.AllUsers ? "/ALLUSERS" : "/CURRENTUSER")} /DIR=\"{site.Dir}\" /TASKS=\"{CurrentTasks()}\" /LOG=\"{log}\"";
        // 모든 사용자용 설치는 Program Files 를 건드리므로 권한 상승이 필요하다(UAC 창이 뜬다).
        var psi = new ProcessStartInfo(setupPath, args) { UseShellExecute = true };
        if (site.AllUsers) psi.Verb = "runas";
        Log.Write($"running installer: {setupPath} {args}");
        return Process.Start(psi) is not null;
    }

    /// <summary>무설치 exe: 새 exe 를 일꾼 모드로 띄운다. 그쪽이 우리가 끝나기를 기다렸다가 자리를 바꾼다.</summary>
    static bool ReplaceExe(string newExePath)
    {
        string? target = Environment.ProcessPath;
        if (string.IsNullOrEmpty(target)) { Log.Error("update apply: ProcessPath unknown"); return false; }
        string args = $"{ApplySwitch} {TargetSwitch} \"{target}\" {PidSwitch} {Environment.ProcessId} {RestartSwitch}";
        var psi = new ProcessStartInfo(newExePath, args) { UseShellExecute = true };
        // exe 가 쓸 수 없는 폴더(Program Files 등)에 있으면 권한 상승이 필요하다.
        if (!DirWritable(Path.GetDirectoryName(target)!)) psi.Verb = "runas";
        Log.Write($"launching updater helper: {newExePath} {args} (elevated={psi.Verb == "runas"})");
        return Process.Start(psi) is not null;
    }

    /// <summary>
    /// 조용한 설치는 "추가 작업"(자동 시작·바탕 화면 바로 가기)을 기본값으로 되돌린다. 지금 상태를 그대로 넘겨
    /// 사용자가 꺼 둔 것이 업그레이드로 되살아나지 않게 한다(빈 문자열이면 아무 작업도 고르지 않는다).
    /// </summary>
    static string CurrentTasks()
    {
        var tasks = new List<string>(2);
        if (Autostart.IsEnabled()) tasks.Add("autostart");
        if (DesktopShortcutExists()) tasks.Add("desktopicon");
        return string.Join(",", tasks);
    }

    static bool DesktopShortcutExists()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
        {
            try
            {
                string dir = Environment.GetFolderPath(folder);
                if (dir.Length > 0 && File.Exists(Path.Combine(dir, AppInfo.ProductName + ".lnk"))) return true;
            }
            catch (Exception ex) { Log.Error("desktop shortcut lookup failed", ex); }
        }
        return false;
    }

    static bool DirWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".imebadge-write-{Environment.ProcessId}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch { return false; }
    }

    // ── 일꾼 모드(--apply-update) ──

    /// <summary>
    /// 새로 받은 exe 가 <c>--apply-update</c> 로 실행되었을 때의 전부. 옛 프로세스가 끝나기를 기다렸다가
    /// 자기 자신을 옛 exe 자리에 복사하고, 그 자리의 exe 를 다시 띄운다. UI 는 실패했을 때만 보여 준다.
    /// </summary>
    public static void RunApply(string[] args)
    {
        // 이 모드에는 예외 처리기(CrashHandler)가 없다. 여기서 막지 못한 예외는 아무 흔적 없이 사라지므로 직접 감싼다.
        try { TakeOver(args); }
        catch (Exception ex) { Log.Error("apply-update failed", ex); }
    }

    /// <summary>옛 프로세스를 기다렸다가 그 자리를 차지하고 다시 띄우는 본체.</summary>
    static void TakeOver(string[] args)
    {
        string? target = Value(args, TargetSwitch);
        bool restart = Has(args, RestartSwitch);
        int pid = int.TryParse(Value(args, PidSwitch), out int p) ? p : 0;
        string? self = Environment.ProcessPath;
        Log.Write($"apply-update: target={target} pid={pid} restart={restart} self={self}");

        if (string.IsNullOrEmpty(self)) { Log.Error("apply-update: ProcessPath unknown"); return; }
        // 우리 자신이 넘긴 값이지만, 엉뚱한 파일을 덮어쓰지 않도록 "있는 ImeBadge.exe 를 가리키는 절대 경로" 만 받는다.
        if (string.IsNullOrEmpty(target) || !Path.IsPathFullyQualified(target)
            || !string.Equals(Path.GetFileName(target), AppInfo.ProductName + ".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(target))
        {
            Log.Error($"apply-update: bad target '{target}'");
            return;
        }

        WaitForExit(pid);
        if (!Swap(self, target))
        {
            // 갈아 끼우지 못했다. 옛 버전은 그대로 남아 있으니 사용자에게 알리고 손으로 받게 한다.
            Dialogs.Warning(Strings.Get("update.apply.failed"), Strings.Get("update.apply.failed.text"), target);
            if (restart) Start(target);
            return;
        }
        Log.Write("apply-update: replaced");
        if (restart) Start(target);
    }

    static void WaitForExit(int pid)
    {
        if (pid <= 0) return;
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.WaitForExit(30_000)) Log.Error($"apply-update: pid {pid} still running after 30s");
        }
        catch (ArgumentException) { }   // 이미 끝났다
        catch (Exception ex) { Log.Error("apply-update: wait failed", ex); }
    }

    /// <summary>
    /// 옛 exe 를 .old 로 밀어 두고 그 자리에 자신을 복사한다. 복사가 실패하면 밀어 둔 파일을 되돌려
    /// 업그레이드 전 상태로 남긴다("반쪽짜리 exe" 가 생기지 않게).
    /// </summary>
    static bool Swap(string self, string target)
    {
        string backup = target + ".old";
        try { File.Delete(backup); } catch { }
        bool moved = false;
        for (int i = 0; i < 40 && !moved; i++)   // 옛 프로세스가 완전히 사라질 때까지 최대 10초
        {
            try { File.Move(target, backup); moved = true; }
            catch (Exception ex)
            {
                if (i == 39) { Log.Error("apply-update: move aside failed", ex); break; }
                Thread.Sleep(250);
            }
        }
        if (!moved) return false;
        try
        {
            File.Copy(self, target, overwrite: true);
            try { File.Delete(backup); } catch { }   // 잠겨 있으면 다음 실행 때 사라진다
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("apply-update: copy failed", ex);
            try { File.Move(backup, target, overwrite: true); } catch (Exception ex2) { Log.Error("apply-update: rollback failed", ex2); }
            return false;
        }
    }

    /// <summary>
    /// 새 버전을 띄운다. 권한 상승된 상태(쓸 수 없는 폴더의 exe 를 바꾸느라 UAC 를 거친 경우)에서 그냥 실행하면
    /// 새 프로그램도 관리자 권한으로 돌아간다. 그럴 때는 explorer 에 부탁해 로그인 사용자의 보통 권한으로 띄운다
    /// (설치본 쪽은 설치 프로그램의 runasoriginaluser 가 같은 일을 한다).
    /// </summary>
    static void Start(string exe)
    {
        if (IsElevated())
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = true });
                return;
            }
            catch (Exception ex) { Log.Error("apply-update: restart via explorer failed", ex); }
        }
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! }); }
        catch (Exception ex) { Log.Error("apply-update: restart failed", ex); }
    }

    static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) { Log.Error("elevation check failed", ex); return false; }
    }

    public static bool Has(string[] args, string name)
    {
        foreach (var a in args) if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static string? Value(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
