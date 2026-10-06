using System;
using System.IO;
using Xunit;

namespace ImeBadge.Tests;

public sealed class FieldMemoryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "imebadge-fields-" + Guid.NewGuid().ToString("N"));
    static readonly DateTime T0 = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
    static readonly string Url = FieldMemory.KeyOf("chrome|url"), Chat = FieldMemory.KeyOf("chrome|chat");

    public FieldMemoryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    /// <summary>입력칸에 들어가 같은 모드로 n 번 친다.</summary>
    static void Type(FieldMemory m, string field, bool hangul, int n, DateTime? at = null)
    {
        m.Observe(field, hangul, typed: false, at ?? T0);
        for (int i = 0; i < n; i++) m.Observe(field, hangul, typed: true, at ?? T0);
    }

    [Fact]
    public void RemembersAfterAFewKeystrokes_AndSuggestsOnReturn()
    {
        var m = new FieldMemory();
        Type(m, Url, hangul: false, FieldMemory.CommitAfter);
        Assert.False(m.Recall(Url));
        Assert.True(m.Dirty);

        Type(m, Chat, hangul: true, FieldMemory.CommitAfter);
        // 주소창으로 돌아왔는데 한글이다 → "보통 영문" 을 알린다
        Assert.False(m.Observe(Url, hangul: true, typed: false, T0));
        // 그대로 머물면 다시 알리지 않는다
        Assert.Null(m.Observe(Url, hangul: true, typed: false, T0));
        // 채팅 칸으로 갔는데 한글이다 → 기억과 같으니 조용히
        Assert.Null(m.Observe(Chat, hangul: true, typed: false, T0));
    }

    [Fact]
    public void TooFewKeystrokes_AreNotRemembered()
    {
        var m = new FieldMemory();
        Type(m, Url, hangul: false, FieldMemory.CommitAfter - 1);
        Assert.Null(m.Recall(Url));
        Assert.False(m.Dirty);
    }

    [Fact]
    public void SwitchingModeMidway_StartsCountingAgain_AndLatestWins()
    {
        var m = new FieldMemory();
        m.Observe(Url, false, false, T0);
        m.Observe(Url, false, true, T0);
        m.Observe(Url, false, true, T0);
        m.Observe(Url, true, true, T0);   // 두 번 치고 한/영을 바꿨다
        Assert.Null(m.Recall(Url));
        m.Observe(Url, true, true, T0);
        m.Observe(Url, true, true, T0);
        Assert.True(m.Recall(Url));
    }

    [Fact]
    public void OtherLanguageOrNoField_IsIgnored()
    {
        var m = new FieldMemory();
        Type(m, Url, hangul: false, FieldMemory.CommitAfter);
        Assert.Null(m.Observe(null, true, false, T0));
        Assert.Null(m.Observe(Url, null, false, T0));   // 다른 언어 IME 로 돌아왔다: 비교할 수 없다
        for (int i = 0; i < 5; i++) m.Observe(Url, null, true, T0);
        Assert.False(m.Recall(Url));
    }

    [Fact]
    public void OverCapacity_ForgetsTheOldest()
    {
        var m = new FieldMemory();
        for (int i = 0; i <= FieldMemory.Capacity; i++)
            Type(m, FieldMemory.KeyOf("f" + i), true, FieldMemory.CommitAfter, T0.AddMinutes(i));
        Assert.Equal(FieldMemory.Capacity, m.Count);
        Assert.Null(m.Recall(FieldMemory.KeyOf("f0")));
        Assert.True(m.Recall(FieldMemory.KeyOf("f" + FieldMemory.Capacity)));
    }

    [Fact]
    public void KeyOf_IsShortStableHex_AndHidesTheRawValue()
    {
        var k = FieldMemory.KeyOf("msedge|uia|50004|비밀 메모");
        Assert.Equal(24, k.Length);
        Assert.Matches("^[0-9a-f]{24}$", k);
        Assert.Equal(k, FieldMemory.KeyOf("msedge|uia|50004|비밀 메모"));
        Assert.NotEqual(k, FieldMemory.KeyOf("msedge|uia|50004|비밀 메모2"));
    }

    [Fact]
    public void Store_RoundTrips_AndCorruptFileStartsFresh()
    {
        string path = Path.Combine(_dir, "sub", FieldMemoryStore.FileName);
        var m = new FieldMemory();
        Type(m, Url, false, FieldMemory.CommitAfter);
        Type(m, Chat, true, FieldMemory.CommitAfter);
        Assert.True(FieldMemoryStore.Save(m, path));
        Assert.False(m.Dirty);
        Assert.DoesNotContain("chrome", File.ReadAllText(path));   // 원래 값은 남지 않는다

        var back = FieldMemoryStore.Load(path);
        Assert.Equal(2, back.Count);
        Assert.False(back.Recall(Url));
        Assert.True(back.Recall(Chat));

        File.WriteAllText(path, "{ not json");
        Assert.Equal(0, FieldMemoryStore.Load(path).Count);
        Assert.Equal(0, FieldMemoryStore.Load(Path.Combine(_dir, "none.json")).Count);
    }

    [Fact]
    public void FromFile_SkipsKeysThatAreNotHashes()
    {
        var file = new FieldMemoryFile();
        file.Fields["plain text label"] = new FieldMemoryRecord { Hangul = true, Used = T0 };
        file.Fields[Url] = new FieldMemoryRecord { Hangul = false, Used = T0 };
        var m = FieldMemory.FromFile(file);
        Assert.Equal(1, m.Count);
        Assert.False(m.Recall(Url));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var m = new FieldMemory();
        Type(m, Url, false, FieldMemory.CommitAfter);
        m.MarkSaved();
        m.Clear();
        Assert.Equal(0, m.Count);
        Assert.True(m.Dirty);
        Assert.Null(m.Observe(Chat, true, false, T0));
    }
}
