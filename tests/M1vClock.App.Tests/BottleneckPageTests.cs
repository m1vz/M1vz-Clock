using System.IO;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Pages.Games;
using M1vClock.App.Services;
using M1vClock.Core.Settings;
using M1vClock.Games.Bottleneck;
using M1vClock.Games.Frames;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.App.Tests;

internal static class Snapshots
{
    public static SensorReading R(SensorHardware hardware, string name, SensorKind kind, float? value, string id = "x") =>
        new(id, hardware.ToString(), hardware, name, kind, value, null, null);

    public static SensorSnapshot Of(params SensorReading[] readings) => new(DateTimeOffset.UnixEpoch, readings);

    public static SensorSnapshot Idle() => Of(
        R(SensorHardware.Cpu, "CPU Total", SensorKind.Load, 9.87f),
        R(SensorHardware.Cpu, "CPU Core Max", SensorKind.Load, 23.97f),
        R(SensorHardware.Cpu, "CPU Core #1", SensorKind.Load, 9.09f),
        R(SensorHardware.Cpu, "Core (Tctl/Tdie)", SensorKind.Temperature, 0),
        R(SensorHardware.Gpu, "GPU Core", SensorKind.Load, 0),
        R(SensorHardware.Gpu, "GPU Memory Used", SensorKind.SmallData, 1455),
        R(SensorHardware.Gpu, "GPU Memory Total", SensorKind.SmallData, 8176),
        R(SensorHardware.Gpu, "GPU Core", SensorKind.Temperature, 42),
        R(SensorHardware.Memory, "Memory", SensorKind.Load, 67.949036f, "/ram/load/0"));

    public static SensorSnapshot Busy(float gpu, float cpu = 40, float core = 50) => Of(
        R(SensorHardware.Cpu, "CPU Total", SensorKind.Load, cpu),
        R(SensorHardware.Cpu, "CPU Core Max", SensorKind.Load, core),
        R(SensorHardware.Cpu, "Core (Tctl/Tdie)", SensorKind.Temperature, 70),
        R(SensorHardware.Gpu, "GPU Core", SensorKind.Load, gpu),
        R(SensorHardware.Gpu, "GPU Memory Used", SensorKind.SmallData, 4000),
        R(SensorHardware.Gpu, "GPU Memory Total", SensorKind.SmallData, 8176),
        R(SensorHardware.Gpu, "GPU Core", SensorKind.Temperature, 75),
        R(SensorHardware.Memory, "Memory", SensorKind.Load, 60, "/ram/load/0"));
}

internal sealed class FakeFrames : IFrameMonitor
{
    public FrameMonitorState State { get; set; } = FrameMonitorState.Running;

    public FrameStats? Foreground { get; set; }

    public int Acquired { get; private set; }

    public int Released { get; private set; }

    public IDisposable Acquire()
    {
        Acquired++;
        return new Lease(() => Released++);
    }

    public FrameStats? GetStats(int processId) => Foreground;

    public FrameStats? GetForegroundStats() => Foreground;

    private sealed class Lease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}

public sealed class BottleneckSamplerTests
{
    [Fact]
    public void TheRealIdleReadingOfThisPcBecomesACappedSample()
    {
        var sample = BottleneckSampler.From(Snapshots.Idle());

        Assert.Equal(0, sample.GpuLoad);
        Assert.Equal(9.87f, sample.CpuTotal);
        Assert.Equal(23.97f, sample.CpuBusiestCore);
        Assert.Equal(17.8, sample.VramPercent!.Value, 1);
        Assert.Equal(67.949036f, sample.RamPercent);
        Assert.Equal(42, sample.GpuTemperature);
        Assert.Null(sample.CpuTemperature);
        Assert.Equal(Limiter.Capped, sample.Classify());
    }

    [Fact]
    public void WithoutThePrecomputedMaximumTheBusiestCoreIsTheHighestOfTheCores()
    {
        var snapshot = Snapshots.Of(
            Snapshots.R(SensorHardware.Cpu, "CPU Core #1", SensorKind.Load, 10),
            Snapshots.R(SensorHardware.Cpu, "CPU Core #2", SensorKind.Load, 88),
            Snapshots.R(SensorHardware.Cpu, "CPU Core #2 Thread #1", SensorKind.Load, 99),
            Snapshots.R(SensorHardware.Cpu, "CPU Total", SensorKind.Load, 30));

        Assert.Equal(88, BottleneckSampler.BusiestCore(snapshot));
        Assert.Null(BottleneckSampler.BusiestCore(Snapshots.Of(Snapshots.R(SensorHardware.Cpu, "CPU Total", SensorKind.Load, 30))));
    }

