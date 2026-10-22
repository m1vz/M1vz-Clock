using System.Buffers.Binary;
using M1vClock.Core.RegistryValues;
using M1vClock.Diagnostics.Crashes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace M1vClock.Diagnostics.Tests;

internal sealed class FakeRegistry : IRegistryAccess
{
    private readonly Dictionary<string, RegistryValueState> _values = new(StringComparer.Ordinal);

    public FakeRegistry With(RegistryValueAddress address, RegistryValueState state)
    {
        _values[address.Target] = state;
        return this;
    }

    public RegistryValueState Read(RegistryValueAddress address) =>
        _values.TryGetValue(address.Target, out var state) ? state : RegistryValueState.Absent(keyExists: true);

    public void Write(RegistryValueAddress address, RegistryValueState state) => throw new NotSupportedException("A leitura de dumps nunca grava no registro.");
}

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "m1vclock-diag-tests" + System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(string name)
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

public sealed class DumpSettingsReaderTests
{
    private static readonly CrashPaths Paths = new(@"C:\WINDOWS", @"C:\Users\x\AppData\Local");

    [Fact]
    public void TheSettingsOfThisPcAreReadAsDumpsTurnedOffWithTheDefaultPaths()
    {
        var registry = new FakeRegistry()
            .With(DumpSettingsReader.Mode, RegistryValueState.DWord(0))
            .With(DumpSettingsReader.Directory, new RegistryValueState(true, true, RegistryValueKind.ExpandString, @"%SystemRoot%\Minidump"))
            .With(DumpSettingsReader.File, new RegistryValueState(true, true, RegistryValueKind.ExpandString, @"%SystemRoot%\MEMORY.DMP"))
            .With(DumpSettingsReader.AutoReboot, RegistryValueState.DWord(0))
            .With(DumpSettingsReader.LogEvent, RegistryValueState.DWord(0));

        var settings = DumpSettingsReader.Read(registry, Paths);

        var windows = Environment.GetEnvironmentVariable("SystemRoot")!;
        Assert.Equal(DumpMode.Disabled, settings.Mode);
        Assert.False(settings.Collects);
        Assert.Equal(Path.Combine(windows, "Minidump"), settings.MinidumpDirectory, ignoreCase: true);
        Assert.Equal(Path.Combine(windows, "MEMORY.DMP"), settings.DumpFile, ignoreCase: true);
        Assert.False(settings.AutoReboot);
        Assert.False(settings.LogsEvent);
    }

    [Theory]
    [InlineData(0, DumpMode.Disabled, false)]
    [InlineData(1, DumpMode.Complete, true)]
    [InlineData(2, DumpMode.Kernel, true)]
    [InlineData(3, DumpMode.Small, true)]
    [InlineData(7, DumpMode.Automatic, true)]
    [InlineData(5, DumpMode.Unknown, false)]
    [InlineData(-1, DumpMode.Unknown, false)]
    public void EveryModeTheRegistryCanHoldHasAMeaningAndSaysWhetherDumpsAreKept(int value, DumpMode mode, bool collects)
    {
        var registry = new FakeRegistry().With(DumpSettingsReader.Mode, RegistryValueState.DWord(value));

        var settings = DumpSettingsReader.Read(registry, Paths);

        Assert.Equal(mode, settings.Mode);
        Assert.Equal(collects, settings.Collects);
    }

    [Fact]
    public void ValuesThatAreMissingFallBackToUnknownAndTheStandardFolders()
    {
        var settings = DumpSettingsReader.Read(new FakeRegistry(), Paths);

        Assert.Equal(DumpMode.Unknown, settings.Mode);
        Assert.Equal(@"C:\WINDOWS\Minidump", settings.MinidumpDirectory);
        Assert.Equal(@"C:\WINDOWS\MEMORY.DMP", settings.DumpFile);
        Assert.False(settings.AutoReboot);
    }

    [Fact]
    public void AValueOfTheWrongTypeIsTreatedAsMissingInsteadOfBreakingTheRead()
    {
        var registry = new FakeRegistry()
            .With(DumpSettingsReader.Mode, RegistryValueState.Text("0"))
            .With(DumpSettingsReader.Directory, RegistryValueState.DWord(7))
            .With(DumpSettingsReader.LogEvent, RegistryValueState.Text("1"));

        var settings = DumpSettingsReader.Read(registry, Paths);

        Assert.Equal(DumpMode.Unknown, settings.Mode);
        Assert.Equal(@"C:\WINDOWS\Minidump", settings.MinidumpDirectory);
        Assert.False(settings.LogsEvent);
    }

