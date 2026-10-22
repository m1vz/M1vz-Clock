using System.IO;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Pages.Performance;
using M1vClock.App.Services;
using M1vClock.Benchmarks.Io;
using M1vClock.Benchmarks.Results;
using M1vClock.Benchmarks.Safety;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.App.Tests;

internal sealed class FakeBenchJob : IBenchJob, IAvailabilityNotifier
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event EventHandler? AvailabilityChanged;

    public void RaiseAvailabilityChanged() => AvailabilityChanged?.Invoke(this, EventArgs.Empty);

    public string Id { get; init; } = "fake";

    public string IconKey => "Icon.Cpu";

    public string Title { get; init; } = "Teste falso";

    public string Description => "Descrição do teste falso";

    public IReadOnlyList<BenchOptionViewModel> Options { get; init; } = [];

    public IReadOnlyList<string> HistoryTests { get; init; } = [BenchmarkTests.CpuMulti];

    public ThermalComponent? Thermal { get; init; }

    public IPreviewSource? Preview { get; set; }

    public string? UnavailableReason { get; set; }

    public BenchPoll NextPoll { get; set; } = new("Bench.Phase.CpuMulti", 0.25, string.Empty);

    public int Refreshes { get; private set; }

    public int Runs { get; private set; }

    public Task Started => _started.Task;

    public Func<CancellationToken, Task<BenchJobOutcome>>? Body { get; set; }

    public void Refresh() => Refreshes++;

    public BenchPoll Poll() => NextPoll;

    public Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken)
    {
        Runs++;
        _started.TrySetResult();
        return Body is not null ? Body(cancellationToken) : Task.FromResult(Outcome());
    }

    public static BenchJobOutcome Outcome(double score = 3600, string test = BenchmarkTests.CpuMulti) => new(
        [new BenchJobDraft(test, BenchmarkCatalog.Find(test)!.ScoreVersion, score, new Dictionary<string, double> { ["seconds"] = 14.4 })],
        [new InfoRowViewModel("Linha", "Valor")],
        ["Nota do teste"]);

    public static async Task<BenchJobOutcome> WaitForCancellation(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return Outcome();
    }
}

internal sealed class StubJobSource(params IBenchJob[] jobs) : IBenchJobSource
{
    public IReadOnlyList<IBenchJob> Create() => jobs;
}

internal sealed class StubMachine : IMachineProfile
{
    public static readonly BenchmarkMachine Profile = new("AMD Ryzen 7 5700X", "AMD Radeon RX 5500 XT", 16, 31.9, "Windows 11 26200", "0.1.0");

    public Task<BenchmarkMachine> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Profile);
}

internal sealed class StubDialogs : IFileDialogs
{
    public string? OpenPath { get; set; }

    public string? SavePath { get; set; }

    public int OpenCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public string? PickOpen(string title, string filter)
    {
        OpenCalls++;
        return OpenPath;
    }

    public string? PickSave(string title, string filter, string suggestedName)
    {
        SaveCalls++;
        return SavePath;
    }
}

internal sealed class StubProbe : IComponentProbe
{
    public float? CpuTemperature { get; set; } = 55;

    public float? GpuTemperature { get; set; } = 50;

    public ComponentSample? Read(ThermalComponent component)
    {
        var temperature = component == ThermalComponent.Cpu ? CpuTemperature : GpuTemperature;
        return temperature is null ? null : new ComponentSample(temperature, 90, 100, 4400);
    }
}

