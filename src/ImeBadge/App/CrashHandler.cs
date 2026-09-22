using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImeBadge;

/// <summary>
/// 잡히지 않은 예외 처리. 예외 하나 때문에 배지가 소리 없이 사라지는 대신, 오류 로그(errors.log)에 남기고
/// 가능하면 계속 돈다. UI 스레드 예외가 짧은 시간에 반복되면 사용자에게 알리고 종료한다.
/// </summary>
static class CrashHandler
{
    const int MaxUiExceptions = 20;
    static int _uiExceptions;

    public static void Install()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Log.Error("unhandled (UI thread)", e.Exception);
            if (Interlocked.Increment(ref _uiExceptions) >= MaxUiExceptions) Fatal(e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Log.Error("unhandled (fatal)", ex);
            Fatal(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("unobserved task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>오류 로그에 남기고 사용자에게 알린 뒤 종료한다.</summary>
    public static void Fatal(Exception? ex)
    {
        Log.Error("fatal", ex);
        try
        {
            string logDir = AppPaths.Default().LogDir;
            bool open = Dialogs.Fatal(
                Strings.Format("crash.heading", Strings.Get("app.name")),
                Strings.Get("crash.text"),
                ex is null ? null : $"{ex.GetType().Name}: {ex.Message}",
                logDir);
            if (open) AboutForm.OpenFolder(logDir);
        }
        catch { }
        Environment.Exit(1);
    }
}
