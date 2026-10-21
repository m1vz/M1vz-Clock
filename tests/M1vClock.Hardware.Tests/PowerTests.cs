using M1vClock.Core.Processes;
using M1vClock.Hardware.Power;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class PowerPlanParserTests
{
    [Fact]
    public void ReadsTheCustomPlanOfThisMachine()
    {
        var plan = PowerPlanParser.ParseActive("GUID do Esquema de Energia: 11111111-1111-1111-1111-111111111111  (Atlas Power Scheme)");

        Assert.NotNull(plan);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), plan.Id);
        Assert.Equal("Atlas Power Scheme", plan.Name);
        Assert.Equal(PowerPlanKind.Custom, plan.Kind);
        Assert.False(plan.IsKnownAndSlow);
    }

    [Theory]
    [InlineData("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)", PowerPlanKind.Balanced, true)]
    [InlineData("GUID do Esquema de Energia: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Alto desempenho)", PowerPlanKind.HighPerformance, false)]
    [InlineData("GUID do Esquema de Energia: a1841308-3541-4fab-bc81-f71556f20b4a  (Economia de energia)", PowerPlanKind.PowerSaver, true)]
    [InlineData("Power Scheme GUID: e9a42b02-d5df-448d-aa00-03f14749eb61  (Ultimate Performance)", PowerPlanKind.Ultimate, false)]
    public void KnownPlansAreRecognizedByGuidInAnyLanguage(string output, PowerPlanKind kind, bool slow)
    {
        var plan = PowerPlanParser.ParseActive(output);

        Assert.Equal(kind, plan!.Kind);
        Assert.Equal(slow, plan.IsKnownAndSlow);
    }

    [Fact]
    public void UppercaseGuidsStillMatch()
    {
        Assert.Equal(PowerPlanKind.Balanced, PowerPlanParser.ParseActive("X: 381B4222-F694-41F0-9685-FF5BB260DF2E (Qualquer nome)")!.Kind);
    }

    [Fact]
    public void ANameWithoutParenthesesFallsBackToTheGuid()
    {
        var plan = PowerPlanParser.ParseActive("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e");

        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", plan!.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Parâmetros inválidos -- tente digitar \"/?\" para obter ajuda")]
    [InlineData("GUID: não-é-guid")]
    public void GarbageIsNotAPlan(string? output)
    {
        Assert.Null(PowerPlanParser.ParseActive(output));
    }
}

public sealed class BatteryEvaluatorTests
{
    private static BatteryInfo Battery(double? design = 50, double? full = 50, int? cycles = null, int? percent = 80) =>
        new(0, "Dell", "Bateria", BatteryChemistry.LithiumIon, BatteryState.Discharging, percent, null, design, full, cycles, null, null, null, null, false, true);

    [Theory]
    [InlineData(50.0, 50.0, 0.0)]
    [InlineData(50.0, 60.0, 0.0)]
    [InlineData(50.0, 25.0, 50.0)]
    [InlineData(40.0, 30.0, 25.0)]
    public void WearIsTheLostShareOfTheDesignCapacity(double design, double full, double expected)
    {
        Assert.Equal(expected, Battery(design, full).WearPercent!.Value, 6);
    }

    [Fact]
    public void WearNeedsBothCapacities()
    {
        Assert.Null(Battery(null, 40).WearPercent);
        Assert.Null(Battery(50, null).WearPercent);
        Assert.Null(Battery(0, 40).WearPercent);
    }

    [Theory]
    [InlineData(46.0, BatteryVerdict.Good, BatteryFindingKind.Excellent)]
    [InlineData(42.5, BatteryVerdict.Good, BatteryFindingKind.Normal)]
    [InlineData(35.0, BatteryVerdict.Warning, BatteryFindingKind.Moderate)]
    [InlineData(25.0, BatteryVerdict.Worn, BatteryFindingKind.WornOut)]
    public void WearThresholdsAreFifteenThirtyAndFiftyPercent(double full, BatteryVerdict verdict, BatteryFindingKind kind)
    {
        var result = BatteryEvaluator.Evaluate(Battery(50, full), onMains: true);

        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(kind, result.Findings[0].Kind);
    }

