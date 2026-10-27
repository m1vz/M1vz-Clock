using M1vClock.Tuning.Bios;
using M1vClock.Tuning.Gpu;
using M1vClock.Tuning.Stability;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Tuning.Tests;

public sealed class StabilityJudgeTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 22, 0, 0, TimeSpan.Zero);

    private static StabilityEvidence Evidence(StabilityStop stop = StabilityStop.Completed, long memoryErrors = 0, HardwareErrorReport? errors = null, int minutes = 15) =>
        new(StabilityTarget.Everything, stop, TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(15), memoryErrors, 78, 74, errors ?? HardwareErrorReport.Empty);

    private static HardwareErrorReport Errors(params HardwareErrorKind[] kinds) =>
        new(kinds.Select((k, i) => new HardwareErrorEvent(Start.AddMinutes(i), k, "x", 1)).ToList());

    [Fact]
    public void AFullRunWithoutAnyErrorIsStable() =>
        Assert.Equal(new StabilityVerdict(StabilityResult.Stable, []), StabilityJudge.Decide(Evidence()));

    [Fact]
    public void WheaDriverResetsAndMemoryErrorsMakeItUnstableAndAreAllListed()
    {
        var verdict = StabilityJudge.Decide(Evidence(StabilityStop.MemoryErrors, 3, Errors(HardwareErrorKind.Whea, HardwareErrorKind.DriverReset)));

        Assert.Equal(StabilityResult.Unstable, verdict.Result);
        Assert.Equal([StabilityReason.WheaErrors, StabilityReason.DriverResets, StabilityReason.MemoryErrors], verdict.Reasons);
    }

    [Fact]
    public void ACrashedWorkloadIsUnstableEvenWithoutWindowsErrors() =>
        Assert.Equal([StabilityReason.WorkloadCrashed], StabilityJudge.Decide(Evidence(StabilityStop.WorkloadFailed)).Reasons);

    [Theory]
    [InlineData(StabilityStop.Temperature, StabilityReason.TooHot)]
    [InlineData(StabilityStop.StoppedByUser, StabilityReason.StoppedEarly)]
    [InlineData(StabilityStop.SensorLost, StabilityReason.SensorLost)]
    [InlineData(StabilityStop.WindowUnresponsive, StabilityReason.WindowUnresponsive)]
    public void AStopForSafetyOrByTheUserProvesNothing(StabilityStop stop, StabilityReason reason)
    {
        var verdict = StabilityJudge.Decide(Evidence(stop));

        Assert.Equal(StabilityResult.Inconclusive, verdict.Result);
        Assert.Equal([reason], verdict.Reasons);
    }

    [Fact]
    public void TooShortARunOrUnreadableEventsAreInconclusive()
    {
        Assert.Equal([StabilityReason.StoppedEarly], StabilityJudge.Decide(Evidence(minutes: 2)).Reasons);
        Assert.Equal([StabilityReason.ErrorsUnreadable], StabilityJudge.Decide(Evidence(errors: new HardwareErrorReport([], "acesso negado"))).Reasons);
    }

    [Fact]
    public void AnErrorBeatsAnEarlyStop() =>
        Assert.Equal(StabilityResult.Unstable, StabilityJudge.Decide(Evidence(StabilityStop.StoppedByUser, errors: Errors(HardwareErrorKind.Whea))).Result);

    [Theory]
    [InlineData("Microsoft-Windows-WHEA-Logger", 19, HardwareErrorKind.Whea)]
    [InlineData("Microsoft-Windows-WHEA-Logger", 47, HardwareErrorKind.Whea)]
    [InlineData("Display", 4101, HardwareErrorKind.DriverReset)]
    [InlineData("nvlddmkm", 153, HardwareErrorKind.DriverReset)]
    [InlineData("Microsoft-Windows-Kernel-Power", 41, HardwareErrorKind.UnexpectedShutdown)]
    public void TheEventsThatMatterAreClassified(string provider, int id, HardwareErrorKind kind) =>
        Assert.Equal(kind, HardwareErrorQuery.Classify(provider, id));

    [Theory]
    [InlineData("amdkmdag", 4101)]
    [InlineData("Display", 4100)]
    [InlineData("nvlddmkm", 14)]
    [InlineData("Microsoft-Windows-Kernel-Power", 109)]
    public void OtherDriverMessagesAreNotCountedAsResets(string provider, int id) =>
        Assert.Null(HardwareErrorQuery.Classify(provider, id));

    [Fact]
    public void TheQueryStartsAtTheGivenMomentInUtc() =>
        Assert.Contains("@SystemTime>='2026-10-26T22:00:00.000Z'", HardwareErrorQuery.XPath(Start.ToOffset(TimeSpan.FromHours(-3))), StringComparison.Ordinal);

    [Fact]
    public void TheRealSystemLogOfThisPcIsReadAndItsUnexpectedShutdownsAppear()
    {
        var report = new WindowsHardwareErrorSource(NullLogger<WindowsHardwareErrorSource>.Instance).ErrorsSince(DateTimeOffset.UtcNow.AddDays(-30));

        Assert.True(report.CouldRead, report.ReadError);
        Assert.All(report.Events, e => Assert.True(e.Time >= DateTimeOffset.UtcNow.AddDays(-30).AddMinutes(-1)));
        TestContext.Current.SendDiagnosticMessage($"30 dias: WHEA {report.Whea}, resets {report.DriverResets}, quedas {report.UnexpectedShutdowns}");
    }
}