    [Fact]
    public void VideoMemoryFallsBackToTheDirect3DCountersAndIsCappedAtOneHundred()
    {
        var d3d = Snapshots.Of(
            Snapshots.R(SensorHardware.Gpu, "D3D Dedicated Memory Used", SensorKind.SmallData, 1455.7383f),
            Snapshots.R(SensorHardware.Gpu, "D3D Dedicated Memory Total", SensorKind.SmallData, 8151.504f));
        var over = Snapshots.Of(
            Snapshots.R(SensorHardware.Gpu, "GPU Memory Used", SensorKind.SmallData, 9000),
            Snapshots.R(SensorHardware.Gpu, "GPU Memory Total", SensorKind.SmallData, 8000));

        Assert.Equal(17.86, BottleneckSampler.VideoMemoryPercent(d3d)!.Value, 1);
        Assert.Equal(100, BottleneckSampler.VideoMemoryPercent(over));
    }

    [Fact]
    public void VideoMemoryIsUnknownWithoutATotal()
    {
        var onlyUsed = Snapshots.Of(Snapshots.R(SensorHardware.Gpu, "GPU Memory Used", SensorKind.SmallData, 1000));
        var zeroTotal = Snapshots.Of(
            Snapshots.R(SensorHardware.Gpu, "GPU Memory Used", SensorKind.SmallData, 1000),
            Snapshots.R(SensorHardware.Gpu, "GPU Memory Total", SensorKind.SmallData, 0));

        Assert.Null(BottleneckSampler.VideoMemoryPercent(onlyUsed));
        Assert.Null(BottleneckSampler.VideoMemoryPercent(zeroTotal));
        Assert.Null(BottleneckSampler.VideoMemoryPercent(Snapshots.Of()));
    }
}

