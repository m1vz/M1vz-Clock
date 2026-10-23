using System.Buffers.Binary;
using M1vClock.Core.Results;
using M1vClock.Diagnostics.Crashes;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Diagnostics.Tests;

internal static class RealCrashXml
{
    public const string KernelPowerWithPowerButton =
        "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-Kernel-Power' Guid='{331c3b3a-2005-44c2-ac5e-77220c37d6b4}'/><EventID>41</EventID><Version>10</Version><Level>1</Level><Task>63</Task><Opcode>0</Opcode><Keywords>0x8000400000000002</Keywords><TimeCreated SystemTime='2026-10-19T14:41:24.1754199Z'/><EventRecordID>8342</EventRecordID><Correlation/><Execution ProcessID='4' ThreadID='8'/><Channel>System</Channel><Computer>PC-DE-TESTE</Computer><Security UserID='S-1-5-18'/></System><EventData><Data Name='BugcheckCode'>0</Data><Data Name='BugcheckParameter1'>0x0</Data><Data Name='BugcheckParameter2'>0x0</Data><Data Name='BugcheckParameter3'>0x0</Data><Data Name='BugcheckParameter4'>0x0</Data><Data Name='SleepInProgress'>0</Data><Data Name='PowerButtonTimestamp'>134368549280417617</Data><Data Name='BootAppStatus'>0</Data><Data Name='Checkpoint'>0</Data><Data Name='ConnectedStandbyInProgress'>false</Data><Data Name='SystemSleepTransitionsToOn'>0</Data><Data Name='CsEntryScenarioInstanceId'>0</Data><Data Name='BugcheckInfoFromEFI'>false</Data><Data Name='CheckpointStatus'>0</Data><Data Name='CsEntryScenarioInstanceIdV2'>0</Data><Data Name='LongPowerButtonPressDetected'>false</Data><Data Name='LidReliability'>false</Data><Data Name='InputSuppressionState'>0</Data><Data Name='PowerButtonSuppressionState'>0</Data><Data Name='LidState'>3</Data><Data Name='WHEABootErrorCount'>0</Data></EventData></Event>";

    public const string KernelPowerWithoutAnyCause =
        "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Microsoft-Windows-Kernel-Power' Guid='{331c3b3a-2005-44c2-ac5e-77220c37d6b4}'/><EventID>41</EventID><Version>10</Version><Level>1</Level><Task>63</Task><Opcode>0</Opcode><Keywords>0x8000400000000002</Keywords><TimeCreated SystemTime='2026-10-16T23:07:33.8905451Z'/><EventRecordID>8038</EventRecordID><Correlation/><Execution ProcessID='4' ThreadID='8'/><Channel>System</Channel><Computer>PC-DE-TESTE</Computer><Security UserID='S-1-5-18'/></System><EventData><Data Name='BugcheckCode'>0</Data><Data Name='BugcheckParameter1'>0x0</Data><Data Name='BugcheckParameter2'>0x0</Data><Data Name='BugcheckParameter3'>0x0</Data><Data Name='BugcheckParameter4'>0x0</Data><Data Name='SleepInProgress'>0</Data><Data Name='PowerButtonTimestamp'>0</Data><Data Name='BootAppStatus'>0</Data><Data Name='Checkpoint'>0</Data><Data Name='ConnectedStandbyInProgress'>false</Data><Data Name='SystemSleepTransitionsToOn'>0</Data><Data Name='CsEntryScenarioInstanceId'>0</Data><Data Name='BugcheckInfoFromEFI'>false</Data><Data Name='CheckpointStatus'>0</Data><Data Name='CsEntryScenarioInstanceIdV2'>0</Data><Data Name='LongPowerButtonPressDetected'>false</Data><Data Name='LidReliability'>false</Data><Data Name='InputSuppressionState'>0</Data><Data Name='PowerButtonSuppressionState'>0</Data><Data Name='LidState'>3</Data><Data Name='WHEABootErrorCount'>0</Data></EventData></Event>";

