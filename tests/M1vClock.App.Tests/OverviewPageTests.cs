using System.IO;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Hosting;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Health;
using M1vClock.Hardware.Sensors;
using M1vClock.Hardware.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.App.Tests;

internal static class SensorReadings
{
    public static SensorReading Make(string id, SensorHardware hardware, string name, SensorKind kind, float? value) =>
        new(id, "Hardware", hardware, name, kind, value, value, value);
}

internal sealed class StubHealth : IHealthService
{
    public HealthReport Report { get; set; } = new([], HealthStatus.Unknown);

    public Exception? Failure { get; set; }

    public int Reads { get; private set; }

    public SensorSnapshot? LastSnapshot { get; private set; }

    public bool? LastElevated { get; private set; }

    public Task<HealthReport> ReadAsync(SensorSnapshot? snapshot, bool elevated, CancellationToken cancellationToken)
    {
        Reads++;
        LastSnapshot = snapshot;
        LastElevated = elevated;
        return Failure is null ? Task.FromResult(Report) : Task.FromException<HealthReport>(Failure);
    }
}

public sealed class OverviewRowsTests
{
    private static Volume Drive(ulong total, ulong free) => new("C:", "Sistema", VolumeKind.Internal, "NTFS", "NX-512", total, free);

    [Theory]
    [InlineData(null, MetricState.Neutral)]
    [InlineData(40f, MetricState.Good)]
    [InlineData(74.9f, MetricState.Good)]
    [InlineData(75f, MetricState.Warning)]
    [InlineData(89.9f, MetricState.Warning)]
    [InlineData(90f, MetricState.Critical)]
    public void TheCpuIsWarmFromSeventyFiveDegrees(float? celsius, MetricState expected)
    {
        Assert.Equal(expected, OverviewRows.CpuTemperatureState(celsius));
    }

    [Theory]
    [InlineData(79.9f, MetricState.Good)]
    [InlineData(80f, MetricState.Warning)]
    [InlineData(90f, MetricState.Critical)]
    public void TheGpuIsWarmFromEightyDegrees(float celsius, MetricState expected)
    {
        Assert.Equal(expected, OverviewRows.GpuTemperatureState(celsius));
    }

    [Theory]
    [InlineData(null, MetricState.Neutral)]
    [InlineData(84.9f, MetricState.Good)]
    [InlineData(85f, MetricState.Warning)]
    [InlineData(95f, MetricState.Critical)]
    public void MemoryFollowsTheHealthThresholds(float? used, MetricState expected)
    {
        Assert.Equal(expected, OverviewRows.MemoryState(used));
    }

    [Theory]
    [InlineData(null, MetricState.Neutral)]
    [InlineData(10.0, MetricState.Good)]
    [InlineData(9.9, MetricState.Warning)]
    [InlineData(4.9, MetricState.Critical)]
    public void TheDiskFollowsTheHealthThresholds(double? free, MetricState expected)
    {
        Assert.Equal(expected, OverviewRows.DiskState(free));
    }

    [Fact]
    public void ATemperatureCardShowsTheTemperatureAndTheOtherReadingsAsDetail()
    {
        var card = new OverviewCardViewModel("Processador");

        OverviewRows.Processor(card, 58f, 12.4f, 41.55f, 4450f, TemperatureUnit.Celsius, OverviewRows.CpuTemperatureState);

        Assert.Equal("58", card.Value);
        Assert.Equal("°C", card.Unit);
        Assert.Equal("12,4 % · 41,6 W · 4.450 MHz", card.Detail);
        Assert.Equal(MetricState.Good, card.State);
        Assert.Equal("Normal", card.StateText);
        Assert.False(card.IsLoading);
    }

    [Fact]
    public void FahrenheitConvertsOnlyTheMainValue()
    {
        var card = new OverviewCardViewModel("Processador");

        OverviewRows.Processor(card, 55f, 10f, null, null, TemperatureUnit.Fahrenheit, OverviewRows.CpuTemperatureState);

        Assert.Equal("131", card.Value);
        Assert.Equal("°F", card.Unit);
        Assert.Equal("10 %", card.Detail);
    }