    [Fact]
    public void TurningOnAutoRebootAndEventLoggingIsReadBack()
    {
        var registry = new FakeRegistry()
            .With(DumpSettingsReader.Mode, RegistryValueState.DWord(7))
            .With(DumpSettingsReader.AutoReboot, RegistryValueState.DWord(1))
            .With(DumpSettingsReader.LogEvent, RegistryValueState.DWord(1));

        var settings = DumpSettingsReader.Read(registry, Paths);

        Assert.True(settings.AutoReboot);
        Assert.True(settings.LogsEvent);
    }
}

public sealed class WindowsCrashSourceFolderTests
{
    private static byte[] KernelDump(uint code)
    {
        var bytes = new byte[0x1000];
        "PAGE"u8.CopyTo(bytes);
        "DU64"u8.CopyTo(bytes.AsSpan(4));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x38), code);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x40), 0x7);
        return bytes;
    }

    private static WindowsCrashSource Create(FakeRegistry registry, CrashPaths paths) => new(registry, paths, NullLogger<WindowsCrashSource>.Instance);

    private static FakeRegistry Pointing(string directory, string dumpFile) => new FakeRegistry()
        .With(DumpSettingsReader.Mode, RegistryValueState.DWord(3))
        .With(DumpSettingsReader.Directory, RegistryValueState.Text(directory))
        .With(DumpSettingsReader.File, RegistryValueState.Text(dumpFile));

    [Fact]
    public void KernelDumpsAreListedWithTheirBugCheckAndOtherFilesAreIgnored()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("Minidump");
        File.WriteAllBytes(Path.Combine(folder, "102226-1234-01.dmp"), KernelDump(0x133));
        File.WriteAllBytes(Path.Combine(folder, "102026-9999-01.dmp"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(folder, "notas.txt"), "x");
        Directory.CreateDirectory(Path.Combine(folder, "pasta.dmp"));
        var source = Create(Pointing(folder, Path.Combine(temp.Path, "nao-existe.dmp")), new CrashPaths(temp.Path, temp.Path));

        var result = source.ReadDumps(DumpFolder.Kernel);

        Assert.True(result.TryGet(out var files), result.Error);
        Assert.Equal(["102026-9999-01.dmp", "102226-1234-01.dmp"], files.Select(f => f.Name).Order(StringComparer.Ordinal));
        var good = files.Single(f => f.Name == "102226-1234-01.dmp");
        Assert.Equal("DPC_WATCHDOG_VIOLATION", good.BugCheck?.Name);
        Assert.Equal(0x1000, good.Length);
        Assert.Null(files.Single(f => f.Name == "102026-9999-01.dmp").BugCheck);
    }

    [Fact]
    public void TheCompleteMemoryDumpIsAddedWhenItExistsOutsideTheFolder()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("Minidump");
        var full = Path.Combine(temp.Path, "MEMORY.DMP");
        File.WriteAllBytes(full, KernelDump(0x124));
        var source = Create(Pointing(folder, full), new CrashPaths(temp.Path, temp.Path));

        var result = source.ReadDumps(DumpFolder.Kernel);

        Assert.True(result.TryGet(out var files), result.Error);
        var dump = Assert.Single(files);
        Assert.Equal("MEMORY.DMP", dump.Name);
        Assert.Equal("WHEA_UNCORRECTABLE_ERROR", dump.BugCheck?.Name);
    }

    [Fact]
    public void ADumpFileFoundTwiceIsListedOnce()
    {
        using var temp = new TempFolder();
        var full = Path.Combine(temp.Path, "MEMORY.DMP");
        File.WriteAllBytes(full, KernelDump(0x1A));
        var source = Create(Pointing(temp.Path, full), new CrashPaths(temp.Path, temp.Path));

        var result = source.ReadDumps(DumpFolder.Kernel);

        Assert.True(result.TryGet(out var files), result.Error);
        Assert.Single(files);
    }

    [Fact]
    public void AFolderThatDoesNotExistGivesAnEmptyListNotAFailure()
    {
        using var temp = new TempFolder();
        var source = Create(Pointing(Path.Combine(temp.Path, "nada"), Path.Combine(temp.Path, "nada.dmp")), new CrashPaths(temp.Path, temp.Path));

        Assert.True(source.ReadDumps(DumpFolder.Kernel).TryGet(out var kernel));
        Assert.Empty(kernel);
        Assert.True(source.ReadDumps(DumpFolder.User).TryGet(out var user));
        Assert.Empty(user);
    }

    [Fact]
    public void ProgramDumpsAreListedWithoutBeingOpened()
    {
        using var temp = new TempFolder();
        var local = temp.Sub("Local");
        var crashDumps = Path.Combine(local, "CrashDumps");
        Directory.CreateDirectory(crashDumps);
        File.WriteAllBytes(Path.Combine(crashDumps, "jogo.exe.123.dmp"), KernelDump(0x133));
        var source = Create(new FakeRegistry(), new CrashPaths(temp.Path, local));

        var result = source.ReadDumps(DumpFolder.User);

        Assert.True(result.TryGet(out var files), result.Error);
        var dump = Assert.Single(files);
        Assert.Equal("jogo.exe.123.dmp", dump.Name);
        Assert.Null(dump.BugCheck);
        Assert.Equal(0x1000, dump.Length);
    }

    [Fact]
    public void AnOpenDumpFileCanStillBeListed()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("Minidump");
        var path = Path.Combine(folder, "a.dmp");
        File.WriteAllBytes(path, KernelDump(0xA));
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var source = Create(Pointing(folder, Path.Combine(temp.Path, "x.dmp")), new CrashPaths(temp.Path, temp.Path));

        var result = source.ReadDumps(DumpFolder.Kernel);

        Assert.True(result.TryGet(out var files), result.Error);
        Assert.Equal("a.dmp", Assert.Single(files).Name);
    }

    [Fact]
    public void ReadingTheSettingsNeverWritesToTheRegistry()
    {
        var source = Create(new FakeRegistry(), CrashPaths.Current);

        Assert.True(source.ReadSettings().IsSuccess);
    }

    [Fact]
    public void ASourceNameWithAnApostropheIsRefusedBeforeItCanBreakTheQuery()
    {
        var source = Create(new FakeRegistry(), CrashPaths.Current);

        Assert.Throws<ArgumentException>(() => source.ReadEvents("System", 41, "x' or 1=1 or 'y", TimeSpan.FromDays(1), 5, CancellationToken.None));
    }
}