public sealed class BiosGuideTests
{
    [Fact]
    public void ThisPcGetsTheAsusAm4Guide()
    {
        var guide = BiosGuides.Build("ASUSTeK COMPUTER INC.", "TUF GAMING B550M-PLUS", "AMD Ryzen 7 5700X 8-Core Processor             ");

        Assert.Equal(BoardMaker.Asus, guide.Maker);
        Assert.Equal(CpuFamily.AmdAm4, guide.Family);
        Assert.Equal("D.O.C.P.", guide.MemoryProfileName);
        Assert.Equal("TUF GAMING B550M-PLUS", guide.Board);
        Assert.Equal(
        [
            GuideStepKind.SaveProfile, GuideStepKind.MemoryProfile, GuideStepKind.PrecisionBoostOverdrive,
            GuideStepKind.CurveOptimizer, GuideStepKind.BoostOverride, GuideStepKind.EcoMode, GuideStepKind.TestEachStep,
        ], guide.Steps.Select(s => s.Kind));
        Assert.Equal("Ai Tweaker → Ai Overclock Tuner → D.O.C.P.", guide.Steps[1].Menu);
        Assert.Equal("Advanced → AMD Overclocking → Precision Boost Overdrive → Curve Optimizer", guide.Steps[3].Menu);
    }

    [Theory]
    [InlineData("AMD Ryzen 7 5700X 8-Core Processor", CpuFamily.AmdAm4)]
    [InlineData("AMD Ryzen 5 3600 6-Core Processor", CpuFamily.AmdAm4)]
    [InlineData("AMD Ryzen 7 9800X3D 8-Core Processor", CpuFamily.AmdAm5)]
    [InlineData("AMD Ryzen 5 7600 6-Core Processor", CpuFamily.AmdAm5)]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700K", CpuFamily.IntelUnlocked)]
    [InlineData("Intel(R) Core(TM) i9-14900KF", CpuFamily.IntelUnlocked)]
    [InlineData("Intel(R) Core(TM) i5-12400F CPU @ 2.50GHz", CpuFamily.IntelLocked)]
    [InlineData("Intel(R) Core(TM) Ultra 9 285K", CpuFamily.IntelUnlocked)]
    [InlineData("Qualcomm Snapdragon X Elite", CpuFamily.Unknown)]
    public void TheCpuFamilyIsRecognized(string cpu, CpuFamily family) =>
        Assert.Equal(family, BiosGuides.FamilyOf(cpu));

    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.", BoardMaker.Asus)]
    [InlineData("Gigabyte Technology Co., Ltd.", BoardMaker.Gigabyte)]
    [InlineData("Micro-Star International Co., Ltd.", BoardMaker.Msi)]
    [InlineData("ASRock", BoardMaker.AsRock)]
    [InlineData("Dell Inc.", BoardMaker.Other)]
    [InlineData(null, BoardMaker.Other)]
    public void TheBoardMakerIsRecognized(string? manufacturer, BoardMaker maker) =>
        Assert.Equal(maker, BiosGuides.MakerOf(manufacturer));