    [Fact]
    public void WithoutATemperatureTheLoadBecomesTheMainValueAndZeroPowerAndClockAreHidden()
    {
        var card = new OverviewCardViewModel("Processador");

        OverviewRows.Processor(card, null, 16.3f, 0f, 0f, TemperatureUnit.Celsius, OverviewRows.CpuTemperatureState);

        Assert.Equal("16", card.Value);
        Assert.Equal("%", card.Unit);
        Assert.Equal(string.Empty, card.Detail);
        Assert.Equal(MetricState.Neutral, card.State);
        Assert.Equal(string.Empty, card.StateText);
    }

    [Fact]
    public void ACardWithNoReadingsShowsADash()
    {
        var card = new OverviewCardViewModel("Placa de vídeo");

        OverviewRows.Processor(card, null, null, null, null, TemperatureUnit.Celsius, OverviewRows.GpuTemperatureState);

        Assert.Equal("—", card.Value);
        Assert.False(card.IsLoading);
    }

    [Fact]
    public void TheMemoryCardShowsUsageAndAmountsFromThePhysicalMemory()
    {
        var snapshot = new SensorSnapshot(DateTimeOffset.UnixEpoch,
        [
            SensorReadings.Make("/ram/load/0", SensorHardware.Memory, "Memory", SensorKind.Load, 85.8f),
            SensorReadings.Make("/ram/data/0", SensorHardware.Memory, "Memory Used", SensorKind.Data, 13.63f),
            SensorReadings.Make("/ram/data/1", SensorHardware.Memory, "Memory Available", SensorKind.Data, 2.26f),
        ]);
        var card = new OverviewCardViewModel("Memória");

        OverviewRows.Memory(card, snapshot);

        Assert.Equal("86", card.Value);
        Assert.Equal("%", card.Unit);
        Assert.Equal("13,6 de 16 GB", card.Detail);
        Assert.Equal(MetricState.Warning, card.State);
        Assert.Equal("Alta", card.StateText);
    }

    [Fact]
    public void WithoutMemoryReadingsTheCardIsEmptyButNotLoading()
    {
        var card = new OverviewCardViewModel("Memória");

        OverviewRows.Memory(card, new SensorSnapshot(DateTimeOffset.UnixEpoch, []));

        Assert.Equal("—", card.Value);
        Assert.False(card.IsLoading);
    }

    [Fact]
    public void TheDiskCardShowsTheUsedPercentAndTheFreeSpace()
    {
        var card = new OverviewCardViewModel("Disco do sistema");

        OverviewRows.Disk(card, Drive(508_800_000_000, 59_000_000_000));

        Assert.Equal("88", card.Value);
        Assert.Equal("59 GB livres de 508,8 GB", card.Detail);
        Assert.Equal(MetricState.Good, card.State);
    }

    [Fact]
    public void AnAlmostFullDiskIsCriticalAndSaysFull()
    {
        var card = new OverviewCardViewModel("Disco do sistema");

        OverviewRows.Disk(card, Drive(1_000_000_000_000, 30_000_000_000));

        Assert.Equal(MetricState.Critical, card.State);
        Assert.Equal("Quase cheia", card.StateText);
    }

    [Fact]
    public void WithoutTheSystemVolumeTheDiskCardShowsADash()
    {
        var card = new OverviewCardViewModel("Disco do sistema");

        OverviewRows.Disk(card, null);

        Assert.Equal("—", card.Value);
        Assert.False(card.IsLoading);
    }

