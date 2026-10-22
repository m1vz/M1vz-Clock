using System.Text.Json;
using M1vClock.Benchmarks.Cpu;
using M1vClock.Benchmarks.Disk;
using M1vClock.Benchmarks.Memory;
using M1vClock.Benchmarks.Results;
using M1vClock.Core.Settings;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Benchmarks.Tests;

internal static class Samples
{
    public static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    public static readonly BenchmarkMachine Machine = new("AMD Ryzen 7 5700X", "AMD Radeon RX 5500 XT", 16, 31.9, "Windows 11 26200", "0.1.0");

    public static BenchmarkRecord Record(
        string test = BenchmarkTests.CpuMulti,
        double score = 3600,
        DateTimeOffset? at = null,
        Guid? id = null,
        BenchmarkMachine? machine = null,
        IReadOnlyDictionary<string, double>? metrics = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            At = at ?? Now.AddMinutes(-5),
            Test = test,
            ScoreVersion = BenchmarkCatalog.Find(test)?.ScoreVersion ?? 1,
            Score = score,
            Metrics = metrics ?? new Dictionary<string, double> { ["seconds"] = 14.4, ["passes"] = 1, ["threads"] = 16 },
            Machine = machine ?? Machine,
        };
}

public sealed class BenchmarkCatalogTests
{
    [Fact]
    public void EveryTestHasAUniqueIdAndUniqueMetricKeys()
    {
        Assert.Equal(BenchmarkCatalog.All.Count, BenchmarkCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.All(BenchmarkCatalog.All, t => Assert.Equal(t.Metrics.Count, t.Metrics.Select(m => m.Key).Distinct().Count()));
    }

    [Fact]
    public void EveryTestConstantIsInTheCatalog()
    {
        var constants = typeof(BenchmarkTests).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.Equal(constants.Order(), BenchmarkCatalog.All.Select(t => t.Id).Order());
    }

    [Fact]
    public void ThePathTracerMetricsMatchTheCatalogExactly()
    {
        var keys = BenchmarkCatalog.MetricsOf(new PathTraceResult(16, 14.4, 530_000_000, 1)).Keys;

        Assert.Equal(CatalogKeys(BenchmarkTests.CpuMulti), keys.Order());
        Assert.Equal(CatalogKeys(BenchmarkTests.CpuSingle), keys.Order());
    }

    [Fact]
    public void TheSimulationMetricsMatchTheCatalogExactly()
    {
        var keys = BenchmarkCatalog.MetricsOf(new GameSimulationSummary(150, 90, 2.5, 4, 1500, 30, [], 3, 8), 30_000).Keys;

        Assert.Equal(CatalogKeys(BenchmarkTests.GameSimulation), keys.Order());
    }

    [Fact]
    public void TheMemoryAndLoadingMetricsMatchTheCatalogExactly()
    {
        Assert.Equal(CatalogKeys(BenchmarkTests.Memory), BenchmarkCatalog.MetricsOf(new MemoryBenchmarkResult(90, 34, 33)).Keys.Order());
        Assert.Equal(CatalogKeys(BenchmarkTests.Loading), BenchmarkCatalog.MetricsOf(new LoadingBenchmarkResult(3000, 500, 8000, 1.2, "C:")).Keys.Order());
    }

    [Fact]
    public void TheDiskMetricsMatchTheCatalogExactlyAndCarryTheScoreVersion()
    {
        var result = new DiskBenchmarkResult("C:", 1024, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

        Assert.Equal(CatalogKeys(BenchmarkTests.Disk), BenchmarkCatalog.MetricsOf(result).Keys.Order());
        Assert.Equal(DiskBenchmarkResult.ScoreVersion, BenchmarkCatalog.Find(BenchmarkTests.Disk)!.ScoreVersion);
    }

    [Fact]
    public void LoadingTimeIsTheOnlyScoreWhereLowerIsBetter()
    {
        Assert.Equal([BenchmarkTests.Loading], BenchmarkCatalog.All.Where(t => !t.HigherIsBetter).Select(t => t.Id));
    }

    [Fact]
    public void AnUnknownTestOrMetricIsNotFound()
    {
        Assert.Null(BenchmarkCatalog.Find("nao.existe"));
        Assert.Null(BenchmarkCatalog.FindMetric(BenchmarkTests.CpuMulti, "nao_existe"));
        Assert.Null(BenchmarkCatalog.FindMetric("nao.existe", "seconds"));
    }

    private static IEnumerable<string> CatalogKeys(string test) => BenchmarkCatalog.Find(test)!.Metrics.Select(m => m.Key).Order();
}

public sealed class BenchmarkComparerTests
{
    [Fact]
    public void AHigherScoreIsBetterAndTheChangeIsRelativeToTheBaseline()
    {
        var comparison = BenchmarkComparer.Compare(Samples.Record(score: 3000), Samples.Record(score: 3300));

        Assert.Equal(ComparisonProblem.None, comparison.Problem);
        Assert.Equal(10, comparison.ScoreChangePercent, 9);
        Assert.Equal(ComparisonVerdict.Better, comparison.ScoreVerdict);
    }

    [Fact]
    public void ALowerScoreIsWorse()
    {
        var comparison = BenchmarkComparer.Compare(Samples.Record(score: 3000), Samples.Record(score: 2400));

        Assert.Equal(ComparisonVerdict.Worse, comparison.ScoreVerdict);
    }

    [Fact]
    public void ADifferenceInsideTheNoiseMarginIsNotCalledBetterOrWorse()
    {
        Assert.Equal(ComparisonVerdict.WithinNoise, BenchmarkComparer.Compare(Samples.Record(score: 3000), Samples.Record(score: 3085)).ScoreVerdict);
        Assert.Equal(ComparisonVerdict.WithinNoise, BenchmarkComparer.Compare(Samples.Record(score: 3000), Samples.Record(score: 2915)).ScoreVerdict);
    }

    [Fact]
    public void ADifferenceExactlyAtTheNoiseMarginCounts()
    {
        Assert.Equal(ComparisonVerdict.Better, BenchmarkComparer.Compare(Samples.Record(score: 100), Samples.Record(score: 103)).ScoreVerdict);
    }

    [Fact]
    public void ForLoadingTimeLowerIsBetter()
    {
        var faster = BenchmarkComparer.Compare(Samples.Record(BenchmarkTests.Loading, 2.0, metrics: new Dictionary<string, double>()), Samples.Record(BenchmarkTests.Loading, 1.5, metrics: new Dictionary<string, double>()));
        var slower = BenchmarkComparer.Compare(Samples.Record(BenchmarkTests.Loading, 1.5, metrics: new Dictionary<string, double>()), Samples.Record(BenchmarkTests.Loading, 2.0, metrics: new Dictionary<string, double>()));

        Assert.Equal(ComparisonVerdict.Better, faster.ScoreVerdict);
        Assert.Equal(ComparisonVerdict.Worse, slower.ScoreVerdict);
    }

    [Fact]
    public void EachMetricIsJudgedByItsOwnDirection()
    {
        var before = Samples.Record(BenchmarkTests.GameSimulation, 150, metrics: new Dictionary<string, double> { ["average_fps"] = 150, ["main_ms"] = 2.5, ["unknown"] = 1 });
        var after = Samples.Record(BenchmarkTests.GameSimulation, 180, metrics: new Dictionary<string, double> { ["average_fps"] = 180, ["main_ms"] = 2.0, ["unknown"] = 5 });

        var comparison = BenchmarkComparer.Compare(before, after);

        Assert.Equal(["average_fps", "main_ms"], comparison.Metrics.Select(m => m.Key));
        Assert.All(comparison.Metrics, m => Assert.Equal(ComparisonVerdict.Better, m.Verdict));
    }

    [Fact]
    public void ARecordFromAnotherTestCannotBeCompared()
    {
        var comparison = BenchmarkComparer.Compare(Samples.Record(BenchmarkTests.CpuMulti), Samples.Record(BenchmarkTests.CpuSingle));

        Assert.Equal(ComparisonProblem.DifferentTests, comparison.Problem);
        Assert.Empty(comparison.Metrics);
    }

    [Fact]
    public void ADifferentScoreVersionCannotBeCompared()
    {
        var comparison = BenchmarkComparer.Compare(Samples.Record(), Samples.Record() with { ScoreVersion = 2 });

        Assert.Equal(ComparisonProblem.DifferentScoreVersions, comparison.Problem);
    }

    [Fact]
    public void TheSameMachineIsRecognizedEvenWithAnotherAppVersionOrOsBuild()
    {
        var updated = Samples.Machine with { AppVersion = "0.2.0", OsVersion = "Windows 11 26300" };

        Assert.True(BenchmarkComparer.Compare(Samples.Record(), Samples.Record(machine: updated)).SameMachine);
        Assert.False(BenchmarkComparer.Compare(Samples.Record(), Samples.Record(machine: Samples.Machine with { Cpu = "Intel Core i5" })).SameMachine);
        Assert.False(BenchmarkComparer.Compare(Samples.Record(), Samples.Record(machine: Samples.Machine with { MemoryGigabytes = 16 })).SameMachine);
    }
}

public sealed class BenchmarkExchangeTests : IDisposable
{
    private readonly ScratchDirectory _folder = new();

    public void Dispose() => _folder.Dispose();

    private string FilePath(string name = "export.json") => Path.Combine(_folder.Path, name);

    [Fact]
    public void WhatIsExportedComesBackAsImported()
    {
        var original = new[] { Samples.Record(score: 3600), Samples.Record(BenchmarkTests.GameSimulation, 150, metrics: new Dictionary<string, double> { ["average_fps"] = 150 }) };
        BenchmarkExchange.Write(FilePath(), original, Samples.Now);

        var outcome = BenchmarkExchange.Read(FilePath(), Samples.Now);

        Assert.Equal(ImportFailure.None, outcome.Failure);
        Assert.Equal(0, outcome.Rejected);
        Assert.Equal(original.Select(r => r.Id).Order(), outcome.Accepted.Select(r => r.Id).Order());
        Assert.All(outcome.Accepted, r => Assert.True(r.Imported));
        Assert.Equal(3600, outcome.Accepted.Single(r => r.Test == BenchmarkTests.CpuMulti).Score);
    }

    [Fact]
    public void AMissingFileIsReported()
    {
        Assert.Equal(ImportFailure.Missing, BenchmarkExchange.Read(FilePath("nada.json"), Samples.Now).Failure);
    }

    [Fact]
    public void AFileThatIsNotJsonIsRefusedWithItsCause()
    {
        File.WriteAllText(FilePath(), "isto não é json");

        var outcome = BenchmarkExchange.Read(FilePath(), Samples.Now);

        Assert.Equal(ImportFailure.NotValid, outcome.Failure);
        Assert.NotNull(outcome.Cause);
    }

    [Fact]
    public void JsonOfAnotherKindIsRefused()
    {
        File.WriteAllText(FilePath(), "{\"Format\":\"outra.coisa\",\"SchemaVersion\":1,\"Records\":[]}");

        Assert.Equal(ImportFailure.WrongFormat, BenchmarkExchange.Read(FilePath(), Samples.Now).Failure);
    }

    [Fact]
    public void AFileFromANewerVersionIsRefusedInsteadOfGuessing()
    {
        File.WriteAllText(FilePath(), "{\"Format\":\"m1vclock.benchmarks\",\"SchemaVersion\":99,\"Records\":[]}");

        Assert.Equal(ImportFailure.NewerSchema, BenchmarkExchange.Read(FilePath(), Samples.Now).Failure);
    }

    [Fact]
    public void AHugeFileIsRefusedWithoutBeingRead()
    {
        using (var stream = File.Create(FilePath()))
        {
            stream.SetLength(BenchmarkExchange.MaximumFileBytes + 1);
        }

        Assert.Equal(ImportFailure.TooLarge, BenchmarkExchange.Read(FilePath(), Samples.Now).Failure);
    }

    [Fact]
    public void MoreRecordsThanTheLimitAreRefused()
    {
        var records = Enumerable.Range(0, BenchmarkExchange.MaximumRecords + 1).Select(_ => Samples.Record());
        BenchmarkExchange.Write(FilePath(), records, Samples.Now);

        Assert.Equal(ImportFailure.TooMany, BenchmarkExchange.Read(FilePath(), Samples.Now).Failure);
    }

    [Fact]
    public void RecordsThatLieAreRejectedAndTheGoodOnesStay()
    {
        var good = Samples.Record();
        var bad = new[]
        {
            Samples.Record(test: "inventado.teste"),
            Samples.Record(score: 1e15),
            Samples.Record(score: -1),
            Samples.Record(at: Samples.Now.AddDays(30)),
            Samples.Record(at: new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Samples.Record(id: Guid.Empty),
            Samples.Record() with { ScoreVersion = 77 },
            Samples.Record(machine: Samples.Machine with { LogicalProcessors = 0 }),
            Samples.Record(machine: Samples.Machine with { MemoryGigabytes = -4 }),
        };
        BenchmarkExchange.Write(FilePath(), bad.Append(good), Samples.Now);

        var outcome = BenchmarkExchange.Read(FilePath(), Samples.Now);

        Assert.Equal(bad.Length, outcome.Rejected);
        Assert.Equal([good.Id], outcome.Accepted.Select(r => r.Id));
    }

    [Fact]
    public void UnknownMetricsAndAbsurdValuesAreDroppedFromAnAcceptedRecord()
    {
        var record = Samples.Record(metrics: new Dictionary<string, double> { ["seconds"] = 14.4, ["rootkit"] = 1, ["passes"] = 1e20, ["threads"] = 16 });

        var clean = BenchmarkExchange.Sanitize(record, Samples.Now)!;

        Assert.Equal(["seconds", "threads"], clean.Metrics.Keys.Order());
    }

    [Fact]
    public void TextIsTrimmedAndStrippedOfControlCharacters()
    {
        var machine = Samples.Machine with { Cpu = "AMD\u0007 Ryzen\r\n" + new string('x', 500), Gpu = "   " };
        var record = Samples.Record(machine: machine) with { Note = "nota\u001b[31m vermelha" };

        var clean = BenchmarkExchange.Sanitize(record, Samples.Now)!;

        Assert.Equal(BenchmarkExchange.MaximumTextLength, clean.Machine.Cpu.Length);
        Assert.StartsWith("AMD Ryzen", clean.Machine.Cpu, StringComparison.Ordinal);
        Assert.Equal(string.Empty, clean.Machine.Gpu);
        Assert.Equal("nota[31m vermelha", clean.Note);
    }

    [Fact]
    public void TheSameIdTwiceInOneFileCountsOnce()
    {
        var id = Guid.NewGuid();
        BenchmarkExchange.Write(FilePath(), [Samples.Record(id: id), Samples.Record(id: id, score: 1)], Samples.Now);

        var outcome = BenchmarkExchange.Read(FilePath(), Samples.Now);

        Assert.Single(outcome.Accepted);
        Assert.Equal(1, outcome.Rejected);
    }

    [Fact]
    public void ARecordClaimingToBeImportedIsStillTreatedByTheImportAsImported()
    {
        BenchmarkExchange.Write(FilePath(), [Samples.Record() with { Imported = false }], Samples.Now);

        Assert.True(BenchmarkExchange.Read(FilePath(), Samples.Now).Accepted[0].Imported);
    }

    [Fact]
    public void AFileWithNullRecordsIsHandledWithoutCrashing()
    {
        File.WriteAllText(FilePath(), "{\"Format\":\"m1vclock.benchmarks\",\"SchemaVersion\":1,\"Records\":[null,{}]}");

        var outcome = BenchmarkExchange.Read(FilePath(), Samples.Now);

        Assert.Empty(outcome.Accepted);
        Assert.True(outcome.Failure != ImportFailure.None || outcome.Rejected == 2);
    }

    [Fact]
    public void TheExportedFileCarriesNoComputerNameOrUserName()
    {
        BenchmarkExchange.Write(FilePath(), [Samples.Record()], Samples.Now);

        var text = File.ReadAllText(FilePath());

        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(text);
        Assert.Equal(BenchmarkExchange.FormatName, document.RootElement.GetProperty("Format").GetString());
    }
}

public sealed class BenchmarkHistoryTests : IDisposable
{
    private readonly ScratchDirectory _folder = new();
    private readonly FakeTimeProvider _clock = new(Samples.Now);
    private readonly CollectingLogger<BenchmarkHistory> _logger = new();

    public void Dispose() => _folder.Dispose();

    private string StorePath => Path.Combine(_folder.Path, "benchmarks.json");

    private BenchmarkHistory Open()
    {
        var history = new BenchmarkHistory(BenchmarkHistory.CreateStore(StorePath, _logger), _clock, _logger);
        history.Load();
        return history;
    }

    [Fact]
    public void ANewHistoryStartsEmpty()
    {
        var history = Open();

        Assert.Empty(history.Records);
        Assert.Equal(SettingsLoadStatus.Created, history.LoadStatus);
    }

    [Fact]
    public void WhatWasAddedSurvivesClosingAndReopening()
    {
        var first = Open();
        var record = Samples.Record(score: 3650);
        first.Add(record);

        var second = Open();

        var loaded = Assert.Single(second.Records);
        Assert.Equal(record.Id, loaded.Id);
        Assert.Equal(3650, loaded.Score);
        Assert.Equal(Samples.Machine, loaded.Machine);
        Assert.Equal(record.Metrics, loaded.Metrics);
    }

    [Fact]
    public void RecordsComeNewestFirstWhateverTheOrderTheyWereAdded()
    {
        var history = Open();
        var oldest = Samples.Record(at: Samples.Now.AddDays(-3));
        var newest = Samples.Record(at: Samples.Now.AddDays(-1));
        history.Add(oldest);
        history.Add(newest);

        Assert.Equal([newest.Id, oldest.Id], Open().Records.Select(r => r.Id));
    }

    [Fact]
    public void AnOlderRecordAddedLaterStillSitsBehindTheNewerOneWithoutReopening()
    {
        var history = Open();
        var newest = Samples.Record(at: Samples.Now.AddDays(-1));
        var oldest = Samples.Record(at: Samples.Now.AddDays(-3));

        history.Add(newest);
        history.Add(oldest);

        Assert.Equal([newest.Id, oldest.Id], history.Records.Select(r => r.Id));
    }

    [Fact]
    public void ThereIsNoLimitOfTenAsInTheOriginal()
    {
        var history = Open();
        for (var i = 0; i < 60; i++)
        {
            history.Add(Samples.Record(at: Samples.Now.AddMinutes(-i)));
        }

        Assert.Equal(60, Open().Records.Count);
    }

    [Fact]
    public void OnlyTheOldestAreDroppedWhenTheHardLimitIsReached()
    {
        var history = Open();
        history.AddRange(Enumerable.Range(0, BenchmarkHistory.MaximumRecords + 3).Select(i => Samples.Record(at: Samples.Now.AddMinutes(-i - 1))));

        var records = Open().Records;

        Assert.Equal(BenchmarkHistory.MaximumRecords, records.Count);
        Assert.Equal(Samples.Now.AddMinutes(-1), records[0].At);
    }

    [Fact]
    public void RemovingOneRecordLeavesTheOthers()
    {
        var history = Open();
        var keep = Samples.Record();
        var drop = Samples.Record();
        history.Add(keep);
        history.Add(drop);

        Assert.True(history.Remove(drop.Id));
        Assert.False(history.Remove(drop.Id));

        Assert.Equal([keep.Id], Open().Records.Select(r => r.Id));
    }

    [Fact]
    public void ClearingEmptiesTheFileToo()
    {
        var history = Open();
        history.Add(Samples.Record());

        history.Clear();

        Assert.Empty(Open().Records);
    }

    [Fact]
    public void TheChangedEventFiresOnEveryChange()
    {
        var history = Open();
        var count = 0;
        history.Changed += (_, _) => count++;
        var record = Samples.Record();

        history.Add(record);
        history.AddRange([Samples.Record()]);
        history.Remove(record.Id);
        history.Clear();

        Assert.Equal(4, count);
    }

    [Fact]
    public void RecordsAlreadyKnownAreNotAddedTwice()
    {
        var history = Open();
        var record = Samples.Record();
        history.Add(record);

        Assert.Equal(0, history.AddRange([record]));
        Assert.Single(history.Records);
    }

    [Fact]
    public void TheBestOfATestIsTheHighestScoreExceptWhereLowerIsBetter()
    {
        var history = Open();
        history.Add(Samples.Record(score: 3000));
        history.Add(Samples.Record(score: 3700));
        history.Add(Samples.Record(score: 3500));
        history.Add(Samples.Record(BenchmarkTests.Loading, 2.4, metrics: new Dictionary<string, double>()));
        history.Add(Samples.Record(BenchmarkTests.Loading, 1.8, metrics: new Dictionary<string, double>()));

        Assert.Equal(3700, history.Best(BenchmarkTests.CpuMulti)!.Score);
        Assert.Equal(1.8, history.Best(BenchmarkTests.Loading)!.Score);
        Assert.Null(history.Best(BenchmarkTests.Disk));
        Assert.Null(history.Best("nao.existe"));
    }

    [Fact]
    public void ARecordFromAnOlderScoreVersionNeverCountsAsTheBest()
    {
        var history = Open();
        history.Add(Samples.Record(BenchmarkTests.Disk, 9000) with { ScoreVersion = 1 });
        history.Add(Samples.Record(BenchmarkTests.Disk, 4000, metrics: new Dictionary<string, double>()));

        Assert.Equal(4000, history.Best(BenchmarkTests.Disk)!.Score);
    }

    [Fact]
    public void ImportAddsValidRecordsAndFlagsThemAsFromAnotherPc()
    {
        var source = Open();
        source.Add(Samples.Record(machine: Samples.Machine with { Cpu = "Intel Core i7-12700" }));
        var path = Path.Combine(_folder.Path, "outro-pc.json");
        source.Export(path);
        source.Clear();

        var outcome = source.Import(path);

        Assert.Equal(ImportFailure.None, outcome.Failure);
        var imported = Assert.Single(source.Records);
        Assert.True(imported.Imported);
        Assert.Equal("Intel Core i7-12700", imported.Machine.Cpu);
    }

    [Fact]
    public void AFailedImportLeavesTheHistoryUntouchedAndIsLogged()
    {
        var history = Open();
        history.Add(Samples.Record());
        File.WriteAllText(Path.Combine(_folder.Path, "ruim.json"), "lixo");

        var outcome = history.Import(Path.Combine(_folder.Path, "ruim.json"));

        Assert.Equal(ImportFailure.NotValid, outcome.Failure);
        Assert.Single(history.Records);
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 4101);
    }

    [Fact]
    public void ExportingChosenRecordsLeavesTheRestOut()
    {
        var history = Open();
        var chosen = Samples.Record();
        history.Add(chosen);
        history.Add(Samples.Record());
        var path = Path.Combine(_folder.Path, "um.json");

        history.Export(path, [chosen.Id]);

        Assert.Equal([chosen.Id], BenchmarkExchange.Read(path, Samples.Now).Accepted.Select(r => r.Id));
    }

    [Fact]
    public void ACorruptHistoryFileIsRecoveredFromTheBackup()
    {
        var history = Open();
        history.Add(Samples.Record(score: 3500));
        history.Add(Samples.Record(score: 3600));
        File.WriteAllText(StorePath, "{ corrompido");

        var reopened = Open();

        Assert.Equal(SettingsLoadStatus.RecoveredFromBackup, reopened.LoadStatus);
        Assert.NotEmpty(reopened.Records);
    }

    [Fact]
    public void AFailureToWriteIsRememberedAndTheRecordStaysInMemory()
    {
        var blocker = Path.Combine(_folder.Path, "arquivo");
        File.WriteAllText(blocker, "x");
        var store = BenchmarkHistory.CreateStore(Path.Combine(blocker, "benchmarks.json"), _logger);
        var history = new BenchmarkHistory(store, _clock, _logger);
        history.Load();

        history.Add(Samples.Record());

        Assert.True(history.SaveFailed);
        Assert.Single(history.Records);
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 4102);
    }
}
