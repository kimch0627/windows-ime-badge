using System;
using System.Collections.Generic;
using System.Linq;

namespace ImeBadge;

/// <summary>
/// 입력하는데 배지가 안 뜬 까닭. 설정 창의 문구 키(missed.reason.{값})와 이슈에 붙여 넣는 보고서에 그대로 쓴다.
/// </summary>
public static class MissReason
{
    /// <summary>관리자 권한으로 실행된 앱. Windows(UIPI)가 보통 권한 프로그램의 접근성·IME 질문을 막는다.</summary>
    public const string Elevated = "elevated";

    /// <summary>앱이 커서 위치를 알려 주지 않는다(Win32 caret·커서 창·UI Automation·MSAA 어느 것도 없음).</summary>
    public const string NoCaret = "noCaret";

    /// <summary>앱이 커서 위치 질문(UI Automation)에 제때(0.4초) 답하지 않았다.</summary>
    public const string Slow = "slow";

    /// <summary>커서는 찾았지만 한/영 상태를 읽지 못했다(IMM·TSF 모두 답이 없음).</summary>
    public const string ImeUnknown = "imeUnknown";

    /// <summary>"전체 화면 앱에서는 숨김" 설정 때문에 숨겼다.</summary>
    public const string Fullscreen = "fullscreen";

    public static readonly string[] All = { Elevated, NoCaret, Slow, ImeUnknown, Fullscreen };
}

/// <summary>
/// 포커스된 요소가 글자를 받을 수 있는 종류인가(UI Automation 컨트롤 종류 ID). 버튼·목록 항목처럼 글자를 받지 않는 곳에서
/// 스페이스·백스페이스를 눌렀다고(누르기·선택·뒤로 가기) "배지가 안 떴다" 고 세지 않으려고 쓴다.
/// 직접 글자를 그리는 앱(터미널·게임·Java·Qt)은 입력칸을 Pane·Custom·Window 로만 알리므로 그 종류는 입력칸일 수 있는 것으로 둔다.
/// </summary>
public static class InputSurface
{
    public const int Unknown = 0;
    public const int ComboBox = 50003, Edit = 50004, Custom = 50025, Group = 50026, Document = 50030, Window = 50032, Pane = 50033;

    /// <summary>글자를 받을 수 있는 종류이거나 모른다(0). 그 밖(버튼·체크 상자·목록·메뉴·탭·링크·스크롤 막대 등)은 false.</summary>
    public static bool MayTakeText(int controlType) =>
        controlType is Unknown or ComboBox or Edit or Custom or Group or Document or Window or Pane;
}

/// <summary>설정 창 목록에 보일 앱 하나.</summary>
/// <param name="Process">실행 파일 이름(소문자, .exe 없음).</param>
/// <param name="Reason">가장 자주 나온 까닭(<see cref="MissReason"/>). 횟수가 같으면 최근 것.</param>
/// <param name="MissedSeconds">입력하는데 배지가 안 보인 순간 수(1초에 한 번만 센다).</param>
/// <param name="ShownSeconds">입력하는 동안 배지가 보인 순간 수(1초에 한 번만 센다).</param>
/// <param name="LastMissed">마지막으로 안 보인 때(현지 시각).</param>
/// <param name="Detail">마지막으로 안 보인 자리의 기술 정보(창 클래스 이름·UI Automation 컨트롤 종류). 보고서용.</param>
public sealed record MissedApp(string Process, string Reason, int MissedSeconds, int ShownSeconds, DateTime LastMissed, string Detail)
{
    /// <summary>이번 실행 동안 이 앱에서 입력할 때 배지가 한 번도 보이지 않았다.</summary>
    public bool NeverShown => ShownSeconds == 0;
}

/// <summary>
/// 배지가 안 뜬 앱 찾기. 입력하는 순간마다(부르는 쪽이 가린다: 스페이스·백스페이스를 누르고 있거나 한/영이 바뀜) 배지가 보였는지,
/// 안 보였으면 그 까닭을 앱(실행 파일 이름)마다 센다. 같은 앱의 같은 종류(보임·안 보임)는 1초에 한 번만 세어 폴링 간격과 상관없게 한다.
/// <para>
/// 안 보인 순간이 <see cref="MinMissedSeconds"/> 번 이상인 앱만 목록에 올린다. 우연히 한 번 놓친 것은 올리지 않기 위해서다.
/// 배지가 잘 뜨는 앱도 센다: 나중에 몇 번 놓쳤을 때 "한 번도 안 뜸" 이 아니라 "일부 입력칸에서 안 뜸" 으로 보이게 하려고.
/// 메모리에만 두고 파일로 저장하지 않는다(프로그램을 끄면 사라진다).
/// </para>
/// </summary>
public sealed class MissedAppTracker
{
    /// <summary>같은 앱·같은 종류를 한 번만 세는 간격(ms).</summary>
    public const int BucketMs = 1000;