    [Fact]
    public void HealthRowsPutTheWorstFirstAndSpeakInPlainWords()
    {
        var report = new HealthReport(
        [
            new HealthCheck(HealthKind.Memory, HealthStatus.Ok, 40),
            new HealthCheck(HealthKind.Usb, HealthStatus.Attention, 2),
            new HealthCheck(HealthKind.Disks, HealthStatus.Critical, 1),
            new HealthCheck(HealthKind.Battery, HealthStatus.Unknown),
        ], HealthStatus.Critical);

        var rows = OverviewRows.Health(report);

        Assert.Equal(["Discos", "Dispositivos USB", "Bateria", "Memória"], rows.Select(r => r.Title));
        Assert.Equal("1 disco(s) com risco de falha: faça backup.", rows[0].Detail);
        Assert.Equal("2 dispositivo(s) USB com erro no Windows.", rows[1].Detail);
        Assert.Equal("O Windows não informou o desgaste da bateria.", rows[2].Detail);
        Assert.Equal("40% em uso.", rows[3].Detail);
        Assert.Equal(["Icon.Error", "Icon.Warning", "Icon.Info", "Icon.Success"], rows.Select(r => r.IconKey));
        Assert.Equal([MetricState.Critical, MetricState.Warning, MetricState.Neutral, MetricState.Good], rows.Select(r => r.Tone));
    }

    [Theory]
    [InlineData(CpuSensorProblem.None, "Temperatura e consumo disponíveis.")]
    [InlineData(CpuSensorProblem.NotElevated, "Falta rodar como administrador para ler a temperatura da CPU.")]
    [InlineData(CpuSensorProblem.PawnIoMissing, "Falta o driver PawnIO para ler a temperatura da CPU.")]
    [InlineData(CpuSensorProblem.ReadFailed, "O PawnIO está instalado, mas a leitura da CPU falhou.")]
    public void TheCpuSensorCheckExplainsEachProblem(CpuSensorProblem problem, string expected)
    {
        var status = problem == CpuSensorProblem.None ? HealthStatus.Ok : HealthStatus.Attention;

        Assert.Equal(expected, OverviewRows.HealthDetail(new HealthCheck(HealthKind.CpuSensors, status, (int)problem)));
    }

    [Fact]
    public void EveryCheckTheEvaluatorCanProduceHasAText()
    {
        (HealthKind Kind, HealthStatus[] Statuses)[] produced =
        [
            (HealthKind.Disks, [HealthStatus.Ok, HealthStatus.Attention, HealthStatus.Critical, HealthStatus.Unknown]),
            (HealthKind.Memory, [HealthStatus.Ok, HealthStatus.Attention, HealthStatus.Critical]),
            (HealthKind.SystemDrive, [HealthStatus.Ok, HealthStatus.Attention, HealthStatus.Critical]),
            (HealthKind.PowerPlan, [HealthStatus.Ok, HealthStatus.Attention]),
            (HealthKind.Battery, [HealthStatus.Ok, HealthStatus.Attention, HealthStatus.Critical, HealthStatus.Unknown]),
            (HealthKind.Usb, [HealthStatus.Ok, HealthStatus.Attention]),
        ];

        foreach (var (kind, statuses) in produced)
        {
            Assert.False(OverviewRows.Health(new HealthReport([new HealthCheck(kind, statuses[0])], statuses[0]))[0].Title.StartsWith('['), $"falta o título de {kind}");
            foreach (var status in statuses)
            {
                var detail = OverviewRows.HealthDetail(new HealthCheck(kind, status, 3));
                Assert.False(detail.StartsWith('[') || detail.Contains("{0}", StringComparison.Ordinal), $"falta o texto de {kind}/{status}");
            }
        }
    }

    [Theory]
    [InlineData(HealthStatus.Ok, "Tudo certo")]
    [InlineData(HealthStatus.Unknown, "Sem dados suficientes")]
    public void TheOverallTextForOkAndUnknown(HealthStatus status, string expected)
    {
        Assert.Equal(expected, OverviewRows.HealthOverall(new HealthReport([], status)));
    }