    [Fact]
    public void IntelChipsGetPowerLimitsAndOnlyUnlockedOnesGetUndervolt()
    {
        var unlocked = BiosGuides.Build("Gigabyte Technology Co., Ltd.", "Z790 AORUS ELITE", "Intel(R) Core(TM) i7-14700K");
        var locked = BiosGuides.Build("Micro-Star International Co., Ltd.", "B760M", "Intel(R) Core(TM) i5-12400F");

        Assert.Contains(unlocked.Steps, s => s.Kind == GuideStepKind.IntelUndervolt);
        Assert.Contains(locked.Steps, s => s.Kind == GuideStepKind.IntelPowerLimits);
        Assert.DoesNotContain(locked.Steps, s => s.Kind is GuideStepKind.IntelUndervolt or GuideStepKind.CurveOptimizer);
        Assert.Equal("XMP", locked.MemoryProfileName);
    }

    [Fact]
    public void AnAm5BoardUsesExpoAndAnUnknownCpuGetsOnlyTheSafeSteps()
    {
        Assert.Equal("EXPO", BiosGuides.Build("ASRock", "B650M", "AMD Ryzen 5 7600").MemoryProfileName);
        Assert.Equal([GuideStepKind.SaveProfile, GuideStepKind.MemoryProfile, GuideStepKind.TestEachStep], BiosGuides.Build(null, null, null).Steps.Select(s => s.Kind));
    }
}

internal sealed class FakeNvml : INvmlApi
{
    public uint Count { get; set; } = 1;

    public uint Minimum { get; set; } = 100_000;

    public uint Maximum { get; set; } = 250_000;

    public uint Default { get; set; } = 200_000;

    public uint Limit { get; set; } = 200_000;

    public uint MaxClock { get; set; } = 2_805;

    public (uint Min, uint Max)? Locked { get; set; }

    public NvmlReturn PowerAnswer { get; set; } = NvmlReturn.Success;

    public int Inits { get; private set; }

    public int Shutdowns { get; private set; }

    public NvmlReturn Init()
    {
        Inits++;
        return NvmlReturn.Success;
    }

    public void Shutdown() => Shutdowns++;

    public NvmlReturn DeviceCount(out uint count)
    {
        count = Count;
        return NvmlReturn.Success;
    }

    public NvmlReturn Handle(uint index, out IntPtr device)
    {
        device = new IntPtr(index + 1);
        return index < Count ? NvmlReturn.Success : NvmlReturn.InvalidArgument;
    }

    public NvmlReturn Name(IntPtr device, out string name)
    {
        name = "NVIDIA GeForce RTX 4070";
        return NvmlReturn.Success;
    }

    public NvmlReturn PowerConstraints(IntPtr device, out uint minimumMilliwatts, out uint maximumMilliwatts)
    {
        minimumMilliwatts = Minimum;
        maximumMilliwatts = Maximum;
        return NvmlReturn.Success;
    }

    public NvmlReturn PowerLimit(IntPtr device, out uint milliwatts)
    {
        milliwatts = Limit;
        return NvmlReturn.Success;
    }

    public NvmlReturn PowerDefault(IntPtr device, out uint milliwatts)
    {
        milliwatts = Default;
        return NvmlReturn.Success;
    }

    public NvmlReturn SetPowerLimit(IntPtr device, uint milliwatts)
    {
        if (PowerAnswer == NvmlReturn.Success)
        {
            Limit = milliwatts;
        }

        return PowerAnswer;
    }