public sealed class RealMachineCrashTests
{
    private static WindowsCrashSource Create() => new(new WindowsRegistryAccess(), CrashPaths.Current, NullLogger<WindowsCrashSource>.Instance);

    [Fact]
    public void TheDumpSettingsOfThisPcAreReadFromTheRealRegistry()
    {
        var result = Create().ReadSettings();

        Assert.True(result.TryGet(out var settings), result.Error);
        Assert.False(string.IsNullOrWhiteSpace(settings.MinidumpDirectory));
        Assert.False(string.IsNullOrWhiteSpace(settings.DumpFile));
        Assert.Equal(settings.Mode is not (DumpMode.Disabled or DumpMode.Unknown), settings.Collects);
    }

    [Fact]
    public void EveryKernelPowerEventOfThisPcParsesAndTheTimesFallInsideTheWindow()
    {
        var window = TimeSpan.FromDays(90);

        var result = Create().ReadEvents("System", 41, "Microsoft-Windows-Kernel-Power", window, 500, CancellationToken.None);

        Assert.True(result.TryGet(out var xmls), result.Error);
        var cutoff = DateTimeOffset.UtcNow - window - TimeSpan.FromMinutes(1);
        foreach (var xml in xmls)
        {
            var shutdown = CrashParsers.ParseKernelPower(xml);
            Assert.NotNull(shutdown);
            Assert.True(shutdown.Time >= cutoff);
            Assert.Equal(shutdown.BugCheck is not null, shutdown.Cause == ShutdownCause.BlueScreen);
        }
    }

    [Fact]
    public void EveryApplicationErrorOfThisPcParsesIntoACrashWithAProgramName()
    {
        var result = Create().ReadEvents("Application", 1000, "Application Error", TimeSpan.FromDays(90), 500, CancellationToken.None);

        Assert.True(result.TryGet(out var xmls), result.Error);
        foreach (var xml in xmls)
        {
            var crash = CrashParsers.ParseAppCrash(xml);
            Assert.NotNull(crash);
            Assert.False(string.IsNullOrWhiteSpace(crash.App));
            Assert.False(string.IsNullOrWhiteSpace(crash.Module));
        }
    }

    [Fact]
    public async Task TheWholeServiceRunsAgainstThisPcAndTheCountsAddUp()
    {
        var service = new CrashService(Create());

        var report = await service.ReadAsync(TimeSpan.FromDays(60), CancellationToken.None);

        Assert.True(report.Settings.IsSuccess);
        Assert.All(report.AppCrashes, g => Assert.True(g.Count >= 1 && g.First <= g.Last));
        Assert.True(report.BlueScreens <= report.Shutdowns.Count);
        Assert.All(report.UserDumps, d => Assert.True(d.Length > 0 && d.BugCheck is null));
    }

    [Fact]
    public void ALogThatDoesNotExistIsAFailureNotACrash()
    {
        var result = Create().ReadEvents("M1vClock-Log-Que-Nao-Existe", 41, "Algo", TimeSpan.FromDays(1), 5, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