    public const string DwmCrashInTheAmdDriver =
        "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Application Error' Guid='{a0e9b465-b939-57d7-b27d-95d8e925ff57}'/><EventID>1000</EventID><Version>0</Version><Level>2</Level><Task>100</Task><Opcode>0</Opcode><Keywords>0x8000000000000000</Keywords><TimeCreated SystemTime='2026-10-21T12:15:01.4933672Z'/><EventRecordID>20040</EventRecordID><Correlation/><Execution ProcessID='11500' ThreadID='21072'/><Channel>Application</Channel><Computer>PC-DE-TESTE</Computer><Security UserID='S-1-5-90-0-1'/></System><EventData><Data Name='AppName'>dwm.exe</Data><Data Name='AppVersion'>10.0.26100.9549</Data><Data Name='AppTimeStamp'>653fab24</Data><Data Name='ModuleName'>amdxx64.dll</Data><Data Name='ModuleVersion'>32.0.21045.5002</Data><Data Name='ModuleTimeStamp'>6a74d90a</Data><Data Name='ExceptionCode'>c0000005</Data><Data Name='FaultingOffset'>00000000000a2524</Data><Data Name='ProcessId'>0x778</Data><Data Name='ProcessCreationTime'>0x1dd6147d8639482</Data><Data Name='AppPath'>C:\\WINDOWS\\system32\\dwm.exe</Data><Data Name='ModulePath'>C:\\WINDOWS\\System32\\DriverStore\\FileRepository\\u0203303.inf_amd64_e5876a26758f154c\\B026363\\amdxx64.dll</Data><Data Name='IntegratorReportId'>a7b8a83d-a72e-4f79-b6c9-57c6a536aa40</Data><Data Name='PackageFullName'></Data><Data Name='PackageRelativeAppId'></Data></EventData></Event>";
}

public sealed class KernelPowerParserTests
{
    [Fact]
    public void ARealShutdownWithThePowerButtonRecordedIsNotABlueScreen()
    {
        var shutdown = CrashParsers.ParseKernelPower(RealCrashXml.KernelPowerWithPowerButton);

        Assert.NotNull(shutdown);
        Assert.Equal(ShutdownCause.PowerButton, shutdown.Cause);
        Assert.Null(shutdown.BugCheck);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 14, 41, 24, TimeSpan.Zero).AddTicks(1754199), shutdown.Time);
    }

    [Fact]
    public void ARealShutdownWithNoCauseAtAllIsUnknown()
    {
        var shutdown = CrashParsers.ParseKernelPower(RealCrashXml.KernelPowerWithoutAnyCause);

        Assert.NotNull(shutdown);
        Assert.Equal(ShutdownCause.Unknown, shutdown.Cause);
        Assert.Null(shutdown.BugCheck);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 23, 7, 33, TimeSpan.Zero).AddTicks(8905451), shutdown.Time);
    }

    [Fact]
    public void ABugCheckCodeMakesItABlueScreenWithItsParameters()
    {
        var xml = RealCrashXml.KernelPowerWithoutAnyCause
            .Replace("<Data Name='BugcheckCode'>0</Data>", "<Data Name='BugcheckCode'>209</Data>", StringComparison.Ordinal)
            .Replace("<Data Name='BugcheckParameter1'>0x0</Data>", "<Data Name='BugcheckParameter1'>0xffffd0001c2a4000</Data>", StringComparison.Ordinal)
            .Replace("<Data Name='BugcheckParameter2'>0x0</Data>", "<Data Name='BugcheckParameter2'>0x2</Data>", StringComparison.Ordinal);

        var shutdown = CrashParsers.ParseKernelPower(xml);

        Assert.NotNull(shutdown);
        Assert.Equal(ShutdownCause.BlueScreen, shutdown.Cause);
        var bugCheck = Assert.IsType<BugCheckInfo>(shutdown.BugCheck);
        Assert.Equal(0xD1u, bugCheck.Code);
        Assert.Equal("0x000000D1", bugCheck.Hex);
        Assert.Equal("DRIVER_IRQL_NOT_LESS_OR_EQUAL", bugCheck.Name);
        Assert.Equal([0xffffd0001c2a4000UL, 2UL, 0UL, 0UL], bugCheck.Parameters);
    }

    [Fact]
    public void TheBugCheckCodeWinsOverThePowerButton()
    {
        var xml = RealCrashXml.KernelPowerWithPowerButton.Replace("<Data Name='BugcheckCode'>0</Data>", "<Data Name='BugcheckCode'>307</Data>", StringComparison.Ordinal);

        var shutdown = CrashParsers.ParseKernelPower(xml);

        Assert.Equal(ShutdownCause.BlueScreen, shutdown?.Cause);
        Assert.Equal(0x133u, shutdown?.BugCheck?.Code);
    }

    [Fact]
    public void ACodeWrittenInHexIsAlsoUnderstood()
    {
        var xml = RealCrashXml.KernelPowerWithoutAnyCause.Replace("<Data Name='BugcheckCode'>0</Data>", "<Data Name='BugcheckCode'>0x116</Data>", StringComparison.Ordinal);

        Assert.Equal("VIDEO_TDR_FAILURE", CrashParsers.ParseKernelPower(xml)?.BugCheck?.Name);
    }

    [Fact]
    public void ACodeWithNoNameInTheCatalogStillHasItsHexValue()
    {
        var xml = RealCrashXml.KernelPowerWithoutAnyCause.Replace("<Data Name='BugcheckCode'>0</Data>", "<Data Name='BugcheckCode'>4660</Data>", StringComparison.Ordinal);

        var bugCheck = CrashParsers.ParseKernelPower(xml)?.BugCheck;

        Assert.Equal("0x00001234", bugCheck?.Hex);
        Assert.Null(bugCheck?.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("isto não é xml")]
    [InlineData("<Event><System/></Event>")]
    [InlineData("<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System/></Event>")]
    [InlineData("<Outra xmlns='http://schemas.microsoft.com/win/2004/08/events/event'/>")]
    public void AnythingThatIsNotAnEventWithATimeGivesNothing(string xml)
    {
        Assert.Null(CrashParsers.ParseKernelPower(xml));
    }
}