    [Fact]
    public void TheOverallTextCountsTheProblems()
    {
        var attention = new HealthReport([new HealthCheck(HealthKind.Usb, HealthStatus.Attention, 1), new HealthCheck(HealthKind.Memory, HealthStatus.Attention, 90)], HealthStatus.Attention);
        var critical = new HealthReport([new HealthCheck(HealthKind.Disks, HealthStatus.Critical, 1)], HealthStatus.Critical);

        Assert.Equal("2 ponto(s) de atenção", OverviewRows.HealthOverall(attention));
        Assert.Equal("Atenção urgente: 1 problema(s)", OverviewRows.HealthOverall(critical));
    }

    [Fact]
    public void TheTemperatureUnitIsSplitFromTheNumber()
    {
        Assert.Equal(("58,5", "°C"), OverviewRows.Temperature(58.5f, TemperatureUnit.Celsius));
        Assert.Equal(("137,3", "°F"), OverviewRows.Temperature(58.5f, TemperatureUnit.Fahrenheit));
        Assert.Equal("Normal", OverviewRows.TemperatureStateText(MetricState.Good));
        Assert.Equal("Quente", OverviewRows.TemperatureStateText(MetricState.Warning));
        Assert.Equal("Alta", OverviewRows.TemperatureStateText(MetricState.Critical));
        Assert.Equal(string.Empty, OverviewRows.TemperatureStateText(MetricState.Neutral));
    }
}