public sealed class BenchmarksPageViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("m1vclock-bench-page-tests");
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeSensorService _sensors = new() { State = SensorServiceState.Running };
    private readonly StubProbe _probe = new();
    private readonly StubDialogs _dialogs = new();
    private readonly FakeBenchJob _cpu = new() { Id = "cpu", Title = "Render", Thermal = ThermalComponent.Cpu };
    private readonly FakeBenchJob _loading = new() { Id = "loading", Title = "Carregamento", HistoryTests = [BenchmarkTests.Loading] };
    private readonly HeavyTestGate _gate;
    private readonly SafetyMonitor _monitor;
    private readonly BenchmarkHistory _history;
    private readonly AppSettingsService _settings;
    private readonly BenchmarksPageViewModel _page;

    public BenchmarksPageViewModelTests()
    {
        _gate = new HeavyTestGate(_sensors, _clock, NullLogger<HeavyTestGate>.Instance);
        _monitor = new SafetyMonitor(_probe, _clock, NullLogger<SafetyMonitor>.Instance);
        _history = new BenchmarkHistory(BenchmarkHistory.CreateStore(Path.Combine(_temp.FullName, "benchmarks.json"), NullLogger.Instance), _clock, NullLogger<BenchmarkHistory>.Instance);
        _history.Load();
        var store = new SettingsStore<AppSettings>(Path.Combine(_temp.FullName, "settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);
        _settings = new AppSettingsService(store, NullLogger<AppSettingsService>.Instance);
        _page = new BenchmarksPageViewModel(
            new StubJobSource(_cpu, _loading),
            _gate,
            _monitor,
            _history,
            new StubMachine(),
            _dialogs,
            _settings,
            _clock,
            NullLogger<BenchmarksPageViewModel>.Instance);
    }

    public void Dispose()
    {
        _page.Dispose();
        _monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _temp.Delete(recursive: true);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "A condição não chegou a tempo.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private static BenchmarkRecord Record(string test, double score, int minutesAgo = 10, BenchmarkMachine? machine = null, bool imported = false) => new()
    {
        Id = Guid.NewGuid(),
        At = Now.AddMinutes(-minutesAgo),
        Test = test,
        ScoreVersion = BenchmarkCatalog.Find(test)!.ScoreVersion,
        Score = score,
        Metrics = new Dictionary<string, double> { ["seconds"] = 14.4 },
        Machine = machine ?? StubMachine.Profile,
        Imported = imported,
    };

    [Fact]
    public void ThePageStartsOnTheFirstTestReadyToRun()
    {
        Assert.Equal("Render", _page.TestTitle);
        Assert.True(_page.CanStart);
        Assert.True(_page.CanToggle);
        Assert.True(_page.IsIdle);
        Assert.Equal(Loc.Get("Bench.Start"), _page.ButtonText);
        Assert.False(_page.HasResult);
        Assert.False(_page.HasHistory);
    }

    [Fact]
    public void ChoosingAnotherTestSwitchesTheJobAndItsHistory()
    {
        _history.Add(Record(BenchmarkTests.CpuMulti, 3600));
        _history.Add(Record(BenchmarkTests.Loading, 0.3));

        _page.SelectedIndex = 1;

        Assert.Equal("Carregamento", _page.TestTitle);
        Assert.Same(_loading, _page.Selected);
        var row = Assert.Single(_page.HistoryRows);
        Assert.Equal(BenchmarkTests.Loading, row.Record.Test);
    }

    [Fact]
    public async Task ARunSavesTheResultInTheHistoryWithTheMachineAndShowsTheScore()
    {
        await _page.ToggleAsync();

        var record = Assert.Single(_history.Records);
        Assert.Equal(BenchmarkTests.CpuMulti, record.Test);
        Assert.Equal(3600, record.Score);
        Assert.Equal(StubMachine.Profile, record.Machine);
        Assert.Equal(Now, record.At);
        Assert.False(record.Imported);
        Assert.True(_page.HasResult);
        Assert.Equal(BenchNames.Value(BenchmarkTests.CpuMulti, 3600), _page.ScoreHeadline);
        Assert.Equal(Loc.Get("Bench.Saved"), _page.Message);
        Assert.True(_page.MessageIsGood);
        Assert.False(_page.IsRunning);
        Assert.False(_gate.IsBusy);
        Assert.Equal(Loc.Get("Bench.Versus.First"), _page.VersusPrevious);
        Assert.Single(_page.HistoryRows);
    }

    [Fact]
    public async Task WhileRunningTheGeneralSensorReadingIsPausedAndGivenBackAfterwards()
    {
        _cpu.Body = FakeBenchJob.WaitForCancellation;
        var run = _page.ToggleAsync();
        await _cpu.Started;

        Assert.Equal(1, _sensors.Pauses);
        Assert.Equal(0, _sensors.PauseReleases);
        Assert.True(_page.IsRunning);
        Assert.False(_page.CanStart);
        Assert.Equal(Loc.Get("Bench.Cancel"), _page.ButtonText);

        await _page.ToggleAsync();
        await run;

        Assert.Equal(1, _sensors.PauseReleases);
        Assert.False(_gate.IsBusy);
    }

    [Fact]
    public async Task CancellingKeepsNothingAndSaysSo()
    {
        _cpu.Body = FakeBenchJob.WaitForCancellation;
        var run = _page.ToggleAsync();
        await _cpu.Started;

        await _page.ToggleAsync();
        await run;

        Assert.Empty(_history.Records);
        Assert.Equal(Loc.Get("Bench.Stop.User"), _page.Message);
        Assert.False(_page.HasResult);
        Assert.False(_page.IsRunning);
    }

    [Fact]
    public async Task TheEscapeKeyStopsARunningTest()
    {
        _cpu.Body = FakeBenchJob.WaitForCancellation;
        _page.OnNavigatedTo();
        var run = _page.ToggleAsync();
        await _cpu.Started;

        _gate.RequestStop();
        await run;

        Assert.Empty(_history.Records);
        Assert.Equal(Loc.Get("Bench.Stop.User"), _page.Message);
    }

    [Fact]
    public async Task LeavingThePageStopsTheRunningTest()
    {
        _cpu.Body = FakeBenchJob.WaitForCancellation;
        var run = _page.ToggleAsync();
        await _cpu.Started;

        _page.OnNavigatedFrom();
        await run;

        Assert.Empty(_history.Records);
        Assert.False(_gate.IsBusy);
    }

    [Fact]
    public async Task ProgressPhaseAndElapsedTimeFollowThePollWhileRunning()
    {
        _cpu.NextPoll = new BenchPoll("Bench.Phase.CpuMulti", 0.5, "detalhe do teste");
        _cpu.Body = FakeBenchJob.WaitForCancellation;
        var run = _page.ToggleAsync();
        await _cpu.Started;

        await WaitUntilAsync(() =>
        {
            _clock.Advance(TimeSpan.FromMilliseconds(100));
            return _page.Progress > 0;
        });

        Assert.Equal(0.5, _page.Progress);
        Assert.Equal(Loc.Get("Bench.Phase.CpuMulti"), _page.PhaseText);
        Assert.Equal("detalhe do teste", _page.DetailText);
        Assert.False(string.IsNullOrEmpty(_page.ElapsedText));

        await _page.ToggleAsync();
        await run;
        Assert.Equal(0, _page.Progress);
    }

    [Fact]
    public async Task WhenNoDetailIsGivenTheRemainingTimeIsEstimatedFromTheProgress()
    {
        _cpu.NextPoll = new BenchPoll("Bench.Phase.CpuMulti", 0.25, string.Empty);
        _cpu.Body = FakeBenchJob.WaitForCancellation;
        var run = _page.ToggleAsync();
        await _cpu.Started;

        await WaitUntilAsync(() =>
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            return _page.DetailText.Length > 0;
        });

        Assert.StartsWith(Loc.Get("Bench.Remaining").Split('{')[0], _page.DetailText, StringComparison.Ordinal);
        await _page.ToggleAsync();
        await run;
    }

    [Fact]
    public async Task APreviewOfTheJobIsExposedWhileItRuns()
    {
        var preview = new FakePreview();
        _cpu.Preview = preview;
        _cpu.Body = FakeBenchJob.WaitForCancellation;
        var run = _page.ToggleAsync();
        await _cpu.Started;

        await WaitUntilAsync(() =>
        {
            _clock.Advance(TimeSpan.FromMilliseconds(100));
            return _page.Preview is not null;
        });

        Assert.Same(preview, _page.Preview);
        Assert.True(_page.ShowPreview);
        await _page.ToggleAsync();
        await run;
        Assert.Null(_page.Preview);
        Assert.False(_page.ShowPreview);
    }

    [Fact]
    public async Task ATestThatHeatsTheCpuStartsTheSafetyMonitorAndShowsLiveReadings()
    {
        _cpu.Body = FakeBenchJob.WaitForCancellation;
        var run = _page.ToggleAsync();
        await _cpu.Started;

        await WaitUntilAsync(() => _monitor.IsRunning && _monitor.Latest is not null);
        await WaitUntilAsync(() =>
        {
            _clock.Advance(TimeSpan.FromMilliseconds(100));
            return _page.TemperatureCard.Value != "—";
        });

        Assert.True(_page.ThermalProtected);
        Assert.True(_page.ShowLive);
        Assert.Equal("55", _page.TemperatureCard.Value.Split(',')[0]);
        await _page.ToggleAsync();
        await run;
        Assert.False(_monitor.IsRunning);
    }

    [Fact]
    public async Task ReachingTheCpuLimitCancelsTheRunAndNamesTheTemperature()
    {
        _probe.CpuTemperature = 92;
        _cpu.Body = FakeBenchJob.WaitForCancellation;

        await _page.ToggleAsync();

        Assert.Empty(_history.Records);
        Assert.Contains("92", _page.Message, StringComparison.Ordinal);
        Assert.Contains("85", _page.Message, StringComparison.Ordinal);
        Assert.True(_page.MessageIsWarning);
        Assert.False(_monitor.IsRunning);
        Assert.False(_gate.IsBusy);
    }

    [Fact]
    public async Task WithoutATemperatureSensorTheTestStillRunsButSaysThereIsNoProtection()
    {
        _probe.CpuTemperature = null;
        var protectionDuringRun = string.Empty;
        _cpu.Body = async token =>
        {
            await WaitUntilAsync(() => _page.IsRunning);
            protectionDuringRun = _page.ProtectionText;
            return FakeBenchJob.Outcome();
        };

        await _page.ToggleAsync();

        Assert.Equal(Loc.Get("Bench.Protection.Off"), protectionDuringRun);
        Assert.False(_page.ThermalProtected);
        Assert.Single(_history.Records);
        Assert.False(_monitor.IsRunning);
    }

    [Fact]
    public async Task ATestThatDoesNotHeatAnythingNeverStartsTheMonitor()
    {
        _page.SelectedIndex = 1;
        _loading.Body = _ => Task.FromResult(FakeBenchJob.Outcome(0.3, BenchmarkTests.Loading));

        await _page.ToggleAsync();

        Assert.False(_monitor.IsRunning);
        Assert.False(_page.HasProtectionText);
        Assert.Equal(BenchmarkTests.Loading, Assert.Single(_history.Records).Test);
    }

    [Fact]
    public async Task AnUnavailableTestExplainsWhyAndDoesNotTouchTheSensors()
    {
        _cpu.UnavailableReason = "Sem unidades";
        _page.OnNavigatedTo();

        await _page.ToggleAsync();

        Assert.False(_page.CanStart);
        Assert.True(_page.HasUnavailable);
        Assert.Equal("Sem unidades", _page.UnavailableText);
        Assert.Equal("Sem unidades", _page.Message);
        Assert.Equal(0, _cpu.Runs);
        Assert.Equal(0, _sensors.Pauses);
    }

    [Fact]
    public async Task AnotherHeavyTestRunningBlocksTheStartWithAClearMessage()
    {
        using var other = await _gate.EnterAsync("stress", TestContext.Current.CancellationToken);

        Assert.True(_page.OtherTestRunning);
        Assert.False(_page.CanStart);

        await _page.ToggleAsync();

        Assert.Equal(0, _cpu.Runs);
        Assert.Contains("stress", _page.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(InsufficientMemoryException), "Bench.Error.Memory")]
    [InlineData(typeof(UnauthorizedAccessException), "Bench.Error.Access")]
    [InlineData(typeof(IOException), "Bench.Error.Generic")]
    [InlineData(typeof(InvalidOperationException), "Bench.Error.Generic")]
    public async Task EveryFailureBecomesAShortMessageAndFreesEverything(Type exception, string key)
    {
        _cpu.Body = _ => Task.FromException<BenchJobOutcome>((Exception)Activator.CreateInstance(exception, "falhou")!);

        await _page.ToggleAsync();

        Assert.Equal(Loc.Get(key), _page.Message);
        Assert.True(_page.MessageIsCritical);
        Assert.Empty(_history.Records);
        Assert.False(_gate.IsBusy);
        Assert.False(_page.IsRunning);
    }

    [Fact]
    public async Task NotEnoughDiskSpaceHasItsOwnMessage()
    {
        _cpu.Body = _ => Task.FromException<BenchJobOutcome>(new InsufficientDiskSpaceException("C:", 2L << 30, 1L << 30));

        await _page.ToggleAsync();

        Assert.Equal(Loc.Get("Bench.Error.Space"), _page.Message);
    }

    [Fact]
    public async Task ASecondRunShowsHowItComparesWithThePreviousOneOnThisPc()
    {
        _history.Add(Record(BenchmarkTests.CpuMulti, 3000, minutesAgo: 60));
        _cpu.Body = _ => Task.FromResult(FakeBenchJob.Outcome(3300));

        await _page.ToggleAsync();

        Assert.Contains(BenchNames.Percent(10), _page.VersusPrevious, StringComparison.Ordinal);
        Assert.Contains(BenchNames.VerdictText(ComparisonVerdict.Better), _page.VersusPrevious, StringComparison.Ordinal);
        Assert.Equal(MetricState.Good, _page.VersusTone);
    }

    [Fact]
    public async Task ARunThatIsWorseIsFlaggedAsWorse()
    {
        _history.Add(Record(BenchmarkTests.CpuMulti, 3000, minutesAgo: 60));
        _cpu.Body = _ => Task.FromResult(FakeBenchJob.Outcome(2400));

        await _page.ToggleAsync();

        Assert.Equal(MetricState.Critical, _page.VersusTone);
        Assert.Contains(BenchNames.VerdictText(ComparisonVerdict.Worse), _page.VersusPrevious, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyResultsFromThisPcAreUsedAsThePreviousRun()
    {
        _history.Add(Record(BenchmarkTests.CpuMulti, 5000, minutesAgo: 60, machine: StubMachine.Profile with { Cpu = "Intel Core i9" }, imported: true));

        await _page.ToggleAsync();

        Assert.Equal(Loc.Get("Bench.Versus.First"), _page.VersusPrevious);
    }

    [Fact]
    public async Task ATestThatProducesTwoScoresShowsBoth()
    {
        _cpu.Body = _ => Task.FromResult(new BenchJobOutcome(
            [
                new BenchJobDraft(BenchmarkTests.CpuMulti, 1, 3600, new Dictionary<string, double>()),
                new BenchJobDraft(BenchmarkTests.CpuSingle, 1, 370, new Dictionary<string, double>()),
            ],
            [],
            []));

        await _page.ToggleAsync();

        Assert.True(_page.HasSecondScore);
        Assert.Equal(BenchNames.Value(BenchmarkTests.CpuSingle, 370), _page.SecondScoreHeadline);
        Assert.Equal(2, _history.Records.Count);
    }

    [Fact]
    public void TheHistoryBarsAreRelativeToTheBestAndInvertedWhereLowerIsBetter()
    {
        _history.Add(Record(BenchmarkTests.CpuMulti, 4000, minutesAgo: 30));
        _history.Add(Record(BenchmarkTests.CpuMulti, 2000, minutesAgo: 20));
        _history.Add(Record(BenchmarkTests.Loading, 0.2, minutesAgo: 30));
        _history.Add(Record(BenchmarkTests.Loading, 0.4, minutesAgo: 20));

        Assert.Equal([0.5, 1.0], _page.HistoryRows.Select(r => r.Fraction).Order());
        _page.SelectedIndex = 1;
        Assert.Equal([0.5, 1.0], _page.HistoryRows.Select(r => r.Fraction).Order());
    }

    [Fact]
    public void OnlyTheLatestResultsShowAtFirstAndMoreCanBeRevealed()
    {
        for (var i = 0; i < 20; i++)
        {
            _history.Add(Record(BenchmarkTests.CpuMulti, 3000 + i, minutesAgo: i + 1));
        }

        Assert.Equal(BenchmarksPageViewModel.HistoryPageSize, _page.HistoryRows.Count);
        Assert.True(_page.CanShowMore);

        _page.ShowMoreCommand.Execute(null);
        _page.ShowMoreCommand.Execute(null);

        Assert.Equal(20, _page.HistoryRows.Count);
        Assert.False(_page.CanShowMore);
        Assert.Equal(3000, _page.HistoryRows[0].Record.Score);
    }

    [Fact]
    public void ADeletedResultDisappearsFromThePageAndTheFile()
    {
        var keep = Record(BenchmarkTests.CpuMulti, 3600);
        var drop = Record(BenchmarkTests.CpuMulti, 3000);
        _history.Add(keep);
        _history.Add(drop);

        var row = _page.HistoryRows.Single(r => r.Record.Id == drop.Id);
        row.DeleteCommand!.Execute(row);

        Assert.Equal([keep.Id], _page.HistoryRows.Select(r => r.Record.Id));
        Assert.Equal([keep.Id], _history.Records.Select(r => r.Id));
    }

    [Fact]
    public void ChoosingAResultAsTheBaselineComparesItWithTheLatestOneOfTheSameTest()
    {
        var oldRun = Record(BenchmarkTests.CpuMulti, 3000, minutesAgo: 60);
        var newRun = Record(BenchmarkTests.CpuMulti, 3300, minutesAgo: 5);
        _history.Add(oldRun);
        _history.Add(newRun);

        var row = _page.HistoryRows.Single(r => r.Record.Id == oldRun.Id);
        row.SelectCommand!.Execute(row);

        Assert.True(_page.HasComparison);
        Assert.Contains(BenchNames.Percent(10), _page.Comparison!.StatusText, StringComparison.Ordinal);
        Assert.Equal(MetricState.Good, _page.Comparison.StatusTone);
        Assert.True(_page.HistoryRows.Single(r => r.Record.Id == oldRun.Id).IsBaseline);
    }

    [Fact]
    public void ChoosingTheSameBaselineAgainClosesTheComparison()
    {
        _history.Add(Record(BenchmarkTests.CpuMulti, 3000, minutesAgo: 60));
        _history.Add(Record(BenchmarkTests.CpuMulti, 3300, minutesAgo: 5));
        var row = _page.HistoryRows.Last();

        row.SelectCommand!.Execute(row);
        var again = _page.HistoryRows.Single(r => r.IsBaseline);
        again.SelectCommand!.Execute(again);

        Assert.False(_page.HasComparison);
    }

    [Fact]
    public void ComparingWithAnotherPcWarnsThatTheDifferenceIsTheMachine()
    {
        var other = StubMachine.Profile with { Cpu = "Intel Core i5-12400" };
        _history.Add(Record(BenchmarkTests.CpuMulti, 2500, minutesAgo: 60, machine: other, imported: true));
        _history.Add(Record(BenchmarkTests.CpuMulti, 3300, minutesAgo: 5));
        var row = _page.HistoryRows.Single(r => r.IsImported);

        row.SelectCommand!.Execute(row);

        Assert.Contains(Loc.Get("Bench.Compare.OtherMachine"), _page.Comparison!.Notes);
    }

    [Fact]
    public void ExportingWritesTheHistoryToTheChosenFile()
    {
        _history.Add(Record(BenchmarkTests.CpuMulti, 3600));
        var path = Path.Combine(_temp.FullName, "saida.json");
        _dialogs.SavePath = path;

        _page.ExportCommand.Execute(null);

        Assert.True(File.Exists(path));
        Assert.Contains("m1vclock.benchmarks", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.True(_page.MessageIsGood);
    }

    [Fact]
    public void CancellingTheSaveDialogDoesNothing()
    {
        _dialogs.SavePath = null;

        _page.ExportCommand.Execute(null);

        Assert.Equal(1, _dialogs.SaveCalls);
        Assert.False(_page.HasMessage);
    }

    [Fact]
    public void ExportingToAPlaceThatCannotBeWrittenShowsAShortError()
    {
        _history.Add(Record(BenchmarkTests.CpuMulti, 3600));
        var blocker = Path.Combine(_temp.FullName, "arquivo");
        File.WriteAllText(blocker, "x");
        _dialogs.SavePath = Path.Combine(blocker, "saida.json");

        _page.ExportCommand.Execute(null);

        Assert.Equal(Loc.Get("Bench.Export.Failed"), _page.Message);
        Assert.True(_page.MessageIsCritical);
    }

    [Fact]
    public void ImportingAFileFromAnotherPcAddsItsResultsAsImported()
    {
        var path = Path.Combine(_temp.FullName, "outro.json");
        BenchmarkExchange.Write(path, [Record(BenchmarkTests.CpuMulti, 2500, machine: StubMachine.Profile with { Cpu = "Intel Core i5" })], Now);
        _dialogs.OpenPath = path;

        _page.ImportCommand.Execute(null);

        var row = Assert.Single(_page.HistoryRows);
        Assert.True(row.IsImported);
        Assert.Contains("Intel Core i5", row.MachineText, StringComparison.Ordinal);
        Assert.Equal(Loc.Format("Bench.Import.Done", 1), _page.Message);
    }

    [Fact]
    public void ImportingSomethingThatIsNotAnExportShowsTheReason()
    {
        var path = Path.Combine(_temp.FullName, "lixo.json");
        File.WriteAllText(path, "{\"Format\":\"outra.coisa\",\"SchemaVersion\":1,\"Records\":[]}");
        _dialogs.OpenPath = path;

        _page.ImportCommand.Execute(null);

        Assert.Equal(Loc.Get("Bench.Import.Failure.WrongFormat"), _page.Message);
        Assert.True(_page.MessageIsCritical);
        Assert.Empty(_history.Records);
    }

    [Fact]
    public void RejectedRecordsInAnImportAreCountedInTheMessage()
    {
        var path = Path.Combine(_temp.FullName, "misto.json");
        BenchmarkExchange.Write(path, [Record(BenchmarkTests.CpuMulti, 2500), Record(BenchmarkTests.CpuMulti, 1e15)], Now);
        _dialogs.OpenPath = path;

        _page.ImportCommand.Execute(null);

        Assert.Equal(Loc.Format("Bench.Import.DoneWithRejected", 1, 1), _page.Message);
        Assert.True(_page.MessageIsWarning);
    }

    [Fact]
    public void OpeningThePageRefreshesTheDriveListsOfEveryTest()
    {
        _page.OnNavigatedTo();

        Assert.Equal(1, _cpu.Refreshes);
        Assert.Equal(1, _loading.Refreshes);
    }

    [Fact]
    public void WhenATestBecomesUnavailableLaterThePageFollows()
    {
        var changes = new List<string?>();
        _page.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _cpu.UnavailableReason = "Verificando…";
        _cpu.RaiseAvailabilityChanged();

        Assert.False(_page.CanStart);
        Assert.True(_page.HasUnavailable);
        Assert.Equal("Verificando…", _page.UnavailableText);
        Assert.Contains(nameof(BenchmarksPageViewModel.CanToggle), changes);

        _cpu.UnavailableReason = null;
        _cpu.RaiseAvailabilityChanged();

        Assert.True(_page.CanStart);
        Assert.False(_page.HasUnavailable);
    }

    [Fact]
    public void SelectingATestThatIsUnavailableDisablesTheButtonAtOnce()
    {
        _loading.UnavailableReason = "Sem unidades";
        var changes = new List<string?>();
        _page.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _page.SelectedIndex = 1;

        Assert.False(_page.CanToggle);
        Assert.Contains(nameof(BenchmarksPageViewModel.CanToggle), changes);

        _page.SelectedIndex = 0;

        Assert.True(_page.CanToggle);
    }

    [Fact]
    public void ANotificationFromATestThatIsNotOnScreenIsIgnored()
    {
        _loading.UnavailableReason = "Sem unidades";

        _loading.RaiseAvailabilityChanged();

        Assert.True(_page.CanStart);
        Assert.False(_page.HasUnavailable);
    }

    [Fact]
    public void ChangingTheTestWhileRunningIsIgnored()
    {
        _cpu.Body = FakeBenchJob.WaitForCancellation;
        _ = _page.ToggleAsync();

        _page.SelectedIndex = 1;

        Assert.Same(_cpu, _page.Selected);
    }

    private sealed class FakePreview : IPreviewSource
    {
        public int PixelWidth => 8;

        public int PixelHeight => 8;

        public void CopyPixels(Span<int> destination)
        {
        }

        public IReadOnlyList<PreviewRegion> ActiveRegions() => [];
    }
}

public sealed class BenchOptionViewModelTests
{
    [Fact]
    public void TheValueFollowsTheSelectedChoice()
    {
        var option = new BenchOptionViewModel("mode", "Bench.Option.Mode", [new BenchChoice("a", "A"), new BenchChoice("b", "B")]);
        var changes = new List<string?>();
        option.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        option.SelectedIndex = 1;

        Assert.Equal("b", option.Value);
        Assert.Contains(nameof(BenchOptionViewModel.Value), changes);
    }

    [Fact]
    public void AnInitialIndexOutsideTheListIsClamped()
    {
        var option = new BenchOptionViewModel("mode", "Bench.Option.Mode", [new BenchChoice("a", "A")], selectedIndex: 9);

        Assert.Equal(0, option.SelectedIndex);
        Assert.Equal("a", option.Value);
    }

    [Fact]
    public void TheSegmentIsWideEnoughForTheLongestLabel()
    {
        var narrow = new BenchOptionViewModel("m", "Bench.Option.Mode", [new BenchChoice("a", "A"), new BenchChoice("b", "B")]);
        var wide = new BenchOptionViewModel("m", "Bench.Option.Mode", [new BenchChoice("a", "Completo · 3 × 20 s"), new BenchChoice("b", "Rápido")]);

        Assert.Equal(2 * 96, narrow.SegmentWidth);
        Assert.True(wide.SegmentWidth > 2 * 96);
    }

    [Fact]
    public void AnOptionNeedsAtLeastOneChoice()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BenchOptionViewModel("m", "Bench.Option.Mode", []));
    }

    [Fact]
    public void TheChoiceTextIsWhatAccessibilityToolsRead()
    {
        Assert.Equal("Só multi-core", new BenchChoice("multi", "Só multi-core").ToString());
    }
}

public sealed class BenchNamesTests
{
    [Fact]
    public void EveryCatalogTestAndMetricHasATextInBothLanguages()
    {
        foreach (var test in BenchmarkCatalog.All)
        {
            Assert.True(Loc.Exists($"Bench.TestName.{test.Id.Replace('.', '_')}"), test.Id);
            foreach (var metric in test.Metrics)
            {
                Assert.True(Loc.Exists($"Bench.Metric.{metric.Key}"), $"{test.Id}/{metric.Key}");
            }
        }
    }

    [Fact]
    public void ScoresUseTheDecimalsAndUnitOfTheirTest()
    {
        Assert.Equal("3.600 pts", BenchNames.Value(BenchmarkTests.CpuMulti, 3600));
        Assert.Equal("144,3 fps", BenchNames.Value(BenchmarkTests.GameSimulation, 144.26));
        Assert.Equal("0,29 s", BenchNames.Value(BenchmarkTests.Loading, 0.294));
    }

    [Theory]
    [InlineData(12.34, "+12,3 %")]
    [InlineData(-5.0, "-5,0 %")]
    [InlineData(0, "0,0 %")]
    public void PercentagesCarryTheirSign(double change, string expected)
    {
        Assert.Equal(expected, BenchNames.Percent(change));
    }

    [Fact]
    public void EveryPhaseTheEnginesReportHasAText()
    {
        string[] phases =
        [
            "CpuMulti", "CpuSingle", "SimWarmup", "Sim", "MemoryLatency", "MemoryBandwidth", "LoadPrepare", "LoadPack", "LoadAntivirus", "LoadAssets",
            "DiskSeqWrite", "DiskSeqRead", "DiskSeqWriteQ8", "DiskSeqReadQ8", "DiskRandReadQ1", "DiskRandWriteQ1", "DiskRandReadQ8", "DiskRandReadQ32", "DiskRandWriteQ32", "Done",
        ];

        Assert.All(phases, p => Assert.True(Loc.Exists($"Bench.Phase.{p}"), p));
    }

    [Fact]
    public void EveryImportFailureHasATextExceptNone()
    {
        foreach (var failure in Enum.GetValues<ImportFailure>().Where(f => f != ImportFailure.None))
        {
            Assert.True(Loc.Exists($"Bench.Import.Failure.{failure}"), failure.ToString());
        }
    }
}

public sealed class GpuBenchJobTests
{
    [Fact]
    public void TheSceneJobOffersResolutionAndDurationAndWatchesTheGpuTemperature()
    {
        var job = new GpuSceneJob(new StubMachine());

        Assert.Equal(["resolution", "duration"], job.Options.Select(o => o.Id));
        Assert.Equal("1920x1080", job.Options[0].Value);
        Assert.Equal("60", job.Options[1].Value);
        Assert.Equal(ThermalComponent.Gpu, job.Thermal);
        Assert.Equal([BenchmarkTests.GpuScene], job.HistoryTests);
        Assert.Equal(BenchPoll.Idle, job.Poll());
    }

    [Fact]
    public void TheSceneJobIsUnavailableOnlyWhenThereIsNoHardwareGpu()
    {
        var job = new GpuSceneJob(new StubMachine());

        var hasGpu = M1vClock.Benchmarks.Gpu.GpuSupport.HasHardwareAdapter();

        Assert.Equal(hasGpu, job.UnavailableReason is null);
    }

    [Fact]
    public async Task TheRayTracingEntryChecksTheCardInTheBackgroundAndAnnouncesTheAnswer()
    {
        var job = new RayTracingJob(new StubMachine(), NullLogger<RayTracingJob>.Instance);
        Assert.Equal(Loc.Get("Bench.Rt.Checking"), job.UnavailableReason);
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        job.AvailabilityChanged += (_, _) => answered.TrySetResult();

        job.Refresh();
        await answered.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.NotNull(job.UnavailableReason);
        Assert.NotEqual(Loc.Get("Bench.Rt.Checking"), job.UnavailableReason);
        Assert.Empty(job.Options);
        Assert.Empty(job.HistoryTests);
    }

    [Fact]
    public async Task TheRayTracingTestNeverRunsInThisVersion()
    {
        var job = new RayTracingJob(new StubMachine(), NullLogger<RayTracingJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(TestContext.Current.CancellationToken));
    }
}