public sealed class BottleneckPageViewModelTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("m1v-bottleneck-");
    private readonly FakeSensorService _sensors = new() { State = SensorServiceState.Running };
    private readonly FakeFrames _frames = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly AppSettingsService _settings;
    private readonly BottleneckPageViewModel _page;

    public BottleneckPageViewModelTests()
    {
        var store = new SettingsStore<AppSettings>(Path.Combine(_temp.FullName, "settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);
        _settings = new AppSettingsService(store, NullLogger<AppSettingsService>.Instance);
        _page = new BottleneckPageViewModel(_sensors, new InlineUiThread(), _settings, _clock, _frames);
    }

    public void Dispose()
    {
        _page.Dispose();
        _temp.Delete(recursive: true);
    }

    private void Tick(SensorSnapshot snapshot)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        _sensors.Publish(snapshot);
    }

    [Fact]
    public void BeforeTheFirstReadingThePageIsWaitingAndCannotRecord()
    {
        _page.OnNavigatedTo();

        Assert.True(_page.IsWaiting);
        Assert.False(_page.HasLive);
        Assert.False(_page.CanStart);
        Assert.Equal(1, _sensors.Subscriptions);
        Assert.True(_sensors.HasSnapshotSubscribers);
    }

    [Fact]
    public void AReadingShowsTheLiveLimiterTheValuesAndTheAdvice()
    {
        _page.OnNavigatedTo();

        Tick(Snapshots.Idle());

        Assert.False(_page.IsWaiting);
        Assert.True(_page.CanStart);
        Assert.Equal("Nada no limite", _page.LiveTitle);
        Assert.Equal(MetricState.Neutral, _page.LiveTone);
        Assert.Equal("0 %", _page.GpuText);
        Assert.Equal("9,9 %", _page.CpuText);
        Assert.Equal("24 %", _page.CoreText);
        Assert.Equal("17,8 %", _page.VramText);
        Assert.Equal("67,9 %", _page.RamText);
        Assert.Equal("42 °C", _page.GpuTempText);
        Assert.Equal("—", _page.CpuTempText);
        Assert.StartsWith("Nem a CPU nem a GPU", _page.LiveAdvice, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFramesPerSecondOfTheProgramInFrontAreShownWithTheOnePercentLow()
    {
        _frames.Foreground = new FrameStats(42, "jogo", 143.4, 6.97, 98.6, []);
        _page.OnNavigatedTo();

        Tick(Snapshots.Busy(gpu: 97));

        Assert.Equal("143", _page.FpsText);
        Assert.Equal("99", _page.FpsLowText);
        Assert.False(_page.HasFpsHint);
    }

    [Theory]
    [InlineData(FrameMonitorState.NeedsAdministrator, "Abra o m1vClock como administrador para medir os quadros por segundo.")]
    [InlineData(FrameMonitorState.Failed, "Não foi possível medir os quadros por segundo. O motivo está no log do app.")]
    [InlineData(FrameMonitorState.Running, "Deixe o jogo na frente, com a imagem se movendo, para medir os quadros por segundo.")]
    public void WithoutFramesTheHintExplainsWhyAndTheValuesAreDashes(FrameMonitorState state, string hint)
    {
        _frames.State = state;
        _page.OnNavigatedTo();

        Tick(Snapshots.Busy(gpu: 97));

        Assert.Equal("—", _page.FpsText);
        Assert.Equal("—", _page.FpsLowText);
        Assert.Equal(hint, _page.FpsHint);
        Assert.True(_page.HasFpsHint);
    }

    [Fact]
    public void TheFrameMeasurementIsAcquiredOnArrivingAndReleasedOnLeaving()
    {
        _page.OnNavigatedTo();
        _page.OnNavigatedTo();

        Assert.Equal(1, _frames.Acquired);

        _page.OnNavigatedFrom();

        Assert.Equal(1, _frames.Released);
    }

    [Fact]
    public void ASessionWithFramesReportsTheAverageFpsAndOnePercentLow()
    {
        _frames.Foreground = new FrameStats(42, "jogo", 60, 16.7, 45, []);
        _page.OnNavigatedTo();
        Tick(Snapshots.Idle());
        _page.Start();
        for (var i = 0; i < 12; i++)
        {
            Tick(Snapshots.Busy(gpu: 97));
        }

        _page.Stop();

        Assert.Equal("Quadros por segundo: média 60, 1% pior 45", _page.ReportFps);
        Assert.True(_page.HasReportFps);
    }

    [Fact]
    public void ASessionWithoutFramesHasNoFpsLine()
    {
        _page.OnNavigatedTo();
        Tick(Snapshots.Idle());
        _page.Start();
        for (var i = 0; i < 12; i++)
        {
            Tick(Snapshots.Busy(gpu: 97));
        }

        _page.Stop();

        Assert.Equal(string.Empty, _page.ReportFps);
        Assert.False(_page.HasReportFps);
    }

    [Fact]
    public void AGpuAtTheLimitIsGoodAndACpuBottleneckIsAWarning()
    {
        _page.OnNavigatedTo();

        Tick(Snapshots.Busy(gpu: 97));
        Assert.Equal("GPU no limite", _page.LiveTitle);
        Assert.Equal(MetricState.Good, _page.LiveTone);

        Tick(Snapshots.Busy(gpu: 50, cpu: 60, core: 95));
        Assert.Equal("Gargalo de CPU", _page.LiveTitle);
        Assert.Equal(MetricState.Warning, _page.LiveTone);
    }

    [Fact]
    public void TheTemperatureFollowsTheUnitChosenInTheSettings()
    {
        _settings.Update(s => s.TemperatureUnit = TemperatureUnit.Fahrenheit);
        _page.OnNavigatedTo();

        Tick(Snapshots.Idle());

        Assert.Equal("107,6 °F", _page.GpuTempText);
    }

    [Fact]
    public void ARecordingFillsTheProgressAndAtTheEndShowsTheDistributionAndAdvice()
    {
        _page.OnNavigatedTo();
        Tick(Snapshots.Idle());
        _page.DurationIndex = 0;

        _page.Start();

        Assert.True(_page.IsRecording);
        Assert.False(_page.CanStart);
        Assert.Equal("Gravando: 0:00 de 1:00", _page.RecordingText);

        for (var i = 0; i < 30; i++)
        {
            Tick(Snapshots.Busy(gpu: 97));
        }

        Assert.True(_page.IsRecording);
        Assert.Equal(0.5, _page.Fraction, 2);
        Assert.Equal("Gravando: 0:30 de 1:00", _page.RecordingText);

        for (var i = 0; i < 20; i++)
        {
            Tick(Snapshots.Busy(gpu: 50, cpu: 60, core: 95));
        }

        for (var i = 0; i < 10; i++)
        {
            Tick(Snapshots.Idle());
        }

        Assert.False(_page.IsRecording);
        Assert.True(_page.HasReport);
        Assert.Equal("Principal limite: GPU no limite", _page.ReportMain);
        Assert.StartsWith("Normal em jogos pesados", _page.ReportAdvice, StringComparison.Ordinal);
        Assert.Equal(["GPU no limite", "Gargalo de CPU", "Nada no limite"], _page.ReportRows.Select(r => r.Name));
        Assert.Equal(["50 %", "33 %", "17 %"], _page.ReportRows.Select(r => r.PercentText));
        Assert.Equal(0.5, _page.ReportRows[0].Fraction, 2);
        Assert.Equal("60 medições", _page.ReportSamples);
        Assert.False(_page.HasReportWarning);
        Assert.StartsWith("Médias: GPU", _page.ReportAverages, StringComparison.Ordinal);
        Assert.Contains("núcleo 95 %", _page.ReportPeaks, StringComparison.Ordinal);
    }

    [Fact]
    public void StoppingEarlyWithFewReadingsGivesAResultWithAWarning()
    {
        _page.OnNavigatedTo();
        Tick(Snapshots.Idle());
        _page.Start();
        for (var i = 0; i < 4; i++)
        {
            Tick(Snapshots.Busy(gpu: 97));
        }

        _page.Stop();

        Assert.False(_page.IsRecording);
        Assert.Equal("Principal limite: GPU no limite", _page.ReportMain);
        Assert.Equal("A sessão teve poucas medições (4). Grave por mais tempo para o resultado valer.", _page.ReportWarning);
        Assert.True(_page.HasReportWarning);
    }

    [Fact]
    public void StoppingWithNoReadingsSaysSoInsteadOfInventingAConclusion()
    {
        _page.OnNavigatedTo();
        Tick(Snapshots.Idle());
        _page.Start();

        _page.Stop();

        Assert.Equal("A sessão terminou sem nenhuma medição.", _page.ReportWarning);
        Assert.Equal(string.Empty, _page.ReportMain);
        Assert.Empty(_page.ReportRows);
        Assert.True(_page.HasReport);
    }

    [Fact]
    public void StartingANewRecordingClearsTheOldResult()
    {
        _page.OnNavigatedTo();
        Tick(Snapshots.Idle());
        _page.Start();
        Tick(Snapshots.Busy(gpu: 97));
        _page.Stop();
        Assert.True(_page.HasReport);

        _page.Start();

        Assert.False(_page.HasReport);
        Assert.Empty(_page.ReportRows);
    }

    [Fact]
    public void RecordingCannotStartWhileWaitingOrWhileAlreadyRecording()
    {
        _page.OnNavigatedTo();
        _page.Start();
        Assert.False(_page.IsRecording);

        Tick(Snapshots.Idle());
        _page.Start();
        Tick(Snapshots.Busy(gpu: 97));
        var text = _page.RecordingText;
        _page.Start();

        Assert.True(_page.IsRecording);
        Assert.Equal(text, _page.RecordingText);
    }

    [Fact]
    public void LeavingThePageDiscardsTheRecordingAndReleasesTheSensors()
    {
        _page.OnNavigatedTo();
        Tick(Snapshots.Idle());
        _page.Start();
        Tick(Snapshots.Busy(gpu: 97));

        _page.OnNavigatedFrom();

        Assert.False(_page.IsRecording);
        Assert.False(_page.HasReport);
        Assert.Equal(1, _sensors.Releases);
        Assert.False(_sensors.HasSnapshotSubscribers);
    }

    [Fact]
    public void PausedSensorsShowTheWarningUnlessARecordingIsRunning()
    {
        _page.OnNavigatedTo();
        Tick(Snapshots.Idle());
        Assert.False(_page.HasNoSensors);

        _sensors.Change(SensorServiceState.Paused);
        Assert.True(_page.HasNoSensors);

        _sensors.Change(SensorServiceState.Running);
        Assert.False(_page.HasNoSensors);
    }
}