public sealed class AppCrashParserTests
{
    [Fact]
    public void ARealDesktopWindowManagerCrashInTheAmdDriverIsReadInFull()
    {
        var crash = CrashParsers.ParseAppCrash(RealCrashXml.DwmCrashInTheAmdDriver);

        Assert.NotNull(crash);
        Assert.Equal("dwm.exe", crash.App);
        Assert.Equal("amdxx64.dll", crash.Module);
        Assert.Equal(0xC0000005u, crash.ExceptionCode);
        Assert.Equal(@"C:\WINDOWS\system32\dwm.exe", crash.AppPath);
        Assert.StartsWith(@"C:\WINDOWS\System32\DriverStore\FileRepository\", crash.ModulePath, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 12, 15, 1, TimeSpan.Zero).AddTicks(4933672), crash.Time);
    }

    [Fact]
    public void ARealCrashInTheAmdDriverIsBlamedOnTheGraphicsDriver()
    {
        var crash = CrashParsers.ParseAppCrash(RealCrashXml.DwmCrashInTheAmdDriver)!;

        var (origin, vendor) = FaultClassifier.Classify(crash);

        Assert.Equal(FaultOrigin.GraphicsDriver, origin);
        Assert.Equal("AMD", vendor);
    }

    [Fact]
    public void WithoutAModuleTheProgramItselfIsTheFault()
    {
        var xml = RealCrashXml.DwmCrashInTheAmdDriver.Replace("<Data Name='ModuleName'>amdxx64.dll</Data>", "<Data Name='ModuleName'></Data>", StringComparison.Ordinal);

        var crash = CrashParsers.ParseAppCrash(xml);

        Assert.Equal("dwm.exe", crash?.Module);
    }

    [Fact]
    public void ACrashWithoutAProgramNameIsDiscarded()
    {
        var xml = RealCrashXml.DwmCrashInTheAmdDriver.Replace("<Data Name='AppName'>dwm.exe</Data>", "<Data Name='AppName'></Data>", StringComparison.Ordinal);

        Assert.Null(CrashParsers.ParseAppCrash(xml));
    }

    [Fact]
    public void AnUnreadableExceptionCodeBecomesZeroInsteadOfBreakingTheRead()
    {
        var xml = RealCrashXml.DwmCrashInTheAmdDriver.Replace("<Data Name='ExceptionCode'>c0000005</Data>", "<Data Name='ExceptionCode'>zzz</Data>", StringComparison.Ordinal);

        var crash = CrashParsers.ParseAppCrash(xml);

        Assert.Equal(0u, crash?.ExceptionCode);
        Assert.Null(ExceptionCatalog.NameOf(0));
    }
}

public sealed class FaultClassifierTests
{
    private static AppCrash Crash(string app, string module, string? modulePath = null, string? appPath = null) =>
        new(DateTimeOffset.UnixEpoch, app, module, modulePath, 0xC0000005, appPath);

