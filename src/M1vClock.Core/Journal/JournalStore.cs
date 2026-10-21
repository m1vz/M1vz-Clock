using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using M1vClock.Core.IO;
using Microsoft.Extensions.Logging;

namespace M1vClock.Core.Journal;

public sealed class JournalStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILogger<JournalStore> _logger;

    public JournalStore(string filePath, ILogger<JournalStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = Path.GetFullPath(filePath);
        _logger = logger;
    }

    public string BackupPath => _filePath + ".bak";

    public IReadOnlyList<JournalEntry> Load()
    {
        if (!File.Exists(_filePath))
        {
            return File.Exists(BackupPath) ? LoadBackupOrThrow(null) : [];
        }

        try
        {
            return Read(_filePath);
        }
        catch (JsonException ex)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var target = $"{_filePath}.corrompido-{stamp}";
            File.Copy(_filePath, target);
            CoreLog.JournalUnreadable(_logger, ex, target);
            return LoadBackupOrThrow(ex);
        }
    }

    private List<JournalEntry> LoadBackupOrThrow(JsonException? original)
    {
        try
        {
            var entries = Read(BackupPath);
            CoreLog.JournalRecovered(_logger, BackupPath);
            return entries;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            CoreLog.JournalBackupUnreadable(_logger, ex, BackupPath);
            throw new InvalidDataException("O histórico de alterações está corrompido e não há cópia legível.", original ?? ex);
        }
    }

    private static List<JournalEntry> Read(string path) =>
        JsonSerializer.Deserialize<List<JournalEntry>>(File.ReadAllText(path), Options) ?? [];

    public void Save(IReadOnlyList<JournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(entries, Options), File.Exists(_filePath) ? _filePath + ".bak" : null);
    }
}
