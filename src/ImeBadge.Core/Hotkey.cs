using System;
using System.Collections.Generic;
using System.Text;

namespace ImeBadge;

/// <summary>단축키 보조키. 값은 Win32 RegisterHotKey 의 MOD_* 와 같다.</summary>
[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

/// <summary>
/// "Ctrl+Alt+H" 같은 문자열과 (보조키, 가상 키 코드) 사이의 변환. Win32 에 의존하지 않아 어디서나 테스트할 수 있다.
/// 키 이름은 영문 A~Z, 0~9, F1~F24, Space 와 몇 가지 편집 키만 받는다. 보조키 없이 글자 하나만 쓰면 타이핑과 겹치므로
/// Ctrl·Alt·Win 중 하나는 반드시 있어야 <see cref="IsValid"/> 다(Shift 만으로는 부족).
/// </summary>
public readonly record struct HotkeySpec(HotkeyModifiers Modifiers, int Key)
{
    public static readonly HotkeySpec Default = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'H');

    public bool IsValid =>
        Key != 0 && KeyName(Key) is not null &&
        (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0;

    static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["Pause"] = 0x13,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["ScrollLock"] = 0x91,
    };

    /// <summary>가상 키 코드의 표시 이름. 지원하지 않는 키면 null.</summary>
    public static string? KeyName(int vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        foreach (var (name, code) in NamedKeys) if (code == vk) return name;
        return null;
    }

    static bool TryKey(string name, out int vk)
    {
        vk = 0;
        name = name.Trim();
        if (name.Length == 1 && (char.IsAsciiLetter(name[0]) || char.IsAsciiDigit(name[0]))) { vk = char.ToUpperInvariant(name[0]); return true; }
        if (name.Length >= 2 && (name[0] == 'F' || name[0] == 'f') && int.TryParse(name.AsSpan(1), out int f) && f is >= 1 and <= 24) { vk = 0x70 + f - 1; return true; }
        return NamedKeys.TryGetValue(name, out vk);
    }

    /// <summary>"Ctrl+Alt+H", "ctrl + shift + F12", "Win+Space" 등을 읽는다. 보조키 이름: Ctrl/Control, Alt, Shift, Win.</summary>
    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = HotkeyModifiers.None;
        int key = 0;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0) return false;
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": mods |= HotkeyModifiers.Control; break;
                case "ALT": mods |= HotkeyModifiers.Alt; break;
                case "SHIFT": mods |= HotkeyModifiers.Shift; break;
                case "WIN" or "WINDOWS": mods |= HotkeyModifiers.Win; break;
                default:
                    if (key != 0 || !TryKey(part, out key)) return false;
                    break;
            }
        }
        spec = new HotkeySpec(mods, key);
        return spec.IsValid;
    }

    /// <summary>"Ctrl+Alt+H" 형식. 보조키 순서는 Ctrl, Alt, Shift, Win 으로 고정한다.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        if ((Modifiers & HotkeyModifiers.Control) != 0) sb.Append("Ctrl+");
        if ((Modifiers & HotkeyModifiers.Alt) != 0) sb.Append("Alt+");
        if ((Modifiers & HotkeyModifiers.Shift) != 0) sb.Append("Shift+");
        if ((Modifiers & HotkeyModifiers.Win) != 0) sb.Append("Win+");
        sb.Append(KeyName(Key) ?? "?");
        return sb.ToString();
    }
}