    [Theory]
    [InlineData("amdxx64.dll", "AMD")]
    [InlineData("atidxx64.dll", "AMD")]
    [InlineData("amdvlk64.dll", "AMD")]
    [InlineData("nvwgf2umx.dll", "NVIDIA")]
    [InlineData("nvd3dumx.dll", "NVIDIA")]
    [InlineData("nvlddmkm.sys", "NVIDIA")]
    [InlineData("igd10iumd64.dll", "Intel")]
    [InlineData("ig9icd64.dll", "Intel")]
    public void TheUserModeGraphicsDriversOfTheThreeVendorsAreRecognised(string module, string vendor)
    {
        Assert.Equal((FaultOrigin.GraphicsDriver, vendor), FaultClassifier.Classify(Crash("jogo.exe", module)));
    }

    [Fact]
    public void ACrashInTheProgramsOwnCodeIsTheApplicationItself()
    {
        Assert.Equal(FaultOrigin.Application, FaultClassifier.Classify(Crash("jogo.exe", "JOGO.EXE")).Origin);
        Assert.Equal(FaultOrigin.Application, FaultClassifier.Classify(Crash("jogo.exe", "outro.exe", @"D:\Jogos\jogo.exe", @"D:\Jogos\jogo.exe")).Origin);
    }

    [Fact]
    public void ACrashInAWindowsLibraryIsBlamedOnWindows()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.Equal(FaultOrigin.Windows, FaultClassifier.Classify(Crash("jogo.exe", "ntdll.dll", Path.Combine(windows, "System32", "ntdll.dll"))).Origin);
    }

    [Fact]
    public void ADriverStoreLibraryThatIsNotGraphicsIsAThirdPartyModule()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Path.Combine(windows, "System32", "DriverStore", "FileRepository", "realtek.inf", "rtkapi64.dll");

        Assert.Equal(FaultOrigin.ThirdPartyModule, FaultClassifier.Classify(Crash("jogo.exe", "rtkapi64.dll", path)).Origin);
    }

    [Fact]
    public void ALibraryOutsideWindowsIsAThirdPartyModule()
    {
        Assert.Equal(FaultOrigin.ThirdPartyModule, FaultClassifier.Classify(Crash("jogo.exe", "mod.dll", @"D:\Jogos\mod.dll")).Origin);
        Assert.Equal(FaultOrigin.ThirdPartyModule, FaultClassifier.Classify(Crash("jogo.exe", "mod.dll")).Origin);
    }
}

