using Orbix.Core.Localization;
using Xunit;

namespace Orbix.Core.Tests;

public class LocTests
{
    [Fact]
    public void In_ReturnsTheRequestedLanguage_AndFallsBackToTheKey()
    {
        Assert.Equal("Проводник", Loc.In("ru", "Default.Explorer"));
        Assert.Equal("File Explorer", Loc.In("en", "Default.Explorer"));
        Assert.Equal("File Explorer", Loc.In("de", "Default.Explorer"));
        Assert.Equal("No.Such.Key", Loc.In("ru", "No.Such.Key"));
    }

    [Theory]
    [InlineData("auto", "ru-RU", "ru")]
    [InlineData("AUTO", "en-US", "en")]
    [InlineData("", "ru", "ru")]
    [InlineData("ru", "en-US", "ru")]
    [InlineData("en", "ru-RU", "en")]
    [InlineData("fr", "ru-RU", "en")]
    public void Resolve_PicksTheLanguage(string setting, string system, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, system));

    [Fact]
    public void EveryKeyHasNonEmptyTranslations()
    {
        foreach (var key in Loc.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(Loc.In("ru", key)), key);
            Assert.False(string.IsNullOrWhiteSpace(Loc.In("en", key)), key);
        }
    }

    [Fact]
    public void T_WithArguments_Formats()
    {
        Loc.SetLanguage("en");
        // a key that is not declared is used as the format string itself
        Assert.Equal("Value 5", Loc.T("Value {0}", 5));
    }
}