    [Theory]
    [InlineData(42.5, BatteryVerdict.Good)]
    [InlineData(42.4, BatteryVerdict.Good)]
    [InlineData(35.0, BatteryVerdict.Warning)]
    [InlineData(35.1, BatteryVerdict.Good)]
    [InlineData(25.0, BatteryVerdict.Worn)]
    [InlineData(25.1, BatteryVerdict.Warning)]
    public void ExactBoundariesGoToTheWorseSide(double full, BatteryVerdict expected)
    {
        Assert.Equal(expected, BatteryEvaluator.Evaluate(Battery(50, full), true).Verdict);
    }

    [Fact]
    public void MissingCapacityDataIsUnknown()
    {
        var result = BatteryEvaluator.Evaluate(Battery(null, null), true);

        Assert.Equal(BatteryVerdict.Unknown, result.Verdict);
        Assert.Equal(BatteryFindingKind.NoCapacityData, Assert.Single(result.Findings).Kind);
    }

    [Fact]
    public void ThousandCyclesDowngradeAGoodBatteryToWarning()
    {
        var result = BatteryEvaluator.Evaluate(Battery(cycles: 1000), true);

        Assert.Equal(BatteryVerdict.Warning, result.Verdict);
        Assert.Contains(result.Findings, f => f.Kind == BatteryFindingKind.ManyCycles && f.Value == 1000);
    }

    [Fact]
    public void EightHundredCyclesOnlyAddANote()
    {
        var result = BatteryEvaluator.Evaluate(Battery(cycles: 800), true);

        Assert.Equal(BatteryVerdict.Good, result.Verdict);
        Assert.Contains(result.Findings, f => f.Kind == BatteryFindingKind.ManyCycles);
    }

    [Fact]
    public void CyclesNeverImproveAWornBattery()
    {
        Assert.Equal(BatteryVerdict.Worn, BatteryEvaluator.Evaluate(Battery(50, 20, cycles: 1500), true).Verdict);
    }

    [Theory]
    [InlineData(19, false, true)]
    [InlineData(19, true, false)]
    [InlineData(20, false, false)]
    public void LowChargeOnlyMattersOffTheMains(int percent, bool onMains, bool flagged)
    {
        var result = BatteryEvaluator.Evaluate(Battery(percent: percent), onMains);

        Assert.Equal(flagged, result.Findings.Any(f => f.Kind == BatteryFindingKind.LowCharge));
    }
}

public sealed class PowerCollectorTests
{
    private static FakeWmiReader Laptop() => new FakeWmiReader()
        .With("Win32_Battery", WmiRow.Of(("Name", "DELL 4GVMP"), ("EstimatedChargeRemaining", (ushort)72), ("BatteryStatus", (ushort)1), ("EstimatedRunTime", 190u), ("Chemistry", (ushort)6)))
        .With("BatteryStaticData", WmiRow.Of(("DesignedCapacity", 56000u), ("ManufactureName", "SMP"), ("DeviceName", "DELL 4GVMP")))
        .With("BatteryFullChargedCapacity", WmiRow.Of(("FullChargedCapacity", 41230u)))
        .With("BatteryCycleCount", WmiRow.Of(("CycleCount", 224u)))
        .With("BatteryStatus", WmiRow.Of(("PowerOnline", false), ("Charging", false), ("Discharging", true), ("Voltage", 11400u), ("ChargeRate", 0u), ("DischargeRate", 9500u)));

    private static PowerCollector Collector(FakeWmiReader wmi, CollectingLogger<PowerCollector>? logger = null) => new(wmi, logger ?? new CollectingLogger<PowerCollector>());

    [Fact]
    public void ReadsALaptopBatteryOnTheBattery()
    {
        var set = Collector(Laptop()).ReadBatteries();

        var battery = Assert.Single(set.Batteries);
        Assert.Equal(PowerSource.Battery, set.Source);
        Assert.Equal("SMP", battery.Manufacturer);
        Assert.Equal("DELL 4GVMP", battery.Model);
        Assert.Equal(BatteryChemistry.LithiumIon, battery.Chemistry);
        Assert.Equal(BatteryState.Discharging, battery.State);
        Assert.Equal(72, battery.ChargePercent);
        Assert.Equal(TimeSpan.FromMinutes(190), battery.EstimatedRuntime);
        Assert.Equal(56.0, battery.DesignWattHours);
        Assert.Equal(41.23, battery.FullWattHours);
        Assert.Equal(26.375, battery.WearPercent!.Value, 3);
        Assert.Equal(224, battery.Cycles);
        Assert.Equal(11.4, battery.Volts);
        Assert.Null(battery.ChargeWatts);
        Assert.Equal(9.5, battery.DischargeWatts);
        Assert.True(battery.Discharging);
    }