public sealed class DumpHeaderReaderTests
{
    private static byte[] Header(uint code, ulong p1, ulong p2, ulong p3, ulong p4, string valid = "DU64")
    {
        var bytes = new byte[0x2000];
        "PAGE"u8.CopyTo(bytes);
        System.Text.Encoding.ASCII.GetBytes(valid).CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x38), code);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x40), p1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x48), p2);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x50), p3);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x58), p4);
        return bytes;
    }

    [Fact]
    public void AKernelDumpHeaderGivesTheBugCheckCodeAndItsFourParameters()
    {
        using var stream = new MemoryStream(Header(0x133, 0x1, 0x1e00, 0xfffff80012345678, 0));

        var info = DumpHeaderReader.Read(stream);

        Assert.NotNull(info);
        Assert.Equal(0x133u, info.Code);
        Assert.Equal("DPC_WATCHDOG_VIOLATION", info.Name);
        Assert.Equal([0x1UL, 0x1e00UL, 0xfffff80012345678UL, 0UL], info.Parameters);
    }

    [Fact]
    public void AHeaderThatIsExactlyTheMinimumSizeIsStillRead()
    {
        using var stream = new MemoryStream(Header(0xA, 1, 2, 3, 4).AsSpan(0, DumpHeaderReader.HeaderLength).ToArray());

        Assert.Equal(0xAu, DumpHeaderReader.Read(stream)?.Code);
    }

    [Fact]
    public void AFileShorterThanTheHeaderOrWithAnotherSignatureGivesNothing()
    {
        using var shortFile = new MemoryStream(Header(0xA, 1, 2, 3, 4).AsSpan(0, DumpHeaderReader.HeaderLength - 1).ToArray());
        using var thirtyTwoBit = new MemoryStream(Header(0xA, 1, 2, 3, 4, "DUMP"));
        using var userDump = new MemoryStream([.. "MDMP"u8, .. new byte[0x100]]);
        using var empty = new MemoryStream();

        Assert.Null(DumpHeaderReader.Read(shortFile));
        Assert.Null(DumpHeaderReader.Read(thirtyTwoBit));
        Assert.Null(DumpHeaderReader.Read(userDump));
        Assert.Null(DumpHeaderReader.Read(empty));
    }

    [Fact]
    public void ARealProgramDumpFromThisPcIsNotMistakenForABlueScreen()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.dmp"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Assert.Null(DumpHeaderReader.Read(stream));
        }
    }
}

public sealed class CatalogTests
{
    [Fact]
    public void EveryNameIsAnUppercaseIdentifierAndNoNameRepeats()
    {
        Assert.All(BugCheckCatalog.AllNames.Concat(ExceptionCatalog.AllNames), name => Assert.Matches("^[A-Z][A-Z0-9_]+$", name));
        Assert.Equal(BugCheckCatalog.AllNames.Count, BugCheckCatalog.AllNames.Distinct().Count());
        Assert.Equal(ExceptionCatalog.AllNames.Count, ExceptionCatalog.AllNames.Distinct().Count());
    }

    [Theory]
    [InlineData(0x0Au, "IRQL_NOT_LESS_OR_EQUAL")]
    [InlineData(0x3Bu, "SYSTEM_SERVICE_EXCEPTION")]
    [InlineData(0x124u, "WHEA_UNCORRECTABLE_ERROR")]
    [InlineData(0x1CAu, "SYNTHETIC_WATCHDOG_TIMEOUT")]
    public void TheCommonBlueScreenCodesHaveTheirOfficialNames(uint code, string name)
    {
        Assert.Equal(name, BugCheckCatalog.NameOf(code));
    }

    [Theory]
    [InlineData(0xC0000005u, "ACCESS_VIOLATION")]
    [InlineData(0xC0000409u, "STACK_BUFFER_OVERRUN")]
    [InlineData(0xE0434352u, "CLR_EXCEPTION")]
    public void TheCommonExceptionCodesHaveTheirNames(uint code, string name)
    {
        Assert.Equal(name, ExceptionCatalog.NameOf(code));
    }

    [Fact]
    public void AnUnknownCodeHasNoName()
    {
        Assert.Null(BugCheckCatalog.NameOf(0xDEAD));
        Assert.Null(ExceptionCatalog.NameOf(0x12345678));
    }
}

public sealed class FakeCrashSource : ICrashSource
{
    public ReadResult<DumpSettings> Settings { get; set; } = ReadResult.Success(new DumpSettings(DumpMode.Disabled, @"C:\WINDOWS\Minidump", @"C:\WINDOWS\MEMORY.DMP", false, false));

    public Dictionary<int, ReadResult<IReadOnlyList<string>>> Events { get; } = [];

