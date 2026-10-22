using System.IO;
using M1vClock.App.Hosting;
using M1vClock.App.Pages;
using M1vClock.App.Services;
using M1vClock.Core.Paths;
using M1vClock.Core.Settings;
using M1vClock.Core.Shell;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.App.Tests;

public sealed class SettingsPageViewModelTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("m1vclock-app-tests");
    private readonly SystemPreferences _preferences = new();
    private int _writes;

    public void Dispose()
    {
        _preferences.Dispose();
        _temp.Delete(recursive: true);
    }

    [Fact]
    public void OpeningThePageDoesNotWriteTheSettings()
    {
        using var theme = CreateTheme(new AppSettings { Theme = ThemePreference.Dark, Language = "en" }, out var settings);

        var page = CreatePage(theme, settings);

        Assert.Equal(2, page.ThemeIndex);
        Assert.Equal(1, page.LanguageIndex);
        Assert.False(page.RestartRequired);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void ChangingTheLanguageSavesItAndAsksForARestart()
    {
        using var theme = CreateTheme(new AppSettings { Language = "pt-BR" }, out var settings);
        var page = CreatePage(theme, settings);

        page.LanguageIndex = 1;

        Assert.Equal("en", settings.Current.Language);
        Assert.True(page.RestartRequired);
        Assert.Equal(1, _writes);
    }

    [Fact]
    public void GoingBackToTheStartingLanguageHidesTheRestartNotice()
    {
        using var theme = CreateTheme(new AppSettings { Language = "pt-BR" }, out var settings);
        var page = CreatePage(theme, settings);
        page.LanguageIndex = 1;

        page.LanguageIndex = 0;

        Assert.Equal("pt-BR", settings.Current.Language);
        Assert.False(page.RestartRequired);
    }

    [Fact]
    public void AnUnknownSavedLanguageOpensAsPortugueseWithoutWriting()
    {
        using var theme = CreateTheme(new AppSettings { Language = "fr-FR" }, out var settings);

        var page = CreatePage(theme, settings);

        Assert.Equal(0, page.LanguageIndex);
        Assert.Equal(0, _writes);
    }

    [Fact]
    public void TheSensorHistorySwitchStartsOnAndSavesWhenChanged()
    {
        using var theme = CreateTheme(new AppSettings(), out var settings);
        var page = CreatePage(theme, settings);

        Assert.True(page.BackgroundSensorHistory);
        page.BackgroundSensorHistory = false;

        Assert.False(settings.Current.BackgroundSensorHistory);
        Assert.False(page.BackgroundSensorHistory);
        Assert.Equal(1, _writes);
    }

    [Fact]
    public void TheSensorHistorySwitchReflectsASavedOffAndDoesNotWriteWhenUnchanged()
    {
        using var theme = CreateTheme(new AppSettings { BackgroundSensorHistory = false }, out var settings);
        var page = CreatePage(theme, settings);

        Assert.False(page.BackgroundSensorHistory);
        page.BackgroundSensorHistory = false;

        Assert.Equal(0, _writes);
    }

    private ThemeService CreateTheme(AppSettings initial, out AppSettingsService settings)
    {
        var store = new SettingsStore<AppSettings>(Path.Combine(_temp.FullName, "settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);
        store.Save(initial);
        settings = new AppSettingsService(store, NullLogger<AppSettingsService>.Instance);
        settings.Changed += (_, _) => _writes++;
        return new ThemeService(_preferences, settings);
    }

    private SettingsPageViewModel CreatePage(ThemeService theme, AppSettingsService settings) => new(
        theme,
        settings,
        new NoShell(),
        new AppPaths(_temp.FullName, isHardened: false),
        new AppInfo("0.1.0", _temp.FullName, false),
        NullLogger<SettingsPageViewModel>.Instance);

    private sealed class NoShell : IShellLauncher
    {
        public void OpenUrl(Uri url)
        {
        }

        public void OpenFolder(string path)
        {
        }

        public void Start(string fileName, string? arguments, string? workingDirectory)
        {
        }
    }
}
