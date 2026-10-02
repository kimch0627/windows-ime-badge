using System;
using System.Threading;

namespace ImeBadge;

/// <summary>
/// 오래 걸릴 수 있는 일을 전용 스레드에서 돌리고, 부르는 쪽(UI 스레드)은 정해진 시간까지만 기다린다.
/// 앱에서는 다른 앱에 묻는 접근성 호출(UI Automation·MSAA)로 caret 을 찾는 데 쓴다. 이 호출들은 대상 앱의 UI 스레드가 답해야 끝나서,
/// 그 앱이 바쁘면 호출 하나가 UI Automation 기본 제한(20초)까지 기다리고 caret 을 한 번 찾는 데 여러 번 불러 몇 분이 될 수 있다.
/// UI 스레드에서 바로 부르면 그동안 배지·트레이 메뉴가 모두 멈춘다.
/// <list type="bullet">
/// <item>시간 안에 끝나면 결과(<see cref="Outcome.Done"/>).</item>
/// <item>넘기면 <see cref="Outcome.TimedOut"/>. 그 일은 스레드에서 끝까지 돌고 결과는 버린다.</item>
/// <item>앞의 일이 아직 끝나지 않았으면 새 일을 쌓지 않고 기다리지도 않고 바로 <see cref="Outcome.Busy"/>.</item>
/// <item>한 일이 <c>abandonAfterMs</c> 넘게 묶여 있으면 그 스레드는 버리고 새 스레드에서 일한다(다른 앱으로 옮겨도 다시 찾게).
///   버린 스레드는 일이 끝나면 스스로 끝난다. 버려 둔 채 아직 끝나지 않은 스레드는 <c>maxAbandoned</c> 개까지만 둔다.</item>
/// </list>
/// <see cref="Run"/> 은 한 스레드에서만 부른다고 가정한다. 시각·기다리기·스레드 설정은 바꿔 넣을 수 있다(테스트, STA 에서 메시지를
/// 펌프하지 않는 기다리기, MTA 설정).
/// </summary>
public sealed class DeadlineWorker<T>
{
    public enum Outcome { Done, TimedOut, Busy }

    readonly string _name;
    readonly int _waitMs, _abandonAfterMs, _maxAbandoned;
    readonly Func<long> _clock;
    readonly Func<WaitHandle, int, bool> _wait;
    readonly Action<Thread>? _configure;
    Lane? _lane;
    int _abandoned;

    /// <param name="name">스레드 이름(디버거·덤프에서 알아보게).</param>
    /// <param name="waitMs">한 번에 기다리는 최대 시간.</param>
    /// <param name="abandonAfterMs">이보다 오래 묶인 스레드는 버리고 새로 만든다.</param>
    /// <param name="maxAbandoned">버려 둔 채 아직 끝나지 않은 스레드의 최대 수. 다 차면 새로 만들지 않고 <see cref="Outcome.Busy"/>.</param>
    /// <param name="clock">단조 증가 시각(ms). 기본 <c>Environment.TickCount64</c>.</param>
    /// <param name="wait">핸들을 최대 ms 만큼 기다려 신호를 받았으면 true. 기본 <c>WaitHandle.WaitOne</c>.</param>
    /// <param name="configure">새 스레드를 시작하기 전에 부른다(예: MTA 로 설정).</param>
    public DeadlineWorker(string name, int waitMs, int abandonAfterMs, int maxAbandoned, Func<long>? clock = null,
                          Func<WaitHandle, int, bool>? wait = null, Action<Thread>? configure = null)
    {
        _name = name;
        _waitMs = waitMs;
        _abandonAfterMs = abandonAfterMs;
        _maxAbandoned = maxAbandoned;
        _clock = clock ?? (() => Environment.TickCount64);
        _wait = wait ?? ((h, ms) => h.WaitOne(ms));
        _configure = configure;
    }

    /// <summary>지금 일이 끝나지 않은 스레드가 있는가.</summary>
    public bool IsBusy => _lane?.Busy ?? false;

    /// <summary>버려 둔 채 아직 끝나지 않은 스레드 수.</summary>
    public int AbandonedThreads => Volatile.Read(ref _abandoned);

    /// <summary><paramref name="job"/> 을 작업 스레드에서 돌리고 최대 waitMs 기다린다. <see cref="Outcome.Done"/> 일 때만 <paramref name="result"/> 가 의미 있다.</summary>
    public Outcome Run(Func<T> job, out T? result)
    {
        result = default;
        if (_lane is { Busy: true } stuck)
        {
            if (_clock() - stuck.Started < _abandonAfterMs || Volatile.Read(ref _abandoned) >= _maxAbandoned) return Outcome.Busy;
            if (stuck.Abandon()) _lane = null;   // false 면 그사이 끝났다 → 그 스레드를 그대로 쓴다
        }
        var lane = _lane ??= new Lane(this);
        lane.Start(job, _clock());
        if (!_wait(lane.Done, _waitMs) && lane.MarkLate()) return Outcome.TimedOut;
        result = lane.Result;
        return Outcome.Done;
    }

    /// <summary>작업 스레드 하나. 일을 하나씩 받아 돌리고, 버려졌으면 그 일을 마친 뒤 끝난다.</summary>
    sealed class Lane
    {
        readonly DeadlineWorker<T> _owner;
        readonly object _gate = new();
        readonly AutoResetEvent _go = new(false);
        public readonly ManualResetEvent Done = new(true);
        Func<T>? _job;
        bool _busy, _late, _abandoned;
        public T? Result;
        public long Started;

        public Lane(DeadlineWorker<T> owner)
        {
            _owner = owner;
            var thread = new Thread(Loop) { IsBackground = true, Name = owner._name };
            owner._configure?.Invoke(thread);
            thread.Start();
        }

        public bool Busy { get { lock (_gate) return _busy; } }

        public void Start(Func<T> job, long now)
        {
            lock (_gate)
            {
                _job = job;
                _busy = true;
                _late = false;
                Result = default;
                Started = now;
                Done.Reset();
            }
            _go.Set();
        }

        /// <summary>기다리다 시간을 넘겼다고 표시한다. 그사이 끝났으면 false(결과를 쓸 수 있다).</summary>
        public bool MarkLate()
        {
            lock (_gate)
            {
                if (!_busy) return false;
                _late = true;
                return true;
            }
        }

        /// <summary>묶인 스레드를 버린다. 그사이 끝났으면 false(계속 쓸 수 있다).</summary>
        public bool Abandon()
        {
            lock (_gate)
            {
                if (!_busy) return false;
                _abandoned = true;
                Interlocked.Increment(ref _owner._abandoned);   // 스레드가 _abandoned 를 읽기 전에 센다(빼기가 먼저 일어나지 않게)
                return true;
            }
        }

        void Loop()
        {
            while (true)
            {
                _go.WaitOne();
                Func<T>? job;
                lock (_gate) job = _job;
                T? result = default;
                try { if (job is not null) result = job(); }
                catch { }   // 일 안에서 예외를 처리한다. 여기서는 스레드가 죽지 않게만 한다
                bool abandoned;
                lock (_gate)
                {
                    if (!_late) Result = result;   // 늦은 결과는 버린다
                    _busy = false;
                    _job = null;
                    abandoned = _abandoned;
                }
                Done.Set();
                if (abandoned)
                {
                    Interlocked.Decrement(ref _owner._abandoned);
                    return;
                }
            }
        }
    }
}