public sealed class OverviewHistoryTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("m1vclock-history-tests");
    private readonly FakeSensorService _service = new();
    private readonly AppSettingsService _settings;

    public OverviewHistoryTests()
    {
        var store = new SettingsStore<AppSettings>(Path.Combine(_temp.FullName, "settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);
        _settings = new AppSettingsService(store, NullLogger<AppSettingsService>.Instance);
    }

    public void Dispose() => _temp.Delete(recursive: true);

    private OverviewHistory Create() => new(_service, _settings, new InlineUiThread());

    private static SensorSnapshot Snapshot(float? cpuTemperature = 60f, float? gpuTemperature = 50f) => new(
        DateTimeOffset.UnixEpoch,
        [
            SensorReadings.Make("cpu/t", SensorHardware.Cpu, "CPU Package", SensorKind.Temperature, cpuTemperature),
            SensorReadings.Make("cpu/l", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 20),
            SensorReadings.Make("cpu/p", SensorHardware.Cpu, "CPU Package", SensorKind.Power, 45),
            SensorReadings.Make("gpu/t", SensorHardware.Gpu, "GPU Core", SensorKind.Temperature, gpuTemperature),
            SensorReadings.Make("gpu/l", SensorHardware.Gpu, "GPU Core", SensorKind.Load, 70),
            SensorReadings.Make("gpu/p", SensorHardware.Gpu, "GPU Package", SensorKind.Power, 110),
        ]);

    [Fact]
    public void ASnapshotFeedsEverySeries()
    {
        var history = Create();

        history.Add(Snapshot());

        Assert.Equal(60, history.CpuTemperature.Latest);
        Assert.Equal(50, history.GpuTemperature.Latest);
        Assert.Equal(20, history.CpuLoad.Latest);
        Assert.Equal(70, history.GpuLoad.Latest);
        Assert.Equal(45, history.CpuPower.Latest);
        Assert.Equal(110, history.GpuPower.Latest);
    }

    [Fact]
    public void MissingReadingsLeaveGapsInsteadOfZeros()
    {
        var history = Create();

        history.Add(Snapshot(cpuTemperature: null));

        Assert.Null(history.CpuTemperature.Latest);
        Assert.Empty(history.CpuTemperature.Points);
        Assert.Equal(50, history.GpuTemperature.Latest);
    }

    [Fact]
    public void FahrenheitConvertsTemperaturesOnly()
    {
        _settings.Update(s => s.TemperatureUnit = TemperatureUnit.Fahrenheit);
        var history = Create();

        history.Add(Snapshot());

        Assert.Equal(140, history.CpuTemperature.Latest);
        Assert.Equal(20, history.CpuLoad.Latest);
    }

    [Fact]
    public async Task ChangingTheUnitClearsTheTemperatureHistoryOnly()
    {
        var history = Create();
        await history.StartAsync(TestContext.Current.CancellationToken);
        history.Add(Snapshot());

        _settings.Update(s => s.TemperatureUnit = TemperatureUnit.Fahrenheit);

        Assert.Empty(history.CpuTemperature.Points);
        Assert.Empty(history.GpuTemperature.Points);
        Assert.NotEmpty(history.CpuLoad.Points);
        Assert.Equal(TemperatureUnit.Fahrenheit, history.Unit);
    }

    [Fact]
    public async Task StartingSubscribesToTheSensorsAndStoppingReleasesThem()
    {
        var history = Create();

        await history.StartAsync(TestContext.Current.CancellationToken);
        _service.Publish(Snapshot());
        Assert.Equal(1, _service.Subscriptions);
        Assert.Equal(60, history.CpuTemperature.Latest);

        await history.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _service.Releases);
        Assert.False(_service.HasSnapshotSubscribers);
    }

    [Fact]
    public async Task WithTheBackgroundHistoryOffNoLeaseIsTakenButSnapshotsFromOthersAreStillRecorded()
    {
        _settings.Update(s => s.BackgroundSensorHistory = false);
        var history = Create();

        await history.StartAsync(TestContext.Current.CancellationToken);
        _service.Publish(Snapshot());

        Assert.Equal(0, _service.Subscriptions);
        Assert.Equal(60, history.CpuTemperature.Latest);
    }

    [Fact]
    public async Task TurningTheBackgroundHistoryOffAndOnReleasesAndTakesTheLeaseAgain()
    {
        var history = Create();
        await history.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, _service.Subscriptions);

        _settings.Update(s => s.BackgroundSensorHistory = false);
        Assert.Equal(1, _service.Releases);

        _settings.Update(s => s.BackgroundSensorHistory = true);
        Assert.Equal(2, _service.Subscriptions);

        await history.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, _service.Releases);
    }

    [Fact]
    public async Task WhenTheSensorsStopBecauseNobodyUsesThemTheHistoryIsDropped()
    {
        var history = Create();
        await history.StartAsync(TestContext.Current.CancellationToken);
        history.Add(Snapshot());

        _service.Change(SensorServiceState.Running);
        Assert.NotEmpty(history.CpuLoad.Points);
        _service.Change(SensorServiceState.Paused);
        Assert.NotEmpty(history.CpuLoad.Points);
        _service.Change(SensorServiceState.Stopped);

        Assert.Empty(history.CpuLoad.Points);
        Assert.Empty(history.GpuPower.Points);
        Assert.Empty(history.CpuTemperature.Points);
    }

    [Fact]
    public void OnlyTheLastFifteenMinutesStayInMemory()
    {
        var series = new TimeSeries("CPU", OverviewHistory.Retention);

        for (var second = 0; second < 5000; second++)
        {
            series.Add(second, 50);
        }

        Assert.InRange(series.Points.Count, 900, 905);
        Assert.Equal(4999, series.Points[^1].Time);
        Assert.True(series.Points[0].Time >= 4999 - 15 * 60 - 2);
    }

    [Fact]
    public void TheSeriesKeepFifteenMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), OverviewHistory.Retention);
    }
}

