using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using M1vClock.Core.IO;
using Microsoft.Extensions.Logging;

namespace M1vClock.Core.Settings;

public interface IVersionedSettings
{
    int SchemaVersion { get; set; }
}

public interface ISettingsMigration
{
    int FromVersion { get; }

    void Apply(JsonObject settings);
}

public enum SettingsLoadStatus
{
    Loaded,
    Created,
    RecoveredFromBackup,
    ResetAfterCorruption,
    NewerSchemaReadOnly,
}

public sealed record SettingsLoadResult<T>(T Settings, SettingsLoadStatus Status, string? QuarantinedFile);

public sealed class SettingsStore<T>
    where T : class, IVersionedSettings, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly int _currentVersion;
    private readonly IReadOnlyList<ISettingsMigration> _migrations;
    private readonly ILogger _logger;
    private bool _readOnly;

    public SettingsStore(string filePath, int currentVersion, IEnumerable<ISettingsMigration> migrations, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentVersion, 1);
        ArgumentNullException.ThrowIfNull(migrations);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = Path.GetFullPath(filePath);
        _currentVersion = currentVersion;
        _migrations = migrations.OrderBy(m => m.FromVersion).ToList();
        _logger = logger;
    }

    public string FilePath => _filePath;

    public string BackupPath => _filePath + ".bak";

    public SettingsLoadResult<T> Load()
    {
        if (!File.Exists(_filePath) && !File.Exists(BackupPath))
        {
            return new SettingsLoadResult<T>(new T { SchemaVersion = _currentVersion }, SettingsLoadStatus.Created, null);
        }

        var primary = TryRead(_filePath);
        if (primary.Settings is not null)
        {
            return new SettingsLoadResult<T>(primary.Settings, SettingsLoadStatus.Loaded, null);
        }

        if (primary.NewerSchema)
        {
            _readOnly = true;
            CoreLog.SettingsNewerSchema(_logger, _filePath, primary.Version);
            return new SettingsLoadResult<T>(new T { SchemaVersion = _currentVersion }, SettingsLoadStatus.NewerSchemaReadOnly, null);
        }

        string? quarantined = null;
        if (primary.Error is not null)
        {
            CoreLog.SettingsUnreadable(_logger, primary.Error, _filePath);
            quarantined = Quarantine(_filePath);
        }

        var backup = TryRead(BackupPath);
        if (backup.Settings is not null)
        {
            CoreLog.SettingsRecovered(_logger, BackupPath);
            Save(backup.Settings);
            return new SettingsLoadResult<T>(backup.Settings, SettingsLoadStatus.RecoveredFromBackup, quarantined);
        }

        if (backup.Error is not null)
        {
            CoreLog.SettingsBackupUnreadable(_logger, backup.Error, BackupPath);
        }

        return new SettingsLoadResult<T>(new T { SchemaVersion = _currentVersion }, SettingsLoadStatus.ResetAfterCorruption, quarantined);
    }

    public void Save(T settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_readOnly)
        {
            CoreLog.SettingsSaveSkipped(_logger);
            return;
        }

        settings.SchemaVersion = _currentVersion;
        var json = JsonSerializer.Serialize(settings, Options);
        AtomicFile.WriteAllText(_filePath, json, File.Exists(_filePath) ? BackupPath : null);
    }

    private ReadAttempt TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return new ReadAttempt(null, null, false, 0);
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new JsonException("O arquivo não contém um objeto JSON.");
            var version = node[nameof(IVersionedSettings.SchemaVersion)]?.GetValue<int>() ?? 0;
            if (version > _currentVersion)
            {
                return new ReadAttempt(null, null, true, version);
            }

            foreach (var migration in _migrations.Where(m => m.FromVersion >= version && m.FromVersion < _currentVersion))
            {
                if (migration.FromVersion != version)
                {
                    throw new InvalidOperationException($"Falta a migração do schema {version} para {version + 1}.");
                }

                migration.Apply(node);
                version = migration.FromVersion + 1;
                node[nameof(IVersionedSettings.SchemaVersion)] = version;
            }

            if (version != _currentVersion)
            {
                throw new InvalidOperationException($"Falta a migração do schema {version} para {_currentVersion}.");
            }

            var settings = node.Deserialize<T>(Options) ?? throw new JsonException("Configurações vazias.");
            return new ReadAttempt(settings, null, false, version);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or FormatException or NotSupportedException)
        {
            return new ReadAttempt(null, ex, false, 0);
        }
    }

    private string? Quarantine(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var target = $"{path}.corrompido-{stamp}";
        File.Move(path, target);
        CoreLog.SettingsQuarantined(_logger, target);
        return target;
    }

    private sealed record ReadAttempt(T? Settings, Exception? Error, bool NewerSchema, int Version);
}
