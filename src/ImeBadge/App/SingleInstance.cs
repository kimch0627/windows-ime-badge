using System;
using System.Text;
using System.Threading;

namespace ImeBadge;

/// <summary>
/// 중복 실행 방지. 이름 있는 뮤텍스(named mutex)를 먼저 잡은 프로세스만 실행을 이어 간다.
/// 두 번째로 뜬 프로세스는 첫 프로세스에게 "설정 창을 열어 달라"는 창 메시지만 보내고 조용히 끝난다.
/// (자동 시작 + 바로 가기 더블클릭이 겹쳐도 배지가 두 개 뜨지 않는다.)
/// </summary>
sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;
    public bool IsFirst { get; }

    /// <summary>RegisterWindowMessage 로 만든 전역 메시지 ID. 같은 이름이면 어느 프로세스에서나 같은 값.</summary>
    public static readonly uint ShowSettingsMessage = Native.RegisterWindowMessage(AppInfo.ShowSettingsMessageName);

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, AppInfo.SingleInstanceMutexName, out bool createdNew);
        IsFirst = createdNew;
    }

    /// <summary>
    /// 이미 떠 있는 인스턴스에게 설정 창을 열라고 알린다.
    /// 배지 창(BadgeForm)은 작업 표시줄에 뜨지 않도록 WinForms 가 다른 숨은 창의 "소유 창"으로 만든다. HWND_BROADCAST 는
    /// 숨겨진 소유 창에는 메시지를 보내지 않아서, 배지가 숨어 있는 동안(글자를 입력하지 않는 대부분의 시간)은 알림이 닿지 않았다.
    /// 그래서 최상위 창을 돌며 이름이 "ImeBadge" 인 WinForms 창(다른 프로세스의 배지 창)을 찾아 직접 보낸다. 못 찾으면 예전처럼 broadcast.
    /// 보내기 전에 그 프로세스에 "창을 앞으로 가져와도 된다"고 허락한다. 바로 가기를 누른 사용자의 권리는 이 두 번째 프로세스가 받았으므로,
    /// 넘겨주지 않으면 떠 있던 인스턴스의 설정 창이 다른 창 뒤에서 열리고 작업 표시줄 단추만 깜빡인다.
    /// </summary>
    public static void NotifyExisting()
    {
        int sent = 0;
        uint self = (uint)Environment.ProcessId;
        var title = new StringBuilder(64);
        Native.EnumWindows((h, _) =>
        {
            title.Clear();
            if (Native.GetWindowText(h, title, title.Capacity) > 0 && title.ToString() == AppInfo.ProductName
                && Native.ClassName(h).StartsWith("WindowsForms10.", StringComparison.Ordinal)
                && Native.GetWindowThreadProcessId(h, out uint pid) != 0 && pid != self
                && AllowForeground(pid)
                && Native.PostMessage(h, ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero))
                sent++;
            return true;
        }, IntPtr.Zero);
        Log.Write($"notify existing: posted to {sent} window(s)");
        if (sent == 0)
        {
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            Native.PostMessage(Native.HWND_BROADCAST, ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>허락이 거절돼도(권리가 없을 때) 메시지는 보낸다. 그래서 늘 true.</summary>
    static bool AllowForeground(uint pid)
    {
        if (!Native.AllowSetForegroundWindow((int)pid)) Log.Write("AllowSetForegroundWindow denied");
        return true;
    }

    public void Dispose()
    {
        try { if (IsFirst) _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