    [Fact]
    public void PluggedInIsDetectedFromThePowerOnlineFlag()
    {
        var wmi = Laptop().With("BatteryStatus", WmiRow.Of(("PowerOnline", true), ("Charging", true), ("Discharging", false), ("ChargeRate", 30000u)));

        var set = Collector(wmi).ReadBatteries();

        Assert.Equal(PowerSource.Mains, set.Source);
        Assert.Equal(30.0, set.Batteries[0].ChargeWatts);
    }

    [Fact]
    public void WithoutTheLiveClassTheWindowsStatusDecidesIfItIsPluggedIn()
    {
        var wmi = Laptop().With("BatteryStatus").With("Win32_Battery", WmiRow.Of(("EstimatedChargeRemaining", (ushort)100), ("BatteryStatus", (ushort)3)));

        Assert.Equal(PowerSource.Mains, Collector(wmi).ReadBatteries().Source);
    }

    [Fact]
    public void ACycleCountOfZeroMeansTheMakerDoesNotReportIt()
    {
        var wmi = Laptop().With("BatteryCycleCount", WmiRow.Of(("CycleCount", 0u)));

        Assert.Null(Collector(wmi).ReadBatteries().Batteries[0].Cycles);
    }

    [Fact]
    public void TheUnknownRuntimeSentinelIsDropped()
    {
        var wmi = Laptop().With("Win32_Battery", WmiRow.Of(("EstimatedChargeRemaining", (ushort)90), ("BatteryStatus", (ushort)2), ("EstimatedRunTime", 71582788u)));

        Assert.Null(Collector(wmi).ReadBatteries().Batteries[0].EstimatedRuntime);
    }

    [Fact]
    public void ADesktopHasNoBatteries()
    {
        var wmi = new FakeWmiReader().With("Win32_Battery");

        var set = Collector(wmi).ReadBatteries();

        Assert.Empty(set.Batteries);
        Assert.Equal(PowerSource.DesktopMains, set.Source);
    }

    [Fact]
    public void ADeniedClassIsLoggedAndTheRestStillWorks()
    {
        var logger = new CollectingLogger<PowerCollector>();
        var wmi = Laptop().Failing("BatteryCycleCount", new UnauthorizedAccessException("negado"));

        var set = Collector(wmi, logger).ReadBatteries();

        Assert.Null(set.Batteries[0].Cycles);
        Assert.Equal(56.0, set.Batteries[0].DesignWattHours);
        Assert.Equal(3002, Assert.Single(logger.Entries).EventId.Id);
    }

    [Theory]
    [InlineData(6, BatteryChemistry.LithiumIon)]
    [InlineData(8, BatteryChemistry.LithiumPolymer)]
    [InlineData(5, BatteryChemistry.NiMh)]
    [InlineData(0, BatteryChemistry.Unknown)]
    public void ChemistryCodesAreMapped(int code, BatteryChemistry expected)
    {
        Assert.Equal(expected, PowerCollector.ChemistryOf(code));
    }

    [Theory]
    [InlineData(1, BatteryState.Discharging)]
    [InlineData(2, BatteryState.OnAc)]
    [InlineData(3, BatteryState.FullyCharged)]
    [InlineData(4, BatteryState.Low)]
    [InlineData(5, BatteryState.Critical)]
    [InlineData(6, BatteryState.Charging)]
    [InlineData(9, BatteryState.Charging)]
    [InlineData(11, BatteryState.PartlyCharged)]
    [InlineData(10, BatteryState.Unknown)]
    public void StatusCodesAreMapped(int code, BatteryState expected)
    {
        Assert.Equal(expected, PowerCollector.StateOf(code));
    }
}

public sealed class PowerServiceTests
{
    private static PowerService Create(FakeWmiReader wmi, FakeProcessRunner runner, CollectingLogger<PowerService>? logger = null) =>
        new(new PowerCollector(wmi, NullLogger<PowerCollector>.Instance), runner, logger ?? new CollectingLogger<PowerService>());