    public NvmlReturn MaxGraphicsClock(IntPtr device, out uint megahertz)
    {
        megahertz = MaxClock;
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

public sealed class NvidiaTunerTests
{
    private static NvidiaTuner Create(FakeNvml? nvml) => new(() => nvml, NullLogger<NvidiaTuner>.Instance);

    [Fact]
    public void TheCardIsDescribedInWattsAndMegahertz()
    {
        var nvml = new FakeNvml();

        var gpu = Assert.Single(Create(nvml).Read().Gpus);

        Assert.Equal("NVIDIA GeForce RTX 4070", gpu.Name);
        Assert.Equal(new TuningRange(100, 250, 1), gpu.PowerRange);
        Assert.Equal((200, 200), (gpu.PowerCurrent, gpu.PowerDefault));
        Assert.Equal(PowerUnit.Watts, gpu.PowerUnit);
        Assert.Equal(2_805, gpu.ClockRange!.Value.Maximum);
        Assert.True(gpu.AtFactory);
        Assert.Equal(nvml.Inits, nvml.Shutdowns);
    }

    [Fact]
    public void ApplyingSetsThePowerAndLocksTheClockFromTheFloorUpToTheCap()
    {
        var nvml = new FakeNvml();

        var result = Create(nvml).Apply(0, new NvidiaRequest(180, 2_400));

        Assert.Equal(NvidiaApplyStatus.Applied, result.Status);
        Assert.Equal(180_000u, nvml.Limit);
        Assert.Equal((210u, 2_400u), nvml.Locked);
    }

    [Fact]
    public void WithoutACapAnyPreviousClockLockIsRemoved()
    {
        var nvml = new FakeNvml { Locked = (210, 1_500) };

        Create(nvml).Apply(0, new NvidiaRequest(200, null));

        Assert.Null(nvml.Locked);
    }

    [Theory]
    [InlineData(90, null)]
    [InlineData(260, null)]
    [InlineData(200, 250)]
    [InlineData(200, 3_000)]
    public void ValuesOutsideWhatTheCardAllowsAreRefusedBeforeTouchingIt(int watts, int? cap)
    {
        var nvml = new FakeNvml();

        var result = Create(nvml).Apply(0, new NvidiaRequest(watts, cap));

        Assert.Equal(NvidiaApplyStatus.OutOfRange, result.Status);
        Assert.Equal(200_000u, nvml.Limit);
        Assert.Null(nvml.Locked);
    }

    [Fact]
    public void WithoutAdministratorThePowerIsRefusedAndTheResultIsPartial()
    {
        var nvml = new FakeNvml { PowerAnswer = NvmlReturn.NoPermission };

        var result = Create(nvml).Apply(0, new NvidiaRequest(180, 2_400));

        Assert.Equal(NvidiaApplyStatus.Partial, result.Status);
        Assert.Equal(NvmlReturn.NoPermission, result.Power);
    }

    [Fact]
    public void RestoringPutsTheDefaultPowerBackAndUnlocksTheClock()
    {
        var nvml = new FakeNvml { Limit = 150_000, Locked = (210, 1_800) };

        var result = Create(nvml).RestoreDefaults(0);

        Assert.Equal(NvidiaApplyStatus.Applied, result.Status);
        Assert.Equal(200_000u, nvml.Limit);
        Assert.Null(nvml.Locked);
    }

    [Fact]
    public void NoLibraryOrNoDeviceIsReportedPlainly()
    {
        Assert.Equal(GpuReadStatus.LibraryMissing, Create(null).Read().Status);
        Assert.Equal(NvidiaApplyStatus.LibraryMissing, Create(null).Apply(0, new NvidiaRequest(200, null)).Status);
        Assert.Equal(NvidiaApplyStatus.NoDevice, Create(new FakeNvml()).Apply(3, new NvidiaRequest(200, null)).Status);
        Assert.Equal(GpuReadStatus.NoDevice, Create(new FakeNvml { Count = 0 }).Read().Status);
    }

    [Fact]
    public void ThisPcHasNoNvidiaLibrary() =>
        Assert.True(NvmlLibrary.TryOpen(NullLogger.Instance) is null == !File.Exists(Path.Combine(Environment.SystemDirectory, NvmlLibrary.LibraryName)));
}

public sealed class SessionGuardTests : IDisposable
{
    private static readonly DateTimeOffset Applied = new(2026, 10, 26, 22, 0, 0, TimeSpan.Zero);
    private static readonly TuningMarker Marker = new(GpuVendor.Nvidia, 0, "RTX 4070", Applied, 180, 2_400);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "m1vClock-guard-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class StubErrors(HardwareErrorReport report) : IHardwareErrorSource
    {
        public DateTimeOffset? AskedSince { get; private set; }

        public HardwareErrorReport ErrorsSince(DateTimeOffset since)
        {
            AskedSince = since;
            return report;
        }
    }

    private static HardwareErrorReport With(HardwareErrorKind kind) => new([new HardwareErrorEvent(Applied.AddMinutes(5), kind, "x", 1)]);

    [Fact]
    public void WithoutAMarkerThereIsNothingToGuard() =>
        Assert.Equal(GuardDecision.Nothing, GuardRules.Decide(null, true, With(HardwareErrorKind.DriverReset)).Decision);

    [Theory]
    [InlineData(false, null, GuardDecision.KeepApplied, GuardReason.None)]
    [InlineData(true, null, GuardDecision.RestoreDefaults, GuardReason.PreviousSessionCrashed)]
    [InlineData(false, HardwareErrorKind.DriverReset, GuardDecision.RestoreDefaults, GuardReason.DriverReset)]
    [InlineData(false, HardwareErrorKind.UnexpectedShutdown, GuardDecision.RestoreDefaults, GuardReason.UnexpectedShutdown)]
    [InlineData(false, HardwareErrorKind.Whea, GuardDecision.KeepApplied, GuardReason.None)]
    public void ACrashResetOrSuddenShutdownAfterTuningRestoresTheDefaults(bool crashed, HardwareErrorKind? error, GuardDecision decision, GuardReason reason)
    {
        var errors = error is { } kind ? With(kind) : HardwareErrorReport.Empty;

        var check = GuardRules.Decide(Marker, crashed, errors);

        Assert.Equal((decision, reason), (check.Decision, check.Reason));
    }

    [Fact]
    public void AtStartupTheGuardRestoresTheCardAndForgetsTheMarker()
    {
        var nvml = new FakeNvml { Limit = 180_000, Locked = (210, 2_400) };
        var errors = new StubErrors(With(HardwareErrorKind.DriverReset));
        var guard = new SessionGuard(Path.Combine(_folder, "gpu-tuning.json"), errors, new NvidiaTuner(() => nvml, NullLogger<NvidiaTuner>.Instance), NullLogger<SessionGuard>.Instance);
        guard.Record(Marker);

        var check = guard.CheckAtStartup(previousSessionCrashed: false);

        Assert.Equal(GuardDecision.RestoreDefaults, check.Decision);
        Assert.Equal(Applied, errors.AskedSince);
        Assert.Equal(200_000u, nvml.Limit);
        Assert.Null(nvml.Locked);
        Assert.Null(guard.Load());
    }

    [Fact]
    public void AHealthySessionKeepsTheTuningAndTheMarker()
    {
        var nvml = new FakeNvml { Limit = 180_000 };
        var guard = new SessionGuard(Path.Combine(_folder, "gpu-tuning.json"), new StubErrors(HardwareErrorReport.Empty), new NvidiaTuner(() => nvml, NullLogger<NvidiaTuner>.Instance), NullLogger<SessionGuard>.Instance);
        guard.Record(Marker);

        Assert.Equal(GuardDecision.KeepApplied, guard.CheckAtStartup(false).Decision);
        Assert.Equal(180_000u, nvml.Limit);
        Assert.Equal(Marker, guard.Load());
    }

    [Fact]
    public void ABrokenMarkerFileIsTreatedAsNoMarker()
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, "gpu-tuning.json");
        File.WriteAllText(file, "{ quebrado");
        var guard = new SessionGuard(file, new StubErrors(HardwareErrorReport.Empty), new NvidiaTuner(() => null, NullLogger<NvidiaTuner>.Instance), NullLogger<SessionGuard>.Instance);

        Assert.Equal(GuardDecision.Nothing, guard.CheckAtStartup(true).Decision);
    }
}
