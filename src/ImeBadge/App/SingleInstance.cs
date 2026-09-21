using System;
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

    /// <summary>이미 떠 있는 인스턴스에게 설정 창을 열라고 알린다.</summary>
    public static void NotifyExisting() =>
        Native.PostMessage(Native.HWND_BROADCAST, ShowSettingsMessage, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        try { if (IsFirst) _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
