using System.IO;
using M1vClock.App.Dialogs;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Performance;
using M1vClock.App.Services;
using M1vClock.Benchmarks.Gpu;
using M1vClock.Benchmarks.Safety;
using M1vClock.Benchmarks.Stress;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.App.Tests;

internal sealed class FakeStressWorkload : IStressWorkload
{
    private readonly Lock _gate = new();
    private WorkloadStatus _status = WorkloadStatus.Idle;

    public int Starts { get; private set; }

    public int Halts { get; private set; }

    public int Disposals { get; private set; }

    public WorkloadStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
        set
        {
            lock (_gate)
            {
                _status = value;
            }
        }
    }

    public void Start()
    {
        Starts++;
        Status = Status with { Alive = true };
    }

    public void Halt()
    {
        Halts++;
        Status = Status with { Alive = false };
    }

    public void Dispose()
    {
        Disposals++;
        Halt();
    }
}

internal sealed class StubStressFactory : IStressWorkloadFactory
{
    public bool GpuAvailable { get; set; }

    public List<FakeStressWorkload> Created { get; } = [];

    public (bool Cpu, bool Ram, double Fraction)? LastRequest { get; private set; }

    public IStressWorkload CreateCpuRam(bool cpu, bool ram, double ramFraction)
    {
        LastRequest = (cpu, ram, ramFraction);
        var workload = new FakeStressWorkload();
        Created.Add(workload);
        return workload;
    }

    public GpuBurnSettings? LastGpuSettings { get; private set; }

    public FakeStressWorkload? GpuWorkload { get; private set; }

    public IStressWorkload? CreateGpu(GpuBurnSettings settings)
    {
        LastGpuSettings = settings;
        GpuWorkload = new FakeStressWorkload();
        return GpuWorkload;
    }
}