    /// <summary>목록에 올리는 최소 "안 보인 순간" 수.</summary>
    public const int MinMissedSeconds = 3;

    /// <summary>기억하는 앱 수의 상한. 넘으면 안 보인 적 없는 앱부터, 그다음 오래전에 본 앱부터 잊는다.</summary>
    public const int MaxApps = 64;

    sealed class Entry
    {
        public long MissBucket = long.MinValue, ShownBucket = long.MinValue, Touched;
        public int Missed, Shown;
        public readonly Dictionary<string, int> Reasons = new();
        public string LastReason = "", Detail = "";
        public DateTime LastMissed;
    }

    readonly Dictionary<string, Entry> _apps = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
    long _touch;

    /// <summary>목록(<see cref="List"/>)이 바뀌었을 수 있으면 늘어난다. 설정 창이 이 값만 보고 다시 그릴지 정한다.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// 입력하는 순간 하나를 적는다. <paramref name="reason"/> 이 null 이면 배지가 보였다.
    /// 이 호출로 앱이 처음 목록에 올랐으면 true(디버그 로그용).
    /// </summary>
    /// <param name="nowMs">단조 시계(Environment.TickCount64).</param>
    /// <param name="now">목록에 보일 현지 시각.</param>
    public bool Observe(string? process, string? reason, long nowMs, DateTime now, string detail = "")
    {
        if (string.IsNullOrEmpty(process) || _ignored.Contains(process)) return false;
        if (!_apps.TryGetValue(process, out var e))
        {
            if (_apps.Count >= MaxApps) Evict();
            _apps[process] = e = new Entry();
        }
        long bucket = Math.DivRem(nowMs, BucketMs, out long rem) - (rem < 0 ? 1 : 0);   // 음수 시각도 내림
        bool listed = e.Missed >= MinMissedSeconds;
        if (reason is null)
        {
            if (e.ShownBucket == bucket) return false;
            e.ShownBucket = bucket;
            e.Shown++;
            e.Touched = ++_touch;
            if (listed && e.Shown == 1) Version++;   // "한 번도 안 뜸" → "일부 입력칸에서 안 뜸"
            return false;
        }
        if (e.MissBucket == bucket) return false;
        e.MissBucket = bucket;
        e.Missed++;
        e.Reasons[reason] = e.Reasons.GetValueOrDefault(reason) + 1;
        e.LastReason = reason;
        e.LastMissed = now;
        e.Detail = detail ?? "";
        e.Touched = ++_touch;
        if (e.Missed >= MinMissedSeconds) Version++;
        return !listed && e.Missed >= MinMissedSeconds;
    }

    /// <summary>목록에 올릴 앱. 한 번도 안 뜬 앱이 먼저, 그 안에서는 최근에 놓친 앱이 먼저.</summary>
    public IReadOnlyList<MissedApp> List() =>
        _apps.Where(kv => kv.Value.Missed >= MinMissedSeconds)
            .Select(kv => new MissedApp(kv.Key, Primary(kv.Value), kv.Value.Missed, kv.Value.Shown, kv.Value.LastMissed, kv.Value.Detail))
            .OrderByDescending(a => a.NeverShown)
            .ThenByDescending(a => a.LastMissed)
            .ThenBy(a => a.Process, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>목록에서 빼고, <see cref="Clear"/> 할 때까지(보통은 프로그램을 끌 때까지) 이 앱은 세지 않는다.</summary>
    public void Ignore(string process)
    {
        if (string.IsNullOrEmpty(process)) return;
        _ignored.Add(process);
        if (_apps.Remove(process)) Version++;
    }

    /// <summary>모두 잊는다(무시한 앱 포함).</summary>
    public void Clear()
    {
        if (_apps.Count == 0 && _ignored.Count == 0) return;
        _apps.Clear();
        _ignored.Clear();
        Version++;
    }

    static string Primary(Entry e)
    {
        string best = e.LastReason;
        int most = e.Reasons.GetValueOrDefault(best);
        foreach (var (reason, count) in e.Reasons)
            if (count > most) { best = reason; most = count; }
        return best;
    }

    void Evict()
    {
        string? victim = null;
        bool victimMissed = true;
        long victimTouched = long.MaxValue;
        foreach (var (name, e) in _apps)
        {
            bool missed = e.Missed > 0;
            // 안 보인 적 없는(목록에 오를 일 없는) 앱을 먼저, 같은 쪽끼리는 오래전에 본 앱을 잊는다.
            if (victim is null || (victimMissed && !missed) || (missed == victimMissed && e.Touched < victimTouched))
            {
                victim = name;
                victimMissed = missed;
                victimTouched = e.Touched;
            }
        }
        if (victim is not null && _apps.Remove(victim) && victimMissed) Version++;
    }
}
