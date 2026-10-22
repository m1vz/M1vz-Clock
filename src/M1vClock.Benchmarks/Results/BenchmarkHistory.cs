using M1vClock.Core.Settings;
using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks.Results;

public sealed class BenchmarkHistoryFile : IVersionedSettings
{
    public int SchemaVersion { get; set; }

    public List<BenchmarkRecord> Records { get; set; } = [];
}

public sealed class BenchmarkHistory
{
    public const int CurrentSchema = 1;
    public const int MaximumRecords = 5000;

    private readonly SettingsStore<BenchmarkHistoryFile> _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<BenchmarkHistory> _logger;
    private readonly Lock _gate = new();
    private List<BenchmarkRecord> _records = [];

    public BenchmarkHistory(SettingsStore<BenchmarkHistoryFile> store, TimeProvider clock, ILogger<BenchmarkHistory> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public SettingsLoadStatus LoadStatus { get; private set; } = SettingsLoadStatus.Created;

    public bool SaveFailed { get; private set; }

    public static SettingsStore<BenchmarkHistoryFile> CreateStore(string filePath, ILogger logger) =>
        new(filePath, CurrentSchema, [], logger);

    public IReadOnlyList<BenchmarkRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }
    }

    public void Load()
    {
        var loaded = _store.Load();
        lock (_gate)
        {
            _records = [.. loaded.Settings.Records.OrderByDescending(r => r.At)];
            LoadStatus = loaded.Status;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<BenchmarkRecord> For(string test)
    {
        lock (_gate)
        {
            return [.. _records.Where(r => string.Equals(r.Test, test, StringComparison.Ordinal))];
        }
    }

    public BenchmarkRecord? Best(string test)
    {
        var info = BenchmarkCatalog.Find(test);
        if (info is null)
        {
            return null;
        }

        var matching = For(test).Where(r => r.ScoreVersion == info.ScoreVersion).ToList();
        if (matching.Count == 0)
        {
            return null;
        }

        return info.HigherIsBetter ? matching.MaxBy(r => r.Score) : matching.MinBy(r => r.Score);
    }

    public void Add(BenchmarkRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _records.Insert(0, record);
            _records = [.. _records.OrderByDescending(r => r.At)];
            Trim();
            SaveLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public int AddRange(IEnumerable<BenchmarkRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var added = 0;
        lock (_gate)
        {
            var known = _records.Select(r => r.Id).ToHashSet();
            foreach (var record in records)
            {
                if (known.Add(record.Id))
                {
                    _records.Add(record);
                    added++;
                }
            }

            if (added > 0)
            {
                _records = [.. _records.OrderByDescending(r => r.At)];
                Trim();
                SaveLocked();
            }
        }

        if (added > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return added;
    }

    public bool Remove(Guid id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _records.RemoveAll(r => r.Id == id) > 0;
            if (removed)
            {
                SaveLocked();
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _records = [];
            SaveLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ImportOutcome Import(string path)
    {
        var outcome = BenchmarkExchange.Read(path, _clock.GetUtcNow());
        if (outcome.Failure == ImportFailure.None)
        {
            AddRange(outcome.Accepted);
        }
        else
        {
            BenchmarkLog.ImportFailed(_logger, outcome.Cause, outcome.Failure);
        }

        return outcome;
    }

    public void Export(string path, IEnumerable<Guid>? ids = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        IEnumerable<BenchmarkRecord> chosen;
        lock (_gate)
        {
            chosen = ids is null ? [.. _records] : [.. _records.Where(r => ids.Contains(r.Id))];
        }

        BenchmarkExchange.Write(path, chosen, _clock.GetUtcNow());
    }

    private void Trim()
    {
        if (_records.Count > MaximumRecords)
        {
            _records.RemoveRange(MaximumRecords, _records.Count - MaximumRecords);
        }
    }

    private void SaveLocked()
    {
        try
        {
            _store.Save(new BenchmarkHistoryFile { Records = _records });
            SaveFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveFailed = true;
            BenchmarkLog.HistorySaveFailed(_logger, ex);
        }
    }
}