public sealed class StressPageViewModelTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("m1vclock-stress-page-tests");
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeSensorService _sensors = new() { State = SensorServiceState.Running };
    private readonly StubProbe _probe = new();
    private readonly StubStressFactory _factory = new();
    private readonly DialogService _dialogs = new();
    private readonly HeavyTestGate _gate;
    private readonly SafetyMonitor _monitor;
    private readonly StressSession _session;
    private readonly AppSettingsService _settings;
    private readonly StressPageViewModel _page;
    private readonly List<DialogRequest> _shown = [];
    private bool _answer = true;

    public StressPageViewModelTests()
    {
        _gate = new HeavyTestGate(_sensors, _clock, NullLogger<HeavyTestGate>.Instance);
        _monitor = new SafetyMonitor(_probe, _clock, NullLogger<SafetyMonitor>.Instance);
        _session = new StressSession(_monitor, _clock, NullLogger<StressSession>.Instance);
        var store = new SettingsStore<AppSettings>(Path.Combine(_temp.FullName, "settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);
        _settings = new AppSettingsService(store, NullLogger<AppSettingsService>.Instance);
        _dialogs.Opened += (_, _) =>
        {
            _shown.Add(_dialogs.Current!);
            if (_answer)
            {
                _dialogs.ConfirmCommand.Execute(null);
            }
            else
            {
                _dialogs.CancelCommand.Execute(null);
            }
        };
        _page = new StressPageViewModel(_session, _gate, _factory, new StubMachine(), _dialogs, _settings, new InlineUiThread(), _clock, NullLogger<StressPageViewModel>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        _page.Dispose();
        await _session.DisposeAsync();
        await _monitor.DisposeAsync();
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

    private async Task<Task> StartAndWaitRunningAsync()
    {
        var run = _page.ToggleAsync();
        await WaitUntilAsync(() => _page.IsRunning);
        return run;
    }

    private async Task AdvanceUntilAsync(Func<bool> condition, TimeSpan? step = null)
    {
        var tick = step ?? TimeSpan.FromMilliseconds(250);
        await WaitUntilAsync(() =>
        {
            _clock.Advance(tick);
            return condition();
        });
    }

    [Fact]
    public void ThePageStartsOnTheSafeDefaults()
    {
        Assert.True(_page.CpuEnabled);
        Assert.False(_page.RamEnabled);
        Assert.False(_page.GpuEnabled);
        Assert.False(_page.GpuAvailable);
        Assert.Equal(2, _page.DurationIndex);
        Assert.Equal(SafetyLimits.CpuDefault, _page.CpuLimit);
        Assert.Equal(SafetyLimits.GpuDefault, _page.GpuLimit);
        Assert.True(_page.CanToggle);
        Assert.Equal(Loc.Get("Stress.Start"), _page.ButtonText);
        Assert.Equal(Loc.Get("Stress.Status.Idle"), _page.StatusText);
    }

    [Fact]
    public void TheLiveCardsFollowTheComponentsThatWereChosen()
    {
        Assert.Equal([_page.TemperatureCard, _page.LoadCard, _page.PowerCard, _page.ElapsedCard], _page.Cards);

        _page.RamEnabled = true;
        Assert.Equal([_page.TemperatureCard, _page.LoadCard, _page.PowerCard, _page.MemoryCard, _page.ElapsedCard], _page.Cards);

        _page.CpuEnabled = false;
        Assert.Equal([_page.MemoryCard, _page.ElapsedCard], _page.Cards);
    }

    [Fact]
    public void WithNothingSelectedTheTestCannotStart()
    {
        _page.CpuEnabled = false;

        Assert.False(_page.HasSelection);
        Assert.False(_page.CanToggle);
    }

    [Fact]
    public async Task StartingWithNothingSelectedExplainsInsteadOfRunning()
    {
        _page.CpuEnabled = false;

        await _page.ToggleAsync();

        Assert.Equal(Loc.Get("Stress.Error.NothingSelected"), _page.Message);
        Assert.Empty(_shown);
        Assert.Empty(_factory.Created);
    }

    [Fact]
    public async Task EveryStartAsksForConfirmationAndListsWhatWillHappen()
    {
        _answer = false;

        await _page.ToggleAsync();

        var request = Assert.Single(_shown);
        Assert.Equal(Loc.Get("Stress.Confirm.Title"), request.Title);
        Assert.Contains(request.Changes, c => c.Contains("85", StringComparison.Ordinal));
        Assert.Contains(request.Changes, c => c.Contains("15", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(request.Undo));
        Assert.Empty(_factory.Created);
        Assert.False(_gate.IsBusy);
        Assert.False(_page.IsRunning);
    }

    [Fact]
    public async Task ConfirmingStartsTheWorkloadPausesTheSensorsAndLogsTheStart()
    {
        var run = await StartAndWaitRunningAsync();

        Assert.Equal(Loc.Get("Stress.Status.Running"), _page.StatusText);
        Assert.Equal(MetricState.Good, _page.StatusTone);
        Assert.Equal(Loc.Get("Stress.Stop"), _page.ButtonText);
        Assert.Equal(1, _sensors.Pauses);
        Assert.Single(_factory.Created);
        Assert.Equal(1, _factory.Created[0].Starts);
        Assert.Equal((true, false, 0.6), _factory.LastRequest);
        Assert.Single(_page.Log);
        Assert.True(_page.HasLog);

        _page.ToggleCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task TheLiveCardsAndTheChartFollowTheSafetySamples()
    {
        var run = await StartAndWaitRunningAsync();

        await AdvanceUntilAsync(() => _page.TemperatureCard.Value != "—");
        await AdvanceUntilAsync(() => _page.CpuSeries.Points.Count > 0);

        Assert.Equal("55", _page.TemperatureCard.Value.Split(',')[0]);
        Assert.Equal("90", _page.LoadCard.Value.Split(',')[0]);
        Assert.Equal("100", _page.PowerCard.Value.Split(',')[0]);
        Assert.NotEqual("—", _page.ElapsedCard.Value);
        Assert.Equal(55, _page.CpuSeries.Latest);

        _page.ToggleCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task TheMemoryCardShowsPassesAndErrorsWhenTheRamTestIsOn()
    {
        _page.RamEnabled = true;
        var run = await StartAndWaitRunningAsync();
        _factory.Created[0].Status = _factory.Created[0].Status with { MemoryPasses = 7, MemoryBytes = 384L << 20 };

        await AdvanceUntilAsync(() => _page.MemoryCard.Value == "7");

        Assert.Equal(MetricState.Good, _page.MemoryCard.State);
        _page.ToggleCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task WhenTheTimeIsUpTheTestEndsAsCompletedWithASummary()
    {
        _page.DurationIndex = 1;
        var run = await StartAndWaitRunningAsync();

        await AdvanceUntilAsync(() => !_page.IsRunning, TimeSpan.FromSeconds(5));
        await run;

        Assert.Equal(Loc.Get("Stress.Status.Completed"), _page.StatusText);
        Assert.Equal(MetricState.Good, _page.StatusTone);
        Assert.True(_page.HasSummary);
        Assert.Contains(_page.Summary!.Rows, r => r.Label == Loc.Get("Stress.Summary.CpuMax"));
        Assert.Equal(2, _page.Log.Count);
        Assert.False(_gate.IsBusy);
        Assert.Equal(1, _sensors.PauseReleases);
        Assert.Equal(1, _factory.Created[0].Disposals);
    }

    [Fact]
    public async Task StoppingEndsItAsStoppedByTheUser()
    {
        var run = await StartAndWaitRunningAsync();

        _page.ToggleCommand.Execute(null);
        await run;

        Assert.Equal(Loc.Get("Stress.Status.Stopped"), _page.StatusText);
        Assert.Equal(MetricState.Neutral, _page.StatusTone);
        Assert.False(_page.IsRunning);
    }

    [Fact]
    public async Task TheEscapeKeyStopsItToo()
    {
        _page.OnNavigatedTo();
        var run = await StartAndWaitRunningAsync();

        _gate.RequestStop();
        await run;

        Assert.Equal(Loc.Get("Stress.Status.Stopped"), _page.StatusText);
    }

    [Fact]
    public async Task LeavingThePageStopsTheTest()
    {
        var run = await StartAndWaitRunningAsync();

        _page.OnNavigatedFrom();
        await run;

        Assert.False(_page.IsRunning);
        Assert.False(_gate.IsBusy);
    }

    [Fact]
    public async Task ReachingTheLimitAbortsAndSaysWhichComponentAndTemperature()
    {
        _probe.CpuTemperature = 91;

        await _page.ToggleAsync();

        Assert.Equal(MetricState.Critical, _page.StatusTone);
        Assert.Contains("91", _page.StatusText, StringComparison.Ordinal);
        Assert.Contains("85", _page.StatusText, StringComparison.Ordinal);
        Assert.Contains(Loc.Get("Bench.Component.Cpu"), _page.StatusText, StringComparison.Ordinal);
        Assert.False(_page.IsRunning);
        Assert.True(_page.HasSummary);
        Assert.True(_factory.Created[0].Halts > 0);
    }

    [Fact]
    public async Task MemoryErrorsAbortTheTest()
    {
        _page.RamEnabled = true;
        var run = await StartAndWaitRunningAsync();

        _factory.Created[0].Status = _factory.Created[0].Status with { MemoryErrors = 5 };
        await AdvanceUntilAsync(() => !_page.IsRunning);
        await run;

        Assert.Equal(Loc.Format("Stress.Status.MemoryErrors", "5"), _page.StatusText);
        Assert.Equal(MetricState.Critical, _page.StatusTone);
    }

    [Fact]
    public async Task WithoutATemperatureSensorTheCpuTestIsRefusedWithTheWayToFixIt()
    {
        _probe.CpuTemperature = null;

        await _page.ToggleAsync();

        Assert.False(_page.IsRunning);
        Assert.True(_page.MessageIsCritical);
        Assert.Contains("PawnIO", _page.Message, StringComparison.Ordinal);
        Assert.Contains(Loc.Get("Hw.Sensor.Problem.PawnIoCommand"), _page.Message, StringComparison.Ordinal);
        Assert.Equal(0, _factory.Created[0].Starts);
        Assert.Equal(1, _factory.Created[0].Disposals);
        Assert.False(_gate.IsBusy);
        Assert.Equal(Loc.Get("Stress.Status.Idle"), _page.StatusText);
    }

    [Fact]
    public async Task ARamOnlyTestRunsWithoutATemperatureSensor()
    {
        _probe.CpuTemperature = null;
        _page.CpuEnabled = false;
        _page.RamEnabled = true;

        var run = await StartAndWaitRunningAsync();

        Assert.True(_page.IsRunning);
        _page.ToggleCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task AGpuTestIsIgnoredWhenThereIsNoHardwareGpu()
    {
        _page.GpuEnabled = true;

        var run = await StartAndWaitRunningAsync();

        Assert.Equal((true, false, 0.6), _factory.LastRequest);
        Assert.Single(_factory.Created);
        Assert.Null(_factory.GpuWorkload);
        _page.ToggleCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task TheGpuBurnIsCreatedWithTheOptionsThatWereChosenAndTheNameOfTheGpu()
    {
        _factory.GpuAvailable = true;
        _page.CpuEnabled = false;
        _page.GpuEnabled = true;
        _page.GpuResolutionIndex = 2;
        _page.GpuMsaaIndex = 2;
        _page.GpuIntensityIndex = 2;

        var run = await StartAndWaitRunningAsync();

        var settings = _factory.LastGpuSettings!;
        Assert.Equal((2560, 1440, false, 8, 160, 64), (settings.Width, settings.Height, settings.Fullscreen, settings.Msaa, settings.Shells, settings.Iterations));
        Assert.Equal(StubMachine.Profile.Gpu, settings.AdapterName);
        Assert.Equal(1, _factory.GpuWorkload!.Starts);
        _page.ToggleCommand.Execute(null);
        await run;
        Assert.Equal(1, _factory.GpuWorkload.Disposals);
    }

    [Theory]
    [InlineData(0, 1280, 720, false)]
    [InlineData(1, 1920, 1080, false)]
    [InlineData(3, 1920, 1080, true)]
    public async Task TheResolutionChoiceMapsToASizeAndTheFullscreenFlag(int index, int width, int height, bool fullscreen)
    {
        _factory.GpuAvailable = true;
        _page.CpuEnabled = false;
        _page.GpuEnabled = true;
        _page.GpuResolutionIndex = index;

        var run = await StartAndWaitRunningAsync();

        Assert.Equal((width, height, fullscreen), (_factory.LastGpuSettings!.Width, _factory.LastGpuSettings.Height, _factory.LastGpuSettings.Fullscreen));
        _page.ToggleCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task AGpuTestWithoutAGpuTemperatureIsRefusedAndNothingStarts()
    {
        _factory.GpuAvailable = true;
        _probe.GpuTemperature = null;
        _page.CpuEnabled = false;
        _page.GpuEnabled = true;

        await _page.ToggleAsync();

        Assert.False(_page.IsRunning);
        Assert.Contains(Loc.Get("Bench.Component.Gpu"), _page.Message, StringComparison.Ordinal);
        Assert.Equal(0, _factory.GpuWorkload!.Starts);
        Assert.Equal(1, _factory.GpuWorkload.Disposals);
        Assert.False(_gate.IsBusy);
    }

    [Fact]
    public async Task ClosingTheGpuWindowEndsTheTestAsStoppedByTheUser()
    {
        _factory.GpuAvailable = true;
        _page.CpuEnabled = false;
        _page.GpuEnabled = true;
        var run = await StartAndWaitRunningAsync();

        _factory.GpuWorkload!.Status = _factory.GpuWorkload.Status with { Alive = false };
        await AdvanceUntilAsync(() => !_page.IsRunning);
        await run;

        Assert.Equal(Loc.Get("Stress.Status.Stopped"), _page.StatusText);
    }

    [Fact]
    public async Task AGpuFailureShowsItsReasonAndAbortsTheTest()
    {
        _factory.GpuAvailable = true;
        _page.CpuEnabled = false;
        _page.GpuEnabled = true;
        var run = await StartAndWaitRunningAsync();

        _factory.GpuWorkload!.Status = _factory.GpuWorkload.Status with { Error = "dispositivo perdido" };
        await AdvanceUntilAsync(() => !_page.IsRunning);
        await run;

        Assert.Equal(Loc.Format("Stress.Status.Failed", "dispositivo perdido"), _page.StatusText);
        Assert.Equal(MetricState.Critical, _page.StatusTone);
    }

    [Fact]
    public async Task TheGpuCardsShowTheTemperatureAndTheFramesPerSecond()
    {
        _factory.GpuAvailable = true;
        _page.CpuEnabled = false;
        _page.GpuEnabled = true;
        Assert.Contains(_page.GpuTemperatureCard, _page.Cards);
        Assert.Contains(_page.GpuFpsCard, _page.Cards);
        var run = await StartAndWaitRunningAsync();
        _factory.GpuWorkload!.Status = _factory.GpuWorkload.Status with { Fps = 144, AverageFps = 140, Frames = 5000 };

        await AdvanceUntilAsync(() => _page.GpuFpsCard.Value == "144");

        Assert.Equal("50", _page.GpuTemperatureCard.Value.Split(',')[0]);
        _page.ToggleCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task AnotherHeavyTestRunningBlocksTheStart()
    {
        using var other = await _gate.EnterAsync("benchmark:cpu", TestContext.Current.CancellationToken);

        Assert.True(_page.OtherTestRunning);
        Assert.False(_page.CanToggle);

        await _page.ToggleAsync();

        Assert.False(_page.IsRunning);
        Assert.Contains("benchmark:cpu", _page.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCpuLimitMovesInStepsOfFiveInsideTheLockedRange()
    {
        _page.LowerCpuLimitCommand.Execute(null);
        Assert.Equal(80, _page.CpuLimit);

        for (var i = 0; i < 10; i++)
        {
            _page.LowerCpuLimitCommand.Execute(null);
        }

        Assert.Equal(SafetyLimits.CpuMinimum, _page.CpuLimit);
        Assert.False(_page.CanLowerCpuLimit);
        Assert.True(_page.CanRaiseCpuLimit);
    }

    [Fact]
    public async Task RaisingUpToNinetyNeedsNoExtraConfirmation()
    {
        await _page.RaiseCpuLimitAsync();

        Assert.Equal(90, _page.CpuLimit);
        Assert.Empty(_shown);
    }

    [Fact]
    public async Task GoingAboveNinetyAsksTwiceAndOnlyChangesAfterBothAnswers()
    {
        _page.CpuLimit = 90;

        await _page.RaiseCpuLimitAsync();

        Assert.Equal(95, _page.CpuLimit);
        Assert.Equal(2, _shown.Count);
        Assert.Equal(DialogTone.Destructive, _shown[0].Tone);
        Assert.Equal(DialogTone.Destructive, _shown[1].Tone);
        Assert.False(_page.CanRaiseCpuLimit);
    }

    [Fact]
    public async Task RefusingEitherQuestionKeepsTheOldLimit()
    {
        _page.CpuLimit = 90;
        _answer = false;

        await _page.RaiseCpuLimitAsync();

        Assert.Equal(90, _page.CpuLimit);
        Assert.Single(_shown);
    }

    [Fact]
    public async Task TheLimitNeverGoesPastTheHardMaximum()
    {
        _page.CpuLimit = 95;

        await _page.RaiseCpuLimitAsync();

        Assert.Equal(95, _page.CpuLimit);
        Assert.Empty(_shown);
    }

    [Fact]
    public async Task TheGpuLimitFollowsTheSameRules()
    {
        _page.GpuLimit = 90;

        await _page.RaiseGpuLimitAsync();
        Assert.Equal(95, _page.GpuLimit);
        _page.LowerGpuLimitCommand.Execute(null);

        Assert.Equal(90, _page.GpuLimit);
        Assert.Equal(2, _shown.Count);
    }

    [Fact]
    public void TheLimitTextFollowsTheTemperatureUnitOfTheSettings()
    {
        Assert.Contains("85", _page.CpuLimitText, StringComparison.Ordinal);
        Assert.Contains("°C", _page.CpuLimitText, StringComparison.Ordinal);

        _settings.Update(s => s.TemperatureUnit = TemperatureUnit.Fahrenheit);
        _page.CpuLimit = 90;

        Assert.Contains("194", _page.CpuLimitText, StringComparison.Ordinal);
        Assert.Equal("°F", _page.ChartUnit);
    }

    [Fact]
    public async Task TheRamChoiceChangesTheFractionAskedFromTheFactory()
    {
        _page.CpuEnabled = false;
        _page.RamEnabled = true;
        _page.RamFractionIndex = 2;

        var run = await StartAndWaitRunningAsync();

        Assert.Equal((false, true, 0.8), _factory.LastRequest);
        _page.ToggleCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task TheDurationChoiceReachesTheConfirmationText()
    {
        _page.DurationIndex = 0;
        _answer = false;

        await _page.ToggleAsync();

        Assert.Contains(_shown[0].Changes, c => c == Loc.Get("Stress.Confirm.Unlimited"));
    }

    [Fact]
    public async Task TheLogKeepsOnlyTheLatestLinesAndCanBeCleared()
    {
        var run = await StartAndWaitRunningAsync();
        _page.ToggleCommand.Execute(null);
        await run;
        Assert.Equal(2, _page.Log.Count);

        _page.ClearLogCommand.Execute(null);

        Assert.Empty(_page.Log);
        Assert.False(_page.HasLog);
    }

    [Fact]
    public async Task ARunStartsFromAFreshChartAndSummary()
    {
        var first = await StartAndWaitRunningAsync();
        await AdvanceUntilAsync(() => _page.CpuSeries.Points.Count > 0);
        _page.ToggleCommand.Execute(null);
        await first;
        Assert.True(_page.HasSummary);

        var second = await StartAndWaitRunningAsync();

        Assert.False(_page.HasSummary);
        Assert.Empty(_page.CpuSeries.Points);
        _page.ToggleCommand.Execute(null);
        await second;
    }
}
