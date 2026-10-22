using M1vClock.Core.Privacy;
using M1vClock.Hardware.Ram;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class RamAnalyzerTests
{
    private static RamModule Module(string locator = "DIMM_A2", string bank = "BANK 1", string part = "KF3200C16D4/8GX", int rated = 3200, int configured = 3200, int dataWidth = 64, int totalWidth = 64) =>
        new(bank, locator, 8UL * 1024 * 1024 * 1024, RamType.Ddr4, RamFormFactor.Dimm, rated, configured, "Kingston", part, new SensitiveText("S"), dataWidth, totalWidth, 1, 1.2, 1.2, 1.2);

    [Theory]
    [InlineData("CMK32GX5M2B6000C30", 6000)]
    [InlineData("F5-6000J3038F16GX2", 6000)]
    [InlineData("KF3200C16D4/8GX", 3200)]
    [InlineData("F4-3600C16D-16GVKC", 3600)]
    [InlineData("HX432C16PB3/8", 0)]
    [InlineData("M378A1K43DB2-CTD", 0)]
    [InlineData("ABC32000XYZ", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ReadsTheSpeedWrittenInThePartNumber(string? part, int expected)
    {
        Assert.Equal(expected, RamAnalyzer.SpeedInPartNumber(part));
    }

    [Theory]
    [InlineData("DIMM_A2", "A")]
    [InlineData("DIMM_B2", "B")]
    [InlineData("ChannelA-DIMM0", "A")]
    [InlineData("Controller0-ChannelB-DIMM1", "B")]
    [InlineData("A1", "A")]
    [InlineData("DIMM 0", null)]
    [InlineData("BANK 1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ReadsTheChannelFromTheSlotName(string? locator, string? expected)
    {
        Assert.Equal(expected, RamAnalyzer.ChannelOf(locator));
    }

    [Fact]
    public void SpeedMatchingThePartNumberMeansTheProfileIsProbablyOn()
    {
        var estimate = RamAnalyzer.EstimateProfile([Module(), Module(locator: "DIMM_B2")]);

        Assert.Equal(ProfileVerdict.ProbablyOn, estimate.Verdict);
        Assert.Equal(3200, estimate.ConfiguredSpeed);
        Assert.Equal(3200, estimate.LabelSpeed);
    }

    [Fact]
    public void RunningBelowThePartNumberSpeedMeansTheProfileIsProbablyOff()
    {
        var estimate = RamAnalyzer.EstimateProfile([Module(configured: 2133)]);

        Assert.Equal(ProfileVerdict.ProbablyOff, estimate.Verdict);
        Assert.Equal(2133, estimate.ConfiguredSpeed);
    }

    [Theory]
    [InlineData(3040, ProfileVerdict.ProbablyOn)]
    [InlineData(3039, ProfileVerdict.ProbablyOff)]
    public void TheFivePercentToleranceIsInclusive(int configured, ProfileVerdict expected)
    {
        Assert.Equal(expected, RamAnalyzer.EstimateProfile([Module(configured: configured)]).Verdict);
    }

    [Fact]
    public void WithoutALabelItFallsBackToTheRatedSpeed()
    {
        Assert.Equal(ProfileVerdict.BelowRated, RamAnalyzer.EstimateProfile([Module(part: "X", configured: 2666, rated: 3200)]).Verdict);
        Assert.Equal(ProfileVerdict.CannotEstimate, RamAnalyzer.EstimateProfile([Module(part: "X", configured: 3200, rated: 3200)]).Verdict);
    }

    [Fact]
    public void AnUnreadableConfiguredSpeedIsReportedAsSuch()
    {
        Assert.Equal(ProfileVerdict.SpeedUnreadable, RamAnalyzer.EstimateProfile([Module(configured: 0)]).Verdict);
        Assert.Equal(ProfileVerdict.SpeedUnreadable, RamAnalyzer.EstimateProfile([]).Verdict);
    }

    [Fact]
    public void ThePartNumberOfAnyModuleCounts()
    {
        var estimate = RamAnalyzer.EstimateProfile([Module(part: "SEM-VELOCIDADE", configured: 2400), Module(part: "KF3600C18", configured: 2400)]);

        Assert.Equal(3600, estimate.LabelSpeed);
        Assert.Equal(ProfileVerdict.ProbablyOff, estimate.Verdict);
    }

    [Fact]
    public void TwoModulesInDifferentChannelsAreMultiChannel()
    {
        var estimate = RamAnalyzer.EstimateChannels([Module("DIMM_A2"), Module("DIMM_B2")]);

        Assert.Equal(ChannelVerdict.MultiChannel, estimate.Verdict);
        Assert.Equal(2, estimate.Channels);
        Assert.Equal(["DIMM_A2", "DIMM_B2"], estimate.Locators);
    }

    [Fact]
    public void ASingleModuleIsSingleChannel()
    {
        Assert.Equal(ChannelVerdict.SingleModule, RamAnalyzer.EstimateChannels([Module()]).Verdict);
    }

    [Fact]
    public void TwoModulesOnTheSameChannelAreFlagged()
    {
        Assert.Equal(ChannelVerdict.SameChannel, RamAnalyzer.EstimateChannels([Module("DIMM_A1"), Module("DIMM_A2")]).Verdict);
    }

    [Fact]
    public void SlotNamesThatSayNothingCannotBeInterpreted()
    {
        var estimate = RamAnalyzer.EstimateChannels([Module("DIMM 0", "BANK 0"), Module("DIMM 1", "BANK 1")]);

        Assert.Equal(ChannelVerdict.Unidentified, estimate.Verdict);
        Assert.Equal(0, estimate.Channels);
        Assert.Equal(ChannelVerdict.Unidentified, RamAnalyzer.EstimateChannels([]).Verdict);
    }

    [Fact]
    public void TheBankLabelIsUsedWhenTheLocatorSaysNothing()
    {
        var estimate = RamAnalyzer.EstimateChannels([Module("DIMM 0", "ChannelA"), Module("DIMM 1", "ChannelB")]);

        Assert.Equal(ChannelVerdict.MultiChannel, estimate.Verdict);
    }

    [Fact]
    public void SummaryAddsUpTheInstalledMemory()
    {
        var summary = RamAnalyzer.Summarize([Module(), Module("DIMM_B2")], new RamBoard(4, 128UL * 1024 * 1024 * 1024));

        Assert.Equal(2, summary.Installed);
        Assert.Equal(4, summary.Slots);
        Assert.Equal(16UL * 1024 * 1024 * 1024, summary.TotalBytes);
        Assert.Equal(128UL * 1024 * 1024 * 1024, summary.MaxBytes);
        Assert.Equal([RamType.Ddr4], summary.Types);
        Assert.Equal([1.2], summary.Volts);
        Assert.False(summary.Ecc);
    }

    [Fact]
    public void EccMeansTheTotalWidthIsWiderThanTheDataWidth()
    {
        Assert.True(Module(dataWidth: 64, totalWidth: 72).HasEcc);
        Assert.False(Module(dataWidth: 64, totalWidth: 64).HasEcc);
        Assert.False(Module(dataWidth: 0, totalWidth: 8).HasEcc);
    }

    [Theory]
    [InlineData(20, RamType.Ddr)]
    [InlineData(24, RamType.Ddr3)]
    [InlineData(26, RamType.Ddr4)]
    [InlineData(34, RamType.Ddr5)]
    [InlineData(35, RamType.LpDdr5)]
    [InlineData(0, RamType.Unknown)]
    [InlineData(99, RamType.Unknown)]
    public void MapsTheSmbiosMemoryType(int code, RamType expected)
    {
        Assert.Equal(expected, RamAnalyzer.TypeOf(code));
    }

    [Theory]
    [InlineData(8, RamFormFactor.Dimm)]
    [InlineData(12, RamFormFactor.SoDimm)]
    [InlineData(0, RamFormFactor.Unknown)]
    [InlineData(77, RamFormFactor.Other)]
    public void MapsTheFormFactor(int code, RamFormFactor expected)
    {
        Assert.Equal(expected, RamAnalyzer.FormFactorOf(code));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    public void RanksComeFromTheAttributesField(int attributes, int expected)
    {
        Assert.Equal(expected, RamAnalyzer.RanksOf(attributes));
    }

    [Fact]
    public void VoltsConvertMillivoltsAndIgnoreZero()
    {
        Assert.Equal(1.2, RamAnalyzer.VoltsOf(1200));
        Assert.Null(RamAnalyzer.VoltsOf(0));
        Assert.Null(RamAnalyzer.VoltsOf(null));
    }

    [Fact]
    public void UsageComputesTheUsedShareAndNeverGoesNegative()
    {
        var usage = new RamUsage(16_660_976UL * 1024, 4_165_548UL * 1024, 44_972_528UL * 1024, 26_836_544UL * 1024);

        Assert.Equal(0.75, usage.UsedFraction, 2);
        Assert.Equal((16_660_976UL - 4_165_548UL) * 1024, usage.UsedBytes);
        Assert.Equal(0UL, new RamUsage(10, 20, 0, 5).UsedBytes);
        Assert.Equal(0.0, new RamUsage(0, 0, 0, 0).UsedFraction);
    }
}

public sealed class RamCollectorTests
{
    private static WmiRow Dimm(string locator, string bank) => WmiRow.Of(
        ("BankLabel", bank), ("DeviceLocator", locator), ("Capacity", 8589934592UL), ("SMBIOSMemoryType", 26u), ("FormFactor", 8u),
        ("Speed", 3200u), ("ConfiguredClockSpeed", 3200u), ("Manufacturer", "Kingston"), ("PartNumber", "KF3200C16D4/8GX     "),
        ("SerialNumber", "0A1B2C3D"), ("DataWidth", 64u), ("TotalWidth", 64u), ("Attributes", 1u),
        ("ConfiguredVoltage", 1200u), ("MinVoltage", 1200u), ("MaxVoltage", 1200u));

    private static FakeWmiReader RealMachine() => new FakeWmiReader()
        .With("Win32_PhysicalMemory", Dimm("DIMM_A2", "BANK 1"), Dimm("DIMM_B2", "BANK 3"))
        .With("Win32_PhysicalMemoryArray", WmiRow.Of(("MemoryDevices", 4u), ("MaxCapacity", 134217728u), ("MaxCapacityEx", 134217728UL)))
        .With("Win32_OperatingSystem", WmiRow.Of(("TotalVisibleMemorySize", 16660976UL), ("FreePhysicalMemory", 4165548UL), ("TotalVirtualMemorySize", 44972528UL), ("FreeVirtualMemory", 26836544UL)));

    [Fact]
    public void ReadsTheInventoryOfThisMachine()
    {
        var inventory = new RamCollector(RealMachine()).ReadInventory();

        Assert.Equal(2, inventory.Modules.Count);
        var module = inventory.Modules[0];
        Assert.Equal("DIMM_A2", module.Locator);
        Assert.Equal(8UL * 1024 * 1024 * 1024, module.CapacityBytes);
        Assert.Equal(RamType.Ddr4, module.Type);
        Assert.Equal(RamFormFactor.Dimm, module.FormFactor);
        Assert.Equal("KF3200C16D4/8GX", module.PartNumber);
        Assert.Equal(1.2, module.ConfiguredVolts);
        Assert.Equal(1, module.Ranks);
        Assert.True(module.SerialNumber.HasValue);
        Assert.DoesNotContain("0A1B2C3D", module.ToString(), StringComparison.Ordinal);
        Assert.Equal(4, inventory.Summary.Slots);
        Assert.Equal(128UL * 1024 * 1024 * 1024, inventory.Summary.MaxBytes);
        Assert.Equal(ProfileVerdict.ProbablyOn, inventory.Summary.Profile.Verdict);
        Assert.Equal(ChannelVerdict.MultiChannel, inventory.Summary.Channels.Verdict);
    }

    [Fact]
    public void FallsBackToTheLegacyMaxCapacityWhenTheExtendedOneIsMissing()
    {
        var wmi = RealMachine().With("Win32_PhysicalMemoryArray", WmiRow.Of(("MemoryDevices", 2u), ("MaxCapacity", 33554432u)));

        var inventory = new RamCollector(wmi).ReadInventory();

        Assert.Equal(32UL * 1024 * 1024 * 1024, inventory.Summary.MaxBytes);
    }

    [Fact]
    public void ReadsTheUsageInBytes()
    {
        var usage = new RamCollector(RealMachine()).ReadUsage();

        Assert.Equal(16_660_976UL * 1024, usage.TotalBytes);
        Assert.Equal(4_165_548UL * 1024, usage.FreeBytes);
        Assert.Equal(44_972_528UL * 1024, usage.VirtualTotalBytes);
    }

    [Fact]
    public void NoModulesIsAnError()
    {
        var collector = new RamCollector(new FakeWmiReader());

        Assert.Throws<HardwareReadException>(collector.ReadInventory);
        Assert.Throws<HardwareReadException>(collector.ReadUsage);
    }

    [Fact]
    public async Task OneFailingSectionKeepsTheOther()
    {
        var wmi = RealMachine().Failing("Win32_OperatingSystem", new TimeoutException("Demorou."));
        var service = new RamService(new RamCollector(wmi), NullLogger<RamService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Inventory.IsSuccess);
        Assert.False(report.Usage.IsSuccess);
        Assert.Equal("Demorou.", report.Usage.Error);
    }

    [Fact]
    public async Task ReadsThisMachineForReal()
    {
        var service = new RamService(new RamCollector(new WindowsWmiReader()), NullLogger<RamService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Inventory.TryGet(out var inventory), report.Inventory.Error);
        Assert.NotEmpty(inventory.Modules);
        Assert.True(inventory.Summary.TotalBytes >= 2UL * 1024 * 1024 * 1024);
        Assert.True(report.Usage.TryGet(out var usage), report.Usage.Error);
        Assert.InRange(usage.UsedFraction, 0.01, 1.0);
    }
}
