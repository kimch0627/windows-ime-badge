using Xunit;

namespace ImeBadge.Tests;

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", HotkeyModifiers.Control | HotkeyModifiers.Alt, 'H')]
    [InlineData("ctrl + shift + f12", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x7B)]
    [InlineData("Win+Space", HotkeyModifiers.Win, 0x20)]
    [InlineData("Control+Alt+1", HotkeyModifiers.Control | HotkeyModifiers.Alt, '1')]
    [InlineData("Alt+PageUp", HotkeyModifiers.Alt, 0x21)]
    public void TryParse_Valid(string text, HotkeyModifiers mods, int key)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec));
        Assert.Equal(mods, spec.Modifiers);
        Assert.Equal(key, spec.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("H")]                 // 보조키 없음
    [InlineData("Shift+H")]           // Shift 만으로는 부족
    [InlineData("Ctrl+Alt")]          // 키 없음
    [InlineData("Ctrl+Alt+H+J")]      // 키가 둘
    [InlineData("Ctrl+Alt+ㅎ")]       // 지원하지 않는 키
    [InlineData("Ctrl++H")]
    [InlineData("Ctrl+F25")]
    public void TryParse_Invalid(string? text) => Assert.False(HotkeySpec.TryParse(text, out _));

    [Theory]
    [InlineData("ctrl+alt+h", "Ctrl+Alt+H")]
    [InlineData("shift+ctrl+F5", "Ctrl+Shift+F5")]
    [InlineData("win+space", "Win+Space")]
    public void ToString_Normalizes(string text, string expected)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec));
        Assert.Equal(expected, spec.ToString());
    }

    [Fact]
    public void Default_IsCtrlAltH() => Assert.Equal("Ctrl+Alt+H", HotkeySpec.Default.ToString());

    [Fact]
    public void Settings_InvalidHotkey_NormalizesToDefault()
    {
        var s = new Settings { Hotkey = "banana" };
        s.Normalize();
        Assert.Equal("Ctrl+Alt+H", s.Hotkey);
        s.Hotkey = " ctrl+alt+k ";
        s.Normalize();
        Assert.Equal("Ctrl+Alt+K", s.Hotkey);
    }
}