public sealed class OverviewPageViewModelTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("m1vclock-overview-tests");
    private readonly FakeSensorService _service = new();
    private readonly StubHealth _health = new();
    private readonly ActivityLog _activity = new(new InlineUiThread());
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly AppSettingsService _settings;
    private readonly OverviewHistory _history;

    public OverviewPageViewModelTests()
    {
        var store = new SettingsStore<AppSettings>(Path.Combine(_temp.FullName, "settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);
        _settings = new AppSettingsService(store, NullLogger<AppSettingsService>.Instance);
        _history = new OverviewHistory(_service, _settings, new InlineUiThread());
    }

    public void Dispose() => _temp.Delete(recursive: true);

    private static SensorSnapshot Snapshot() => new(
        DateTimeOffset.UnixEpoch,
        [
            SensorReadings.Make("cpu/t", SensorHardware.Cpu, "CPU Package", SensorKind.Temperature, 58),
            SensorReadings.Make("cpu/l", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 12),
            SensorReadings.Make("gpu/t", SensorHardware.Gpu, "GPU Core", SensorKind.Temperature, 66),
            SensorReadings.Make("/ram/load/0", SensorHardware.Memory, "Memory", SensorKind.Load, 40),
        ]);

    private static HealthReport Report(HealthStatus overall = HealthStatus.Ok, params HealthCheck[] checks) =>
        new(checks, overall) { SystemVolume = new Volume("C:", "Sistema", VolumeKind.Internal, "NTFS", "NX", 500_000_000_000, 200_000_000_000) };

    private OverviewPageViewModel Create(bool elevated = true) => new(
        _service,
        _health,
        _history,
        _activity,
        _settings,
        new AppInfo("0.1.0", _temp.FullName, elevated),
        new InlineUiThread(),
        _clock,
        new CollectingLogger<OverviewPageViewModel>());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [Fact]
    public void TheCardsStartInTheLoadingState()
    {
        var page = Create();

        Assert.True(page.Cpu.IsLoading);
        Assert.True(page.Disk.IsLoading);
        Assert.True(page.IsHealthLoading);
        Assert.Equal("Verificando…", page.HealthSummary);
    }

    [Fact]
    public async Task OpeningShowsTheLatestSnapshotAndTheHealthReport()
    {
        _service.Latest = Snapshot();
        _health.Report = Report(HealthStatus.Attention, new HealthCheck(HealthKind.Usb, HealthStatus.Attention, 1));
        var page = Create();

        page.OnNavigatedTo();
        await WaitUntilAsync(() => !page.IsHealthLoading);

        Assert.Equal("58", page.Cpu.Value);
        Assert.Equal("66", page.Gpu.Value);
        Assert.Equal("40", page.Memory.Value);
        Assert.Equal("60", page.Disk.Value);
        Assert.Equal("1 ponto(s) de atenção", page.HealthSummary);
        Assert.Equal(MetricState.Warning, page.HealthTone);
        Assert.Equal("Dispositivos USB", Assert.Single(page.HealthRows).Title);
        Assert.Equal(_service.Latest, _health.LastSnapshot);
        Assert.True(_health.LastElevated);
    }

    [Fact]
    public void ANewSnapshotUpdatesTheCardsWhileThePageIsOpen()
    {
        var page = Create();
        page.OnNavigatedTo();

        _service.Publish(Snapshot());

        Assert.Equal("58", page.Cpu.Value);
        Assert.Equal("Normal", page.Cpu.StateText);
    }

    [Fact]
    public async Task AFailedHealthReadShowsAShortMessageAndGoesToTheLog()
    {
        _health.Failure = new InvalidOperationException("WMI indisponível.");
        var page = Create();

        page.OnNavigatedTo();
        await WaitUntilAsync(() => page.HasHealthFailure);

        Assert.StartsWith("Não foi possível verificar a saúde do PC", page.HealthFailure, StringComparison.Ordinal);
        Assert.False(page.IsHealthLoading);
    }

    [Fact]
    public async Task TheHealthIsCheckedAgainEverySixtySecondsWhileThePageIsOpen()
    {
        var page = Create();
        page.OnNavigatedTo();
        await WaitUntilAsync(() => _health.Reads >= 1);

        _clock.Advance(OverviewPageViewModel.HealthInterval);
        await WaitUntilAsync(() => _health.Reads >= 2);
        _clock.Advance(OverviewPageViewModel.HealthInterval);
        await WaitUntilAsync(() => _health.Reads >= 3);

        page.OnNavigatedFrom();
        var reads = _health.Reads;
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(reads, _health.Reads);
    }

    [Fact]
    public async Task IfTheFirstCheckRanBeforeTheSensorsOpenedItRunsAgainWithTheFirstSnapshot()
    {
        var page = Create();
        page.OnNavigatedTo();
        await WaitUntilAsync(() => _health.Reads >= 1);
        Assert.Null(_health.LastSnapshot);

        _service.Publish(Snapshot());
        await WaitUntilAsync(() => _health.Reads >= 2);

        Assert.NotNull(_health.LastSnapshot);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _service.Publish(Snapshot());
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(2, _health.Reads);
    }

    [Fact]
    public async Task LeavingThePageReleasesTheSensorsAndStopsListening()
    {
        var page = Create();
        page.OnNavigatedTo();
        await WaitUntilAsync(() => _health.Reads >= 1);

        page.OnNavigatedFrom();

        Assert.Equal(1, _service.Releases);
        Assert.False(_service.HasSnapshotSubscribers);
    }

    [Theory]
    [InlineData(0, "°C", true, 10.0)]
    [InlineData(1, "%", false, 25.0)]
    [InlineData(2, "W", false, 20.0)]
    public void TheMetricSelectorSwitchesTheSeriesTheUnitAndTheScale(int metric, string unit, bool autoFloor, double range)
    {
        var page = Create();

        page.MetricIndex = metric;

        Assert.Equal(unit, page.ChartUnit);
        Assert.Equal(autoFloor, double.IsNaN(page.ChartFloor));
        Assert.Equal(range, page.ChartMinimumRange);
        Assert.Same(metric switch { 1 => _history.CpuLoad, 2 => _history.CpuPower, _ => _history.CpuTemperature }, page.Primary);
        Assert.Same(metric switch { 1 => _history.GpuLoad, 2 => _history.GpuPower, _ => _history.GpuTemperature }, page.Secondary);
    }

    [Theory]
    [InlineData(0, 60.0)]
    [InlineData(1, 300.0)]
    [InlineData(2, 900.0)]
    public void TheWindowSelectorSetsTheSeconds(int index, double seconds)
    {
        var page = Create();

        page.WindowIndex = index;

        Assert.Equal(seconds, page.ChartWindowSeconds);
    }

    [Fact]
    public void ChangingTheSelectorsNotifiesTheChartBindings()
    {
        var page = Create();
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        page.MetricIndex = 1;
        page.WindowIndex = 2;

        Assert.Contains("Primary", changed);
        Assert.Contains("Secondary", changed);
        Assert.Contains("ChartUnit", changed);
        Assert.Contains("ChartWindowSeconds", changed);
    }

    [Fact]
    public async Task TheRecentActivityShowsTheLatestFiveEntriesAndFollowsNewOnes()
    {
        for (var i = 0; i < 7; i++)
        {
            _activity.Add(new ActivityEntry(DateTimeOffset.UtcNow, "Icon.Devices", $"Evento {i}", "Texto"));
        }

        var page = Create();
        page.OnNavigatedTo();
        await WaitUntilAsync(() => _health.Reads >= 1);

        Assert.Equal(5, page.ActivityRows.Count);
        Assert.True(page.HasActivity);
        Assert.EndsWith("Evento 6: Texto", page.ActivityRows[0].Value, StringComparison.Ordinal);

        _activity.Add(new ActivityEntry(DateTimeOffset.UtcNow, "Icon.Devices", "Novo", "Texto"));

        Assert.EndsWith("Novo: Texto", page.ActivityRows[0].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutActivityTheListIsEmpty()
    {
        var page = Create();

        page.OnNavigatedTo();

        Assert.False(page.HasActivity);
    }
}
