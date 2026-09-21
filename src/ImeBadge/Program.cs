using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ImeBadge;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Log.Enabled = args.Contains("--debug");
        var paths = AppPaths.Default();
        Log.Configure(paths);
        Log.Write($"=== {AppInfo.ProductName} {AppVersion.Display} start ===");

        // 이미 떠 있으면 그쪽에 설정 창을 열라고 알리고 끝낸다.
        using var single = new SingleInstance();
        if (!single.IsFirst)
        {
            Log.Write("another instance is running; forwarding");
            SingleInstance.NotifyExisting();
            return;
        }

        CrashHandler.Install();
        ApplicationConfiguration.Initialize();

        var store = new SettingsStore(paths);
        bool firstRun = !File.Exists(store.FilePath) && !(store.LegacyFilePath is { } legacy && File.Exists(legacy));
        var settings = store.Load();
        Autostart.RefreshIfEnabled();

        Application.Run(new BadgeForm(settings, store, paths, firstRun));
        Log.Write("=== exit ===");
    }
}