    [Fact]
    public async Task ADesktopReportsTheMainsAndTheActivePlan()
    {
        var runner = new FakeProcessRunner(_ => new ProcessResult(0, "GUID do Esquema de Energia: 381b4222-f694-41f0-9685-ff5bb260df2e  (Equilibrado)", string.Empty, TimeSpan.Zero, false));

        var report = await Create(new FakeWmiReader().With("Win32_Battery"), runner).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PowerSource.DesktopMains, report.Source);
        Assert.True(report.Batteries.TryGet(out var batteries));
        Assert.Empty(batteries);
        Assert.True(report.Plan.TryGet(out var plan));
        Assert.Equal(PowerPlanKind.Balanced, plan.Kind);
        Assert.Equal("Equilibrado", plan.Name);
    }

    [Fact]
    public async Task PowercfgIsRunWithAnAbsolutePathAndFixedArguments()
    {
        var runner = new FakeProcessRunner(_ => new ProcessResult(0, "X: 381b4222-f694-41f0-9685-ff5bb260df2e (A)", string.Empty, TimeSpan.Zero, false));

        await Create(new FakeWmiReader().With("Win32_Battery"), runner).ReadAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(runner.Requests);
        Assert.True(Path.IsPathFullyQualified(request.FileName));
        Assert.EndsWith("powercfg.exe", request.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["/getactivescheme"], request.Arguments);
        Assert.True(request.Timeout <= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ALaptopOnBatteryProducesAnAssessment()
    {
        var wmi = new FakeWmiReader()
            .With("Win32_Battery", WmiRow.Of(("EstimatedChargeRemaining", (ushort)15), ("BatteryStatus", (ushort)1)))
            .With("BatteryStaticData", WmiRow.Of(("DesignedCapacity", 50000u)))
            .With("BatteryFullChargedCapacity", WmiRow.Of(("FullChargedCapacity", 20000u)));
        var runner = new FakeProcessRunner(_ => new ProcessResult(0, "X: 381b4222-f694-41f0-9685-ff5bb260df2e (A)", string.Empty, TimeSpan.Zero, false));

        var report = await Create(wmi, runner).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PowerSource.Battery, report.Source);
        Assert.True(report.Batteries.TryGet(out var batteries));
        var entry = Assert.Single(batteries);
        Assert.Equal(BatteryVerdict.Worn, entry.Assessment.Verdict);
        Assert.Contains(entry.Assessment.Findings, f => f.Kind == BatteryFindingKind.LowCharge);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task AFailingPowercfgIsReportedButTheBatteriesStillShow(int exitCode)
    {
        var runner = new FakeProcessRunner(_ => new ProcessResult(exitCode, string.Empty, "erro", TimeSpan.Zero, false));

        var report = await Create(new FakeWmiReader().With("Win32_Battery"), runner).ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(report.Plan.IsSuccess);
        Assert.Contains($"{exitCode}", report.Plan.Error, StringComparison.Ordinal);
        Assert.True(report.Batteries.IsSuccess);
    }

    [Fact]
    public async Task ATimedOutPowercfgIsAFailure()
    {
        var runner = new FakeProcessRunner(_ => new ProcessResult(-1, string.Empty, string.Empty, TimeSpan.FromSeconds(15), true));

        var report = await Create(new FakeWmiReader().With("Win32_Battery"), runner).ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(report.Plan.IsSuccess);
    }

    [Fact]
    public async Task APowercfgThatCannotStartIsLogged()
    {
        var logger = new CollectingLogger<PowerService>();
        var runner = new FakeProcessRunner(_ => throw new InvalidOperationException("não iniciou"));

        var report = await Create(new FakeWmiReader().With("Win32_Battery"), runner, logger).ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(report.Plan.IsSuccess);
        Assert.Equal("não iniciou", report.Plan.Error);
        Assert.Equal(3001, Assert.Single(logger.Entries).EventId.Id);
    }

    [Fact]
    public async Task ReadsThisMachineForReal()
    {
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var collector = new PowerCollector(new WindowsWmiReader(), NullLogger<PowerCollector>.Instance);
        var service = new PowerService(collector, runner, NullLogger<PowerService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Batteries.IsSuccess, report.Batteries.Error);
        Assert.True(report.Plan.TryGet(out var plan), report.Plan.Error);
        Assert.False(string.IsNullOrWhiteSpace(plan.Name));
        Assert.NotEqual(Guid.Empty, plan.Id);
    }

    private sealed class FakeProcessRunner(Func<ProcessRequest, ProcessResult> run) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(run(request));
        }
    }
}
