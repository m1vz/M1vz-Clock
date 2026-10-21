using System.Text.Json.Nodes;
using M1vClock.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void LoadWithoutFileReturnsDefaultsAsCreated()
    {
        using var temp = new TempDirectory();
        var store = CreateAppStore(temp);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.Created, result.Status);
        Assert.Equal(ThemePreference.System, result.Settings.Theme);
        Assert.Equal(AppSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
    }

    [Fact]
    public void SaveThenLoadKeepsValues()
    {
        using var temp = new TempDirectory();
        var store = CreateAppStore(temp);
        var settings = new AppSettings { Theme = ThemePreference.Dark, CollapsedSidebarSections = ["jogos"] };

        store.Save(settings);
        var result = CreateAppStore(temp).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(ThemePreference.Dark, result.Settings.Theme);
        Assert.Equal(["jogos"], result.Settings.CollapsedSidebarSections);
    }

    [Fact]
    public void CorruptedFileIsQuarantinedAndBackupIsUsed()
    {
        using var temp = new TempDirectory();
        var store = CreateAppStore(temp);
        store.Save(new AppSettings { Theme = ThemePreference.Light });
        store.Save(new AppSettings { Theme = ThemePreference.Dark });
        File.WriteAllText(store.FilePath, "{ isto não é json");

        var result = CreateAppStore(temp).Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equal(ThemePreference.Light, result.Settings.Theme);
        Assert.NotNull(result.QuarantinedFile);
        Assert.Equal("{ isto não é json", File.ReadAllText(result.QuarantinedFile));
    }

    [Fact]
    public void CorruptedFileWithoutBackupResetsButKeepsTheBrokenFile()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "[1, 2,");

        var result = CreateAppStore(temp).Load();

        Assert.Equal(SettingsLoadStatus.ResetAfterCorruption, result.Status);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(result.QuarantinedFile));
    }

    [Fact]
    public void NewerSchemaIsNeverOverwritten()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        const string future = "{\"SchemaVersion\": 99, \"Theme\": \"Dark\", \"NovaOpcao\": true}";
        File.WriteAllText(path, future);
        var store = CreateAppStore(temp);

        var result = store.Load();
        store.Save(result.Settings);

        Assert.Equal(SettingsLoadStatus.NewerSchemaReadOnly, result.Status);
        Assert.Equal(future, File.ReadAllText(path));
    }

    [Fact]
    public void MigrationsRunInOrderUpToCurrentVersion()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\": 1, \"Nome\": \"Ana\"}");
        var store = new SettingsStore<VersionThreeSettings>(
            path,
            3,
            [new RenameNameToFullName(), new AddGreeting()],
            NullLogger.Instance);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal("Ana", result.Settings.FullName);
        Assert.Equal("Olá, Ana", result.Settings.Greeting);
        Assert.Equal(3, result.Settings.SchemaVersion);
    }

    [Fact]
    public void MissingMigrationIsTreatedAsUnreadable()
    {
        using var temp = new TempDirectory();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\": 1, \"Nome\": \"Ana\"}");
        var store = new SettingsStore<VersionThreeSettings>(path, 3, [new AddGreeting()], NullLogger.Instance);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.ResetAfterCorruption, result.Status);
    }

    private static SettingsStore<AppSettings> CreateAppStore(TempDirectory temp) =>
        new(temp.File("settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);

    private sealed class VersionThreeSettings : IVersionedSettings
    {
        public int SchemaVersion { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Greeting { get; set; } = string.Empty;
    }

    private sealed class RenameNameToFullName : ISettingsMigration
    {
        public int FromVersion => 1;

        public void Apply(JsonObject settings)
        {
            settings["FullName"] = settings["Nome"]?.DeepClone();
            settings.Remove("Nome");
        }
    }

    private sealed class AddGreeting : ISettingsMigration
    {
        public int FromVersion => 2;

        public void Apply(JsonObject settings) =>
            settings["Greeting"] = "Olá, " + settings["FullName"]?.GetValue<string>();
    }
}
