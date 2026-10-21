using M1vClock.App.Localization;

namespace M1vClock.App.Tests;

public sealed class SupportedLanguagesTests
{
    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt", "pt-BR")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("EN-gb", "en")]
    [InlineData("fr-FR", "pt-BR")]
    [InlineData("", "pt-BR")]
    [InlineData("   ", "pt-BR")]
    [InlineData(null, "pt-BR")]
    public void ResolveFallsBackToPortugueseForAnythingElse(string? tag, string expected)
    {
        Assert.Equal(expected, SupportedLanguages.Resolve(tag).Name);
    }

    [Theory]
    [InlineData("pt-BR", 0)]
    [InlineData("en", 1)]
    [InlineData("en-US", 1)]
    [InlineData("xx", 0)]
    public void IndexAndTagRoundTrip(string tag, int index)
    {
        Assert.Equal(index, SupportedLanguages.IndexOf(tag));
        Assert.Equal(SupportedLanguages.Resolve(tag).Name, SupportedLanguages.TagAt(index));
    }

    [Fact]
    public void EverySupportedLanguageResolvesToItself()
    {
        Assert.All(SupportedLanguages.All, tag => Assert.Equal(tag, SupportedLanguages.Resolve(tag).Name));
    }

    [Fact]
    public void NumbersFollowTheChosenLanguage()
    {
        Assert.Equal("48,6", 48.6.ToString("0.0", SupportedLanguages.Resolve("pt-BR")));
        Assert.Equal("48.6", 48.6.ToString("0.0", SupportedLanguages.Resolve("en")));
    }
}
