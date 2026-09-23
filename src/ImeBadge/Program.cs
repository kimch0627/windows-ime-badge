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

        // 자동 업그레이드의 마지막 단계. 새로 받은 exe 가 이 모드로 실행되어 옛 exe 자리를 차지하고
        // 그 자리의 exe 를 다시 띄운다. 창도 트레이 아이콘도 만들지 않으므로 중복 실행 검사보다 앞에 둔다.
        if (Updater.Has(args, Updater.ApplySwitch))
        {
            ApplicationConfiguration.Initialize();   // 실패를 알리는 대화상자만 쓴다(창은 만들지 않는다)
            Updater.RunApply(args);
            Log.Write("=== exit (apply-update) ===");
            return;
        }

        // 개발용: 배지 시안 그림(PNG)만 저장하고 끝낸다. 창·트레이를 만들지 않으므로 떠 있는 인스턴스와 상관없다(build.yml 이 CI 에서 실행).
        if (RenderSheet.TryRun(args))
        {
            Log.Write("=== exit (render-sheet) ===");
            return;
        }

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

            Updater.CleanStaging(paths);   // 지난 업그레이드가 남긴 설치 파일 치우기

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
