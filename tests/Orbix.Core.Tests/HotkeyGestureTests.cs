using Orbix.Core.Hotkeys;
using Xunit;

namespace Orbix.Core.Tests;

public class HotkeyGestureTests
{
    [Fact]
    public void DefaultHotkey_ParsesToCtrlAltSpace()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+Space", out var g));
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Alt, g.Modifiers);
        Assert.Equal(0x20, g.VirtualKey);
        Assert.True(g.IsValid);
        Assert.Equal("Ctrl+Alt+Space", g.ToString());
    }

    [Theory]
    [InlineData("ctrl+alt+space", "Ctrl+Alt+Space")]
    [InlineData("  CONTROL + Shift + a ", "Ctrl+Shift+A")]
    [InlineData("Win+Shift+F12", "Shift+Win+F12")]
    [InlineData("Alt+Ctrl+Shift+Win+Z", "Ctrl+Alt+Shift+Win+Z")]
    [InlineData("Ctrl+Alt+`", "Ctrl+Alt+`")]
    [InlineData("Ctrl+Num5", "Ctrl+Num5")]
    [InlineData("Ctrl+PgUp", "Ctrl+PageUp")]
    [InlineData("Ctrl+0x5B", "Ctrl+0x5B")]
    public void Parse_NormalizesSpelling(string input, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var g));
        Assert.Equal(expected, g.ToString());

        // and the canonical text parses back to the same gesture
        Assert.True(HotkeyGesture.TryParse(g.ToString(), out var again));
        Assert.Equal(g, again);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Banana")]
    [InlineData("+")]
    [InlineData("Ctrl+0xZZ")]
    public void Parse_RejectsGarbage(string input)
    {
        Assert.False(HotkeyGesture.TryParse(input, out var g));
        Assert.True(g.IsEmpty);
    }

    [Fact]
    public void Validity_RequiresAModifier_ExceptForHighFunctionKeys()
    {
        Assert.False(HotkeyGesture.Parse("A").IsValid);
        Assert.False(HotkeyGesture.Parse("F5").IsValid);
        Assert.True(HotkeyGesture.Parse("F13").IsValid);
        Assert.True(HotkeyGesture.Parse("Ctrl+F5").IsValid);
        Assert.False(HotkeyGesture.None.IsValid);
    }

    [Fact]
    public void Modifiers_MatchWin32Constants()
    {
        Assert.Equal(1, (int)KeyModifiers.Alt);
        Assert.Equal(2, (int)KeyModifiers.Control);
        Assert.Equal(4, (int)KeyModifiers.Shift);
        Assert.Equal(8, (int)KeyModifiers.Win);
    }

    [Fact]
    public void Parse_ThrowsOnInvalidText() => Assert.Throws<FormatException>(() => HotkeyGesture.Parse("nonsense"));

    [Fact]
    public void KeyName_CoversLettersDigitsAndFunctionKeys()
    {
        Assert.Equal("A", HotkeyGesture.KeyName('A'));
        Assert.Equal("7", HotkeyGesture.KeyName('7'));
        Assert.Equal("F1", HotkeyGesture.KeyName(0x70));
        Assert.Equal("F24", HotkeyGesture.KeyName(0x87));
        Assert.Equal("Enter", HotkeyGesture.KeyName(0x0D));
        Assert.Equal("0xE9", HotkeyGesture.KeyName(0xE9));
    }
}