    public Dictionary<DumpFolder, ReadResult<IReadOnlyList<DumpFileInfo>>> Dumps { get; } = [];

    public List<(string Log, int Id, string Provider, TimeSpan Window)> Requests { get; } = [];

    public ReadResult<DumpSettings> ReadSettings() => Settings;

    public ReadResult<IReadOnlyList<string>> ReadEvents(string log, int id, string provider, TimeSpan window, int limit, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add((log, id, provider, window));
        }

        return Events.GetValueOrDefault(id, ReadResult.Success<IReadOnlyList<string>>([]));
    }

    public ReadResult<IReadOnlyList<DumpFileInfo>> ReadDumps(DumpFolder folder) =>
        Dumps.GetValueOrDefault(folder, ReadResult.Success<IReadOnlyList<DumpFileInfo>>([]));
}

public sealed class CrashServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    private static ReadResult<IReadOnlyList<string>> Xmls(params string[] xmls) => ReadResult.Success<IReadOnlyList<string>>(xmls);

    private static AppCrash Crash(string app, string module, int minutesAgo, uint code = 0xC0000005) =>
        new(Now.AddMinutes(-minutesAgo), app, module, null, code, null);

    [Fact]
    public async Task TheRealEventsOfThisPcBecomeTwoShutdownsAndOneGroupedCrash()
    {
        var source = new FakeCrashSource();
        source.Events[41] = Xmls(RealCrashXml.KernelPowerWithoutAnyCause, RealCrashXml.KernelPowerWithPowerButton);
        source.Events[1000] = Xmls(RealCrashXml.DwmCrashInTheAmdDriver);

        var report = await new CrashService(source, NullLogger<CrashService>.Instance).ReadAsync(TimeSpan.FromDays(30), CancellationToken.None);

        Assert.Equal([ShutdownCause.PowerButton, ShutdownCause.Unknown], report.Shutdowns.Select(s => s.Cause));
        Assert.Equal(0, report.BlueScreens);
        var crash = Assert.Single(report.AppCrashes);
        Assert.Equal(("dwm.exe", "amdxx64.dll", FaultOrigin.GraphicsDriver, "AMD"), (crash.App, crash.Module, crash.Origin, crash.Vendor));
        Assert.Equal("0xC0000005", crash.ExceptionHex);
        Assert.Equal("ACCESS_VIOLATION", crash.ExceptionName);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public async Task EachKindOfEventIsRequestedFromItsOwnLogForTheWholeWindow()
    {
        var source = new FakeCrashSource();

        await new CrashService(source, NullLogger<CrashService>.Instance).ReadAsync(TimeSpan.FromDays(14), CancellationToken.None);

        Assert.Equal(2, source.Requests.Count);
        Assert.Contains(("System", 41, "Microsoft-Windows-Kernel-Power", TimeSpan.FromDays(14)), source.Requests);
        Assert.Contains(("Application", 1000, "Application Error", TimeSpan.FromDays(14)), source.Requests);
    }

    [Fact]
    public void RepeatedCrashesOfTheSameProgramModuleAndExceptionBecomeOneGroupWithACount()
    {
        var group = CrashService.Group(
        [
            Crash("jogo.exe", "amdxx64.dll", 5),
            Crash("JOGO.EXE", "AMDXX64.DLL", 60),
            Crash("jogo.exe", "amdxx64.dll", 600),
            Crash("jogo.exe", "amdxx64.dll", 10, 0xC0000409),
            Crash("outro.exe", "x.dll", 1),
        ]);
        Assert.Equal(3, group.Count);
        Assert.Equal(("outro.exe", 1), (group[0].App, group[0].Count));
        var main = group.Single(g => g.App == "jogo.exe" && g.ExceptionCode == 0xC0000005);
        Assert.Equal(3, main.Count);
        Assert.Equal(Now.AddMinutes(-5), main.Last);
        Assert.Equal(Now.AddMinutes(-600), main.First);
    }

    [Fact]
    public async Task AFailedPartIsReportedWhileTheOthersStillCome()
    {
        var source = new FakeCrashSource();
        source.Events[41] = ReadResult.Failure<IReadOnlyList<string>>("Sem permissão para ler o log System.");
        source.Events[1000] = Xmls(RealCrashXml.DwmCrashInTheAmdDriver);
        source.Settings = ReadResult.Failure<DumpSettings>("registro bloqueado");
        source.Dumps[DumpFolder.Kernel] = ReadResult.Failure<IReadOnlyList<DumpFileInfo>>("sem acesso");

        var report = await new CrashService(source, NullLogger<CrashService>.Instance).ReadAsync(TimeSpan.FromDays(7), CancellationToken.None);

        Assert.Single(report.AppCrashes);
        Assert.Empty(report.Shutdowns);
        Assert.False(report.Settings.IsSuccess);
        Assert.Equal(["kernelDumps", "shutdowns"], report.Failures.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Sem permissão para ler o log System.", report.Failures[CrashService.ShutdownsPart]);
    }

    [Fact]
    public async Task EventsThatCannotBeParsedAreDroppedInsteadOfBreakingTheReport()
    {
        var source = new FakeCrashSource();
        source.Events[41] = Xmls("lixo", RealCrashXml.KernelPowerWithPowerButton, string.Empty);

        var report = await new CrashService(source, NullLogger<CrashService>.Instance).ReadAsync(TimeSpan.FromDays(7), CancellationToken.None);

        Assert.Single(report.Shutdowns);
    }

    [Fact]
    public async Task TheDumpFilesComeNewestFirstAndTheBlueScreensAreCounted()
    {
        var source = new FakeCrashSource();
        var shutdownXml = RealCrashXml.KernelPowerWithoutAnyCause.Replace("<Data Name='BugcheckCode'>0</Data>", "<Data Name='BugcheckCode'>209</Data>", StringComparison.Ordinal);
        source.Events[41] = Xmls(shutdownXml, RealCrashXml.KernelPowerWithPowerButton);
        source.Dumps[DumpFolder.Kernel] = ReadResult.Success<IReadOnlyList<DumpFileInfo>>(
        [
            new DumpFileInfo(@"C:\WINDOWS\Minidump\a.dmp", "a.dmp", 1000, Now.AddDays(-3), new BugCheckInfo(0xD1, [1, 2, 3, 4])),
            new DumpFileInfo(@"C:\WINDOWS\Minidump\b.dmp", "b.dmp", 2000, Now.AddDays(-1), null),
        ]);

        var report = await new CrashService(source, NullLogger<CrashService>.Instance).ReadAsync(TimeSpan.FromDays(30), CancellationToken.None);

        Assert.Equal(1, report.BlueScreens);
        Assert.Equal(["b.dmp", "a.dmp"], report.KernelDumps.Select(d => d.Name));
        Assert.Empty(report.UserDumps);
        Assert.Equal(0, report.AppCrashCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(91)]
    public async Task AWindowOutsideZeroToNinetyDaysIsRefused(int days)
    {
        var window = days == 0 ? TimeSpan.Zero : TimeSpan.FromDays(days);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new CrashService(new FakeCrashSource(), NullLogger<CrashService>.Instance).ReadAsync(window, CancellationToken.None));
    }
}

internal sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Messages.Add(formatter(state, exception));
    }
}

public sealed class ParserLoggingTests
{
    [Fact]
    public void AnEventWhoseXmlCannotBeReadIsLoggedOnceAndSkipped()
    {
        var logger = new ListLogger();

        Assert.Null(CrashParsers.ParseKernelPower("<Event", logger));
        Assert.Null(CrashParsers.ParseAppCrash("isto não é xml", logger));

        Assert.Equal(2, logger.Messages.Count);
        Assert.All(logger.Messages, m => Assert.Contains("XML", m, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidXmlAndEmptyTextAreNeverLogged()
    {
        var logger = new ListLogger();

        Assert.NotNull(CrashParsers.ParseKernelPower(RealCrashXml.KernelPowerWithPowerButton, logger));
        Assert.Null(CrashParsers.ParseAppCrash(string.Empty, logger));

        Assert.Empty(logger.Messages);
    }
}
