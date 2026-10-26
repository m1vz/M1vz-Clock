using System.IO;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Dialogs;
using M1vClock.App.Hosting;
using M1vClock.App.Pages.Performance;
using M1vClock.App.Services;
using M1vClock.Benchmarks.Results;
using M1vClock.Benchmarks.Stress;
using M1vClock.Core.Privacy;
using M1vClock.Core.Results;
using M1vClock.Hardware.Platform;
using M1vClock.Tuning;
using M1vClock.Tuning.Gpu;
using M1vClock.Tuning.Stability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.App.Tests;

public sealed class OverclockPageTests : IDisposable
{
    private static readonly GpuTuningInfo ThisRadeon = new(GpuVendor.Amd, "AMD Radeon RX 5500 XT", true, true, true,
        new TuningRange(800, 2200, 1), null, new TuningRange(700, 1150, 1), null, new TuningRange(-50, 20, 1), 0, 0, PowerUnit.Percent);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "m1vClock-oc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class Platform : IPlatformService
    {
        public Task<PlatformReport> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new PlatformReport(
            ReadResult.Failure<OsInfo>("n/d"),
            ReadResult.Success(new BoardInfo("ASUSTeK COMPUTER INC.", "TUF GAMING B550M-PLUS", "Rev X.0x", new SensitiveText("x"))),
            ReadResult.Failure<BiosInfo>("n/d"),
            ReadResult.Failure<IReadOnlyList<CpuInfo>>("n/d")));
    }

    private sealed class Machine : IMachineProfile
    {
        public Task<BenchmarkMachine> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BenchmarkMachine("AMD Ryzen 7 5700X 8-Core Processor", "AMD Radeon RX 5500 XT", 16, 31.9, "Windows 11", "0.1.0"));
    }

    private sealed class Reader(GpuTuningReading reading) : IGpuTuningReader
    {
        public GpuVendor Vendor => GpuVendor.Amd;

        public GpuTuningReading Read() => reading;
    }

    private sealed class Errors(HardwareErrorReport report) : IHardwareErrorSource
    {
        public HardwareErrorReport ErrorsSince(DateTimeOffset since) => report;
    }

    private sealed class ScriptedRunner : IStabilityRunner
    {
        public Func<CancellationToken, Task<StabilityEvidence>>? Next { get; set; }

        public (StabilityTarget Target, TimeSpan Duration)? Asked { get; private set; }

        public Task<StabilityEvidence> RunAsync(StabilityTarget target, TimeSpan duration, IProgress<StabilityProgress> progress, CancellationToken cancellationToken)
        {
            Asked = (target, duration);
            return Next!(cancellationToken);
        }
    }

    private (OverclockPageViewModel Page, ScriptedRunner Runner, FakeNvml Nvml, SessionGuard Guard, DialogService Dialogs) Open(GpuTuningReading? amd = null, bool elevated = true, FakeNvml? nvml = null, HardwareErrorReport? errors = null)
    {
        var runner = new ScriptedRunner();
        var tuner = new NvidiaTuner(() => nvml, NullLogger<NvidiaTuner>.Instance);
        var errorSource = new Errors(errors ?? HardwareErrorReport.Empty);
        var guard = new SessionGuard(Path.Combine(_folder, "gpu-tuning.json"), errorSource, tuner, NullLogger<SessionGuard>.Instance);
        var dialogs = new DialogService();
        var page = new OverclockPageViewModel(
            new OverclockServices(new Platform(), new Machine(), new Reader(amd ?? new GpuTuningReading(GpuReadStatus.Read, [ThisRadeon])), tuner, guard, errorSource, runner, dialogs,
                new AppInfo("0.1.0", _folder, elevated), new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 23, 0, 0, TimeSpan.Zero))),
            new CollectingLogger<OverclockPageViewModel>());
        return (page, runner, nvml ?? new FakeNvml(), guard, dialogs);
    }

    [Fact]
    public async Task TheRadeonOfThisPcIsShownReadOnlyWithItsRealRanges()
    {
        var (page, _, _, _, _) = Open();

        await page.RefreshAsync();

        var gpu = Assert.Single(page.Gpus);
        Assert.False(gpu.CanTune);
        Assert.Equal(
        [
            new TuningLine("Clock ajustável", "800 a 2.200 MHz"),
            new TuningLine("Tensão ajustável", "700 a 1.150 mV"),
            new TuningLine("Limite de energia", "agora 0 % (padrão 0 %; faixa -50 a 20 %)"),
            new TuningLine("Situação", "Configuração de fábrica"),
        ], gpu.Lines);
        Assert.StartsWith("Lido pela ADLX", gpu.Note, StringComparison.Ordinal);
        Assert.False(page.HasGpuProblem);
    }

    [Fact]
    public async Task TheGuideFollowsTheBoardAndCpuOfThisPc()
    {
        var (page, _, _, _, _) = Open();

        await page.RefreshAsync();

        Assert.Equal("Guia de BIOS · TUF GAMING B550M-PLUS · AMD Ryzen 7 5700X 8-Core Processor", page.GuideTitle);
        Assert.Equal("Ative o perfil da memória (D.O.C.P.)", page.Guide[1].Title);
        Assert.Contains("entre -10 e -25", page.Guide[3].Text, StringComparison.Ordinal);
        Assert.False(page.Guide[^1].HasMenu);
    }

    [Fact]
    public async Task WithoutAnyCardTheProblemIsShownAndRecentErrorsAreSummarized()
    {
        var shutdown = new HardwareErrorReport([new HardwareErrorEvent(new DateTimeOffset(2026, 10, 23, 13, 59, 0, TimeSpan.Zero), HardwareErrorKind.UnexpectedShutdown, "Microsoft-Windows-Kernel-Power", 41)]);
        var (page, _, _, _, _) = Open(amd: GpuTuningReading.Missing, errors: shutdown);

        await page.RefreshAsync();

        Assert.True(page.HasGpuProblem);
        Assert.StartsWith("WHEA: 0 · resets do driver de vídeo: 0 · desligamentos inesperados: 1", page.ErrorsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANvidiaCardIsTunedAfterConfirmationAndTheGuardRemembersIt()
    {
        var nvml = new FakeNvml();
        var (page, _, _, guard, dialogs) = Open(amd: GpuTuningReading.Missing, nvml: nvml);
        await page.RefreshAsync();
        var gpu = Assert.Single(page.Gpus);
        Assert.True(gpu.CanTune);
        Assert.Equal(["Sem teto", "Até 2.505 MHz", "Até 2.305 MHz", "Até 2.005 MHz"], gpu.CapLabels);

        gpu.PowerText = "180";
        gpu.CapIndex = 2;
        var applying = page.ApplyAsync(gpu);
        Assert.Equal(["Limite de energia: 180 W", "Teto de clock: 2305 MHz"], dialogs.Current!.Changes);
        dialogs.ConfirmCommand.Execute(null);
        await applying;

        Assert.Equal(180_000u, nvml.Limit);
        Assert.Equal((210u, 2_305u), nvml.Locked);
        Assert.Equal(new TuningMarker(GpuVendor.Nvidia, 0, "NVIDIA GeForce RTX 4070", new DateTimeOffset(2026, 10, 26, 23, 0, 0, TimeSpan.Zero), 180, 2_305), guard.Load());

        await page.RestoreAsync(page.Gpus[0]);
        Assert.Equal(200_000u, nvml.Limit);
        Assert.Null(guard.Load());
        Assert.Equal("Placa de vídeo de volta ao padrão de fábrica.", page.GpuResult);
    }

    [Fact]
    public async Task WithoutAdministratorOrWithAWrongPowerNothingIsTouched()
    {
        var nvml = new FakeNvml();
        var (page, _, _, guard, dialogs) = Open(amd: GpuTuningReading.Missing, elevated: false, nvml: nvml);
        await page.RefreshAsync();

        await page.ApplyAsync(page.Gpus[0]);
        Assert.Equal("Para mudar a placa de vídeo, abra o m1vClock como administrador.", page.GpuResult);

        var (elevated, _, _, _, _) = Open(amd: GpuTuningReading.Missing, nvml: nvml);
        await elevated.RefreshAsync();
        elevated.Gpus[0].PowerText = "999";
        await elevated.ApplyAsync(elevated.Gpus[0]);

        Assert.Equal("O limite de energia precisa ficar entre 100 e 250 W.", elevated.GpuResult);
        Assert.Equal(200_000u, nvml.Limit);
        Assert.Null(dialogs.Current);
        Assert.Null(guard.Load());
    }

    [Fact]
    public async Task AStableCheckShowsTheVerdictAndTheHint()
    {
        var (page, runner, _, _, _) = Open();
        runner.Next = _ => Task.FromResult(new StabilityEvidence(StabilityTarget.Everything, StabilityStop.Completed, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15), 0, 71, 63, HardwareErrorReport.Empty));
        page.TargetIndex = 2;

        await page.ToggleCheckAsync();

        Assert.Equal((StabilityTarget.Everything, TimeSpan.FromMinutes(15)), runner.Asked);
        Assert.Equal("Estável", page.VerdictTitle);
        Assert.Equal(MetricState.Good, page.VerdictTone);
        Assert.Equal("Duração 15:00 · máx. CPU 71 °C · máx. GPU 63 °C · WHEA 0 · resets de vídeo 0 · erros de memória 0", page.VerdictDetail);
        Assert.StartsWith("Nenhum erro.", Assert.Single(page.VerdictReasons), StringComparison.Ordinal);
        Assert.False(page.IsChecking);
    }

    [Fact]
    public async Task AnUnstableCheckListsEveryReason()
    {
        var (page, runner, _, _, _) = Open();
        var whea = new HardwareErrorReport([new HardwareErrorEvent(DateTimeOffset.UnixEpoch, HardwareErrorKind.Whea, "Microsoft-Windows-WHEA-Logger", 19)]);
        runner.Next = _ => Task.FromResult(new StabilityEvidence(StabilityTarget.CpuAndMemory, StabilityStop.HardwareErrors, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(15), 12, 80, null, whea));

        await page.ToggleCheckAsync();

        Assert.Equal("Instável", page.VerdictTitle);
        Assert.Equal(MetricState.Critical, page.VerdictTone);
        Assert.Equal(2, page.VerdictReasons.Count);
        Assert.StartsWith("O Windows registrou erro de hardware", page.VerdictReasons[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedStartOrAStopIsExplainedWithoutAVerdict()
    {
        var (page, runner, _, _, _) = Open();
        runner.Next = _ => throw new StabilityStartException(StabilityStartStatus.NoTemperatureSensor);
        await page.ToggleCheckAsync();
        Assert.StartsWith("Sem leitura de temperatura", page.CheckStatus, StringComparison.Ordinal);
        Assert.False(page.HasVerdict);

        var gate = new TaskCompletionSource();
        runner.Next = async token =>
        {
            gate.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        var running = page.ToggleCheckCommand.ExecuteAsync(null);
        await gate.Task;
        Assert.True(page.IsChecking);
        Assert.Equal("Parar", page.CheckButtonText);
        Assert.True(page.ToggleCheckCommand.CanExecute(null));
        await page.ToggleCheckCommand.ExecuteAsync(null);
        await running;

        Assert.Equal("Verificação cancelada.", page.CheckStatus);
        Assert.False(page.HasVerdict);
    }

    [Theory]
    [InlineData(StressEnd.Completed, false, StabilityStop.Completed)]
    [InlineData(StressEnd.StoppedByUser, false, StabilityStop.StoppedByUser)]
    [InlineData(StressEnd.StoppedByUser, true, StabilityStop.HardwareErrors)]
    [InlineData(StressEnd.MemoryErrors, false, StabilityStop.MemoryErrors)]
    [InlineData(StressEnd.Temperature, false, StabilityStop.Temperature)]
    [InlineData(StressEnd.WorkloadFailed, false, StabilityStop.WorkloadFailed)]
    public void TheStressEndBecomesTheStabilityStop(StressEnd end, bool forErrors, StabilityStop expected) =>
        Assert.Equal(expected, StabilityRunner.StopOf(end, forErrors));
}

internal sealed class FakeNvml : INvmlApi
{
    public uint Limit { get; set; } = 200_000;

    public (uint Min, uint Max)? Locked { get; set; }

    public NvmlReturn Init() => NvmlReturn.Success;

    public void Shutdown()
    {
    }

    public NvmlReturn DeviceCount(out uint count)
    {
        count = 1;
        return NvmlReturn.Success;
    }

    public NvmlReturn Handle(uint index, out IntPtr device)
    {
        device = new IntPtr(1);
        return index == 0 ? NvmlReturn.Success : NvmlReturn.InvalidArgument;
    }

    public NvmlReturn Name(IntPtr device, out string name)
    {
        name = "NVIDIA GeForce RTX 4070";
        return NvmlReturn.Success;
    }

    public NvmlReturn PowerConstraints(IntPtr device, out uint minimumMilliwatts, out uint maximumMilliwatts)
    {
        minimumMilliwatts = 100_000;
        maximumMilliwatts = 250_000;
        return NvmlReturn.Success;
    }

    public NvmlReturn PowerLimit(IntPtr device, out uint milliwatts)
    {
        milliwatts = Limit;
        return NvmlReturn.Success;
    }

    public NvmlReturn PowerDefault(IntPtr device, out uint milliwatts)
    {
        milliwatts = 200_000;
        return NvmlReturn.Success;
    }

    public NvmlReturn SetPowerLimit(IntPtr device, uint milliwatts)
    {
        Limit = milliwatts;
        return NvmlReturn.Success;
    }

    public NvmlReturn MaxGraphicsClock(IntPtr device, out uint megahertz)
    {
        megahertz = 2_805;
        return NvmlReturn.Success;
    }

    public NvmlReturn GraphicsClock(IntPtr device, out uint megahertz)
    {
        megahertz = 210;
        return NvmlReturn.Success;
    }

    public NvmlReturn LockGraphicsClocks(IntPtr device, uint minimumMegahertz, uint maximumMegahertz)
    {
        Locked = (minimumMegahertz, maximumMegahertz);
        return NvmlReturn.Success;
    }

    public NvmlReturn ResetGraphicsClocks(IntPtr device)
    {
        Locked = null;
        return NvmlReturn.Success;
    }
}
