using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImeBadge;

/// <summary>
/// 실험 기능 "입력칸별 한/영 기억". 입력칸마다 마지막으로 쓴 한/영을 기억했다가, 그 칸에 다시 들어왔을 때 지금 입력 모드가 기억과 다르면
/// 알려 줄 모드를 돌려준다(한/영을 대신 바꾸지는 않는다). Windows 는 창(앱)마다만 입력 방식을 따로 기억하므로, 같은 창 안의 입력칸
/// (브라우저 주소창과 채팅창, 편집기와 커밋 메시지 칸)은 이 기능이 구별한다.
/// <para>
/// 기억하는 때: 같은 입력칸에서 같은 모드로 caret 이 <see cref="CommitAfter"/> 번 움직이면(글자를 몇 자 치면) 그 모드를 기억한다.
/// 들어오자마자 한/영을 바꾸거나 화살표로 한두 칸 옮긴 것은 기억하지 않는다. 비유하면 단골 손님의 주문을 몇 번 받아 본 뒤에야 외우는 점원이다.
/// </para>
/// <para>
/// 입력칸은 부르는 쪽이 만든 키(<see cref="KeyOf"/>: 앱 이름과 입력칸 이름을 해시한 값)로 알아본다. 파일에는 해시와 한/영, 마지막으로 쓴
/// 날짜만 남고 입력한 글자는 남지 않는다. <see cref="Capacity"/> 를 넘으면 가장 오래 쓰지 않은 칸부터 잊는다.
/// </para>
/// </summary>
public sealed class FieldMemory
{
    /// <summary>기억하는 입력칸 수의 상한.</summary>
    public const int Capacity = 400;

    /// <summary>같은 칸에서 같은 모드로 caret 이 이만큼 움직이면 기억한다.</summary>
    public const int CommitAfter = 3;

    public readonly record struct Entry(bool Hangul, DateTime UsedUtc);

    readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    string? _field;
    bool? _runHangul;
    int _run;

    /// <summary>저장하지 않은 변경이 있다.</summary>
    public bool Dirty { get; private set; }

    /// <summary>기억한 입력칸 수.</summary>
    public int Count => _entries.Count;

    /// <summary>지금 들어와 있는 입력칸(마지막으로 <see cref="Observe"/> 에 넘긴 키).</summary>
    public string? CurrentField => _field;

    /// <summary>입력칸을 알아보는 원래 값(앱 이름·입력칸 이름 등)을 알아볼 수 없는 짧은 키(SHA-256 앞 12바이트, 소문자 16진수 24자)로 바꾼다.</summary>
    public static string KeyOf(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)), 0, 12).ToLowerInvariant();

    static bool IsKey(string s) => s.Length == 24 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>기억한 모드. 한글 true, 영문 false, 모르면 null.</summary>
    public bool? Recall(string field) => _entries.TryGetValue(field, out var e) ? e.Hangul : null;

    /// <summary>
    /// 배지를 갱신할 때마다 부른다.
    /// </summary>
    /// <param name="field">지금 입력칸의 키. 입력칸이 아니거나 알아볼 수 없으면 null.</param>
    /// <param name="hangul">지금 한글이면 true, 영문이면 false, 다른 언어면 null.</param>
    /// <param name="typed">이번 갱신에서 같은 칸 안에서 caret 이 움직였는가.</param>
    /// <param name="nowUtc">기억할 때 적는 날짜.</param>
    /// <returns>방금 들어온 입력칸에 기억한 모드가 지금과 다르면 그 모드(한글 true / 영문 false). 아니면 null.</returns>
    public bool? Observe(string? field, bool? hangul, bool typed, DateTime nowUtc)
    {
        if (field != _field)
        {
            _field = field;
            _run = 0;
            _runHangul = null;
            if (field is not null && hangul is bool now && _entries.TryGetValue(field, out var e) && e.Hangul != now) return e.Hangul;
            return null;
        }
        if (field is null || hangul is not bool mode || !typed) return null;
        if (_runHangul == mode) _run++;
        else { _runHangul = mode; _run = 1; }
        if (_run == CommitAfter) Remember(field, mode, nowUtc);
        return null;
    }

    void Remember(string field, bool hangul, DateTime nowUtc)
    {
        _entries[field] = new Entry(hangul, nowUtc);
        Dirty = true;
        if (_entries.Count <= Capacity) return;
        foreach (var old in _entries.OrderBy(kv => kv.Value.UsedUtc).Take(_entries.Count - Capacity).Select(kv => kv.Key).ToList())
            _entries.Remove(old);
    }

    /// <summary>모두 잊는다(설정 창의 [지우기]).</summary>
    public void Clear()
    {
        if (_entries.Count > 0) Dirty = true;
        _entries.Clear();
        _run = 0;
        _runHangul = null;
    }

    public void MarkSaved() => Dirty = false;

    public FieldMemoryFile ToFile() => new()
    {
        Fields = _entries.ToDictionary(kv => kv.Key, kv => new FieldMemoryRecord { Hangul = kv.Value.Hangul, Used = kv.Value.UsedUtc }),
    };

    /// <summary>파일 내용으로 만든다. 키 모양이 아닌 항목은 버리고, 상한을 넘으면 최근 것만 남긴다.</summary>
    public static FieldMemory FromFile(FieldMemoryFile? file)
    {
        var m = new FieldMemory();
        if (file?.Fields is null) return m;
        foreach (var (key, rec) in file.Fields.Where(kv => kv.Key is not null && IsKey(kv.Key) && kv.Value is not null)
                                               .OrderByDescending(kv => kv.Value.Used).Take(Capacity))
            m._entries[key] = new Entry(rec.Hangul, DateTime.SpecifyKind(rec.Used, DateTimeKind.Utc));
        return m;
    }
}

/// <summary><see cref="FieldMemory"/> 의 저장 형식. 키는 입력칸의 해시, 값은 한/영과 마지막으로 쓴 날짜.</summary>
public sealed class FieldMemoryFile
{
    public int Version { get; set; } = 1;
    public Dictionary<string, FieldMemoryRecord> Fields { get; set; } = new();
}

public sealed class FieldMemoryRecord
{
    public bool Hangul { get; set; }
    public DateTime Used { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(FieldMemoryFile))]
public sealed partial class FieldMemoryJsonContext : JsonSerializerContext { }

/// <summary>입력칸 기억 파일(%APPDATA%\ImeBadge\fields.json)을 읽고 쓴다. 망가진 파일은 없는 것으로 보고 새로 배운다.</summary>
public static class FieldMemoryStore
{
    public const string FileName = "fields.json";

    public static FieldMemory Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new FieldMemory();
            return FieldMemory.FromFile(JsonSerializer.Deserialize(File.ReadAllText(path), FieldMemoryJsonContext.Default.FieldMemoryFile));
        }
        catch (Exception ex)
        {
            Log.Error($"field memory load failed ({path})", ex);
            return new FieldMemory();
        }
    }

    public static bool Save(FieldMemory memory, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(memory.ToFile(), FieldMemoryJsonContext.Default.FieldMemoryFile));
            File.Move(tmp, path, overwrite: true);
            memory.MarkSaved();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("field memory save failed", ex);
            return false;
        }
    }

    public static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { Log.Error("field memory delete failed", ex); }
    }
}
