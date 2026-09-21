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
        try
        {
            ApplicationConfiguration.Initialize();

            var store = new SettingsStore(paths);
            bool firstRun = !File.Exists(store.FilePath) && !(store.LegacyFilePath is { } legacy && File.Exists(legacy));
            var settings = store.Load();
            Strings.Setting = settings.Language;   // 그 전(중복 실행 안내·치명적 오류)에는 Windows 표시 언어를 따른다
            Autostart.RefreshIfEnabled();

            Application.Run(new BadgeForm(settings, store, paths, firstRun));
        }
        catch (Exception ex)
        {
            // 시작 중(창 생성 전후)의 예외. 트리밍으로 잘려 나간 형식(TypeLoadException) 같은 문제를 여기서 알린다.
            CrashHandler.Fatal(ex);
        }
        Log.Write("=== exit ===");
    }
}
