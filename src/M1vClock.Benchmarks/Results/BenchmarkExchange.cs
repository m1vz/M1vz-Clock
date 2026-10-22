using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using M1vClock.Core.IO;

namespace M1vClock.Benchmarks.Results;

public enum ImportFailure
{
    None,
    Missing,
    TooLarge,
    NotValid,
    WrongFormat,
    NewerSchema,
    TooMany,
}

public sealed record ImportOutcome(IReadOnlyList<BenchmarkRecord> Accepted, int Rejected, ImportFailure Failure, Exception? Cause);

public sealed class BenchmarkExchangeFile
{
    public string Format { get; set; } = BenchmarkExchange.FormatName;

    public int SchemaVersion { get; set; } = BenchmarkExchange.CurrentSchema;

    public DateTimeOffset ExportedAt { get; set; }

    public List<BenchmarkRecord> Records { get; set; } = [];
}

public static class BenchmarkExchange
{
    public const string FormatName = "m1vclock.benchmarks";
    public const int CurrentSchema = 1;
    public const long MaximumFileBytes = 5L << 20;
    public const int MaximumRecords = 5000;
    public const int MaximumTextLength = 120;

    private static readonly DateTimeOffset EarliestPlausible = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromDays(1);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Write(string path, IEnumerable<BenchmarkRecord> records, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(records);
        var file = new BenchmarkExchangeFile { ExportedAt = now, Records = [.. records] };
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }

    public static ImportOutcome Read(string path, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return Failed(ImportFailure.Missing);
        }

        if (info.Length > MaximumFileBytes)
        {
            return Failed(ImportFailure.TooLarge);
        }

        BenchmarkExchangeFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BenchmarkExchangeFile>(File.ReadAllText(path, Encoding.UTF8), Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new ImportOutcome([], 0, ImportFailure.NotValid, ex);
        }

        if (file is null || !string.Equals(file.Format, FormatName, StringComparison.Ordinal))
        {
            return Failed(ImportFailure.WrongFormat);
        }

        if (file.SchemaVersion > CurrentSchema || file.SchemaVersion < 1)
        {
            return Failed(file.SchemaVersion > CurrentSchema ? ImportFailure.NewerSchema : ImportFailure.WrongFormat);
        }

        if (file.Records is null || file.Records.Count > MaximumRecords)
        {
            return Failed(ImportFailure.TooMany);
        }

        var accepted = new List<BenchmarkRecord>(file.Records.Count);
        var rejected = 0;
        var seen = new HashSet<Guid>();
        foreach (var raw in file.Records)
        {
            var clean = raw is null ? null : Sanitize(raw, now);
            if (clean is null || !seen.Add(clean.Id))
            {
                rejected++;
                continue;
            }

            accepted.Add(clean with { Imported = true });
        }

        return new ImportOutcome(accepted, rejected, ImportFailure.None, null);
    }

    public static BenchmarkRecord? Sanitize(BenchmarkRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Id == Guid.Empty || record.Metrics is null || record.Machine is null || record.Test is null)
        {
            return null;
        }

        var test = BenchmarkCatalog.Find(record.Test);
        if (test is null || test.ScoreVersion != record.ScoreVersion)
        {
            return null;
        }

        if (!IsReasonable(record.Score) || record.At < EarliestPlausible || record.At > now + ClockSkew)
        {
            return null;
        }

        var metrics = new Dictionary<string, double>();
        foreach (var (key, value) in record.Metrics)
        {
            if (BenchmarkCatalog.FindMetric(record.Test, key) is not null && IsReasonable(value))
            {
                metrics[key] = value;
            }
        }

        var machine = record.Machine;
        if (!IsReasonable(machine.MemoryGigabytes) || machine.LogicalProcessors is < 1 or > 4096)
        {
            return null;
        }

        return record with
        {
            Metrics = metrics,
            Imported = false,
            Note = record.Note is null ? null : Clean(record.Note),
            Machine = new BenchmarkMachine(
                Clean(machine.Cpu),
                Clean(machine.Gpu),
                machine.LogicalProcessors,
                machine.MemoryGigabytes,
                Clean(machine.OsVersion),
                Clean(machine.AppVersion)),
        };
    }

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, MaximumTextLength));
        foreach (var c in text)
        {
            if (builder.Length >= MaximumTextLength)
            {
                break;
            }

            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Trim();
    }

    private static bool IsReasonable(double value) => double.IsFinite(value) && value >= 0 && value <= 1e12;

    private static ImportOutcome Failed(ImportFailure failure) => new([], 0, failure, null);
}
