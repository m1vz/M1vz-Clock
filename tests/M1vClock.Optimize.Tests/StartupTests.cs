using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.RestorePoints;
using M1vClock.Core.Results;
using M1vClock.Optimize.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32;

namespace M1vClock.Optimize.Tests;

internal sealed class MemoryRegistry : IRegistryAccess
{
    private readonly Dictionary<string, RegistryValueState> _values = new(StringComparer.Ordinal);

    public Dictionary<string, RegistryValueState> Values => _values;

    public RegistryValueState Read(RegistryValueAddress address) =>
        _values.TryGetValue(address.Target, out var state) ? state : RegistryValueState.Absent(keyExists: true);

    public void Write(RegistryValueAddress address, RegistryValueState state)
    {
        if (state.ValueExists)
        {
            _values[address.Target] = state;
        }
        else
        {
            _values.Remove(address.Target);
        }
    }
}

internal sealed class CountingGate : IRestorePointGate
{
    public int Calls { get; private set; }

    public Task<RestorePointGateResult> EnsureForTodayAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new RestorePointGateResult(RestorePointGateStatus.CreatedNow, null));
    }
}

internal sealed class TempFile : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "m1vclock-opt-tests" + Path.GetRandomFileName());

    public TempFile()
    {
        Directory.CreateDirectory(_folder);
    }

    public string At(string name) => System.IO.Path.Combine(_folder, name);

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}

public sealed class CommandLineParserTests
{
    [Theory]
    [InlineData("\"C:\\Users\\joaop\\AppData\\Local\\Discord\\Update.exe\" --processStart Discord.exe --process-start-args \"--start-inactive\"", "C:\\Users\\joaop\\AppData\\Local\\Discord\\Update.exe")]
    [InlineData("\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", "C:\\Program Files (x86)\\Steam\\steam.exe")]
    [InlineData("C:\\Riot Games\\Riot Client\\RiotClientServices.exe --launch-background-mode", "C:\\Riot Games\\Riot Client\\RiotClientServices.exe")]
    [InlineData("\"C:\\Users\\joaop\\AppData\\Roaming\\Spotify\\Spotify.exe\" --autostart --minimized", "C:\\Users\\joaop\\AppData\\Roaming\\Spotify\\Spotify.exe")]
    [InlineData("\"C:\\WINDOWS\\system32\\AMD\\ANR\\AMDNoiseSuppression.exe\"", "C:\\WINDOWS\\system32\\AMD\\ANR\\AMDNoiseSuppression.exe")]
    [InlineData("\"C:\\Program Files\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe\" -silent -launchcontext=boot", "C:\\Program Files\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe")]
    [InlineData("\"C:\\Users\\joaop\\AppData\\Local\\Roblox\\Versions\\version-7a0c6d10fcb34a2c\\RobloxPlayerBeta.exe\" --launch-to-tray", "C:\\Users\\joaop\\AppData\\Local\\Roblox\\Versions\\version-7a0c6d10fcb34a2c\\RobloxPlayerBeta.exe")]
    [InlineData("\"C:\\Program Files\\OtimizePC\\OtimizePCNotifier.exe\"", "C:\\Program Files\\OtimizePC\\OtimizePCNotifier.exe")]
    [InlineData("\"C:\\Users\\joaop\\AppData\\Local\\Medal\\current\\Medal.exe\"", "C:\\Users\\joaop\\AppData\\Local\\Medal\\current\\Medal.exe")]
    [InlineData("\"C:\\Program Files\\Riot Vanguard\\vgtray.exe\"", "C:\\Program Files\\Riot Vanguard\\vgtray.exe")]
    [InlineData("\"C:\\Program Files\\MuseHub\\current\\MuseHub.exe\" \"----ms-protocol:ms-encodedlaunch:App?ContractId=Windows.StartupTask&TaskId=MuseHub\"", "C:\\Program Files\\MuseHub\\current\\MuseHub.exe")]
    [InlineData("C:\\Program Files (x86)\\DELUX Gaming Driver\\DELUX.exe -auto", "C:\\Program Files (x86)\\DELUX Gaming Driver\\DELUX.exe")]
    public void EveryRealStartupCommandOfThisPcGivesItsExecutable(string command, string expected)
    {
        Assert.Equal(expected, CommandLineParser.ExecutableOf(command));
    }

    [Fact]
    public void EnvironmentVariablesAreExpandedBeforeLookingForTheExecutable()
    {
        var windows = Environment.GetEnvironmentVariable("SystemRoot")!;

        Assert.Equal(Path.Combine(windows, "System32", "ctfmon.exe"), CommandLineParser.ExecutableOf("%SystemRoot%\\System32\\ctfmon.exe"), ignoreCase: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"sem fechar aspas")]
    public void ACommandWithNothingUsableGivesNoExecutable(string command)
    {
        Assert.Null(CommandLineParser.ExecutableOf(command));
    }

    [Theory]
    [InlineData("script.cmd /c algo", "script.cmd")]
    [InlineData("rundll32 shell32.dll,Foo", "rundll32")]
    [InlineData("programa", "programa")]
    public void WithoutAnExeTheFirstWordIsUsed(string command, string expected)
    {
        Assert.Equal(expected, CommandLineParser.ExecutableOf(command));
    }
}

public sealed class StartupApprovalTests
{
    [Theory]
    [InlineData(2, true)]
    [InlineData(6, true)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    [InlineData(4, false)]
    public void TheFlagsSeenOnThisPcDecideIfAnEntryRuns(byte flag, bool enabled)
    {
        Assert.Equal(enabled, StartupApproval.IsEnabled([flag, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(enabled, StartupApproval.IsEnabled([flag, 0, 0, 0]));
    }

    [Fact]
    public void AnEntryWithNoApprovalRecordRunsByDefault()
    {
        Assert.True(StartupApproval.IsEnabled(null));
        Assert.True(StartupApproval.IsEnabled([]));
    }

    [Fact]
    public void TheValuesWeWriteAreTwelveBytesAndReadBackAsWritten()
    {
        var at = new DateTimeOffset(2026, 10, 22, 19, 30, 0, TimeSpan.Zero);

        var disabled = StartupApproval.Disabled(at);
        var enabled = StartupApproval.Enabled();

        Assert.Equal(StartupApproval.Length, disabled.Length);
        Assert.Equal(StartupApproval.Length, enabled.Length);
        Assert.Equal([3, 0, 0, 0], disabled[..4]);
        Assert.Equal(at.ToFileTime(), BitConverter.ToInt64(disabled, 4));
        Assert.Equal([2, 0, 0, 0], enabled[..4]);
        Assert.False(StartupApproval.IsEnabled(disabled));
        Assert.True(StartupApproval.IsEnabled(enabled));
    }

    [Theory]
    [InlineData(StartupLocation.UserRun, RegistryHive.CurrentUser, "Run")]
    [InlineData(StartupLocation.MachineRun, RegistryHive.LocalMachine, "Run")]
    [InlineData(StartupLocation.MachineRun32, RegistryHive.LocalMachine, "Run32")]
    [InlineData(StartupLocation.UserFolder, RegistryHive.CurrentUser, "StartupFolder")]
    [InlineData(StartupLocation.CommonFolder, RegistryHive.LocalMachine, "StartupFolder")]
    public void EachLocationHasItsOwnApprovalKey(StartupLocation location, RegistryHive hive, string leaf)
    {
        var (actualHive, key) = StartupApproval.KeyOf(location);

        Assert.Equal(hive, actualHive);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + leaf, key);
    }
}

public sealed class StartupServiceTests
{
    private sealed class FakeSource(params StartupRaw[] items) : IStartupSource
    {
        public ReadResult<IReadOnlyList<StartupRaw>> Read() => ReadResult.Success<IReadOnlyList<StartupRaw>>(items);
    }

    private sealed class FakeFiles(Dictionary<string, FileDetails> details) : IFileDetailsSource
    {
        public FileDetails Describe(string path) => details.GetValueOrDefault(path, new FileDetails(false, null, null));
    }

    private static readonly byte[] Off = [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] On = [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private static StartupService Create(IStartupSource source, IFileDetailsSource files, IChangeJournal? journal = null) =>
        new(source, files, journal ?? new ThrowingJournal(), new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 19, 30, 0, TimeSpan.Zero)));

    private sealed class ThrowingJournal : IChangeJournal
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<JournalEntry> Entries => [];

        public Task<ApplyOutcome> ApplyAsync(SystemChange change, CancellationToken cancellationToken) => throw new NotSupportedException();

        public UndoOutcome Undo(Guid entryId) => throw new NotSupportedException();

        public UndoAllOutcome UndoAll() => throw new NotSupportedException();
    }

    [Fact]
    public async Task TheRealEntriesOfThisPcAreDescribedOrderedAndCounted()
    {
        var source = new FakeSource(
            new StartupRaw("Steam", StartupLocation.UserRun, "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", Off),
            new StartupRaw("RiotClient", StartupLocation.UserRun, "C:\\Riot Games\\Riot Client\\RiotClientServices.exe --launch-background-mode", On),
            new StartupRaw("Riot Vanguard", StartupLocation.MachineRun, "\"C:\\Program Files\\Riot Vanguard\\vgtray.exe\"", On),
            new StartupRaw("DELUX Gaming Driver", StartupLocation.MachineRun32, "C:\\Program Files (x86)\\DELUX Gaming Driver\\DELUX.exe -auto", Off),
            new StartupRaw("Antigo", StartupLocation.UserRun, "\"D:\\desinstalado\\antigo.exe\"", null));
        var files = new FakeFiles(new Dictionary<string, FileDetails>
        {
            ["C:\\Program Files (x86)\\Steam\\steam.exe"] = new(true, "Steam Client Bootstrapper", "Valve Corporation"),
            ["C:\\Riot Games\\Riot Client\\RiotClientServices.exe"] = new(true, "Riot Client", "Riot Games, Inc."),
            ["C:\\Program Files\\Riot Vanguard\\vgtray.exe"] = new(true, "Vanguard tray notification", "Riot Games, Inc."),
            ["C:\\Program Files (x86)\\DELUX Gaming Driver\\DELUX.exe"] = new(true, null, null),
        });

        var result = await Create(source, files).ReadAsync(CancellationToken.None);

        Assert.True(result.TryGet(out var report), result.Error);
        Assert.Equal(["Antigo", "Riot Client", "Vanguard tray notification", "DELUX Gaming Driver", "Steam Client Bootstrapper"], report.Entries.Select(e => e.DisplayName));
        Assert.Equal(3, report.Enabled);
        Assert.Equal(2, report.Disabled);
        Assert.Equal(1, report.Missing);
        var steam = report.Entries.Single(e => e.Name == "Steam");
        Assert.False(steam.Enabled);
        Assert.Equal("Valve Corporation", steam.Company);
        Assert.Equal(StartupScope.User, steam.Scope);
        Assert.Equal(StartupScope.Machine, report.Entries.Single(e => e.Name == "Riot Vanguard").Scope);
        Assert.Equal(StartupScope.Machine, report.Entries.Single(e => e.Name == "DELUX Gaming Driver").Scope);
    }

    [Fact]
    public async Task AnEntryWhoseProgramIsGoneIsFlaggedAsMissingAndStillListed()
    {
        var source = new FakeSource(new StartupRaw("Fantasma", StartupLocation.UserRun, "\"D:\\sumiu\\f.exe\"", null), new StartupRaw("Vazio", StartupLocation.UserRun, "  ", null));

        var result = await Create(source, new FakeFiles([])).ReadAsync(CancellationToken.None);

        Assert.True(result.TryGet(out var report), result.Error);
        Assert.All(report.Entries, e => Assert.True(e.TargetMissing));
        Assert.Equal("Fantasma", report.Entries.Single(e => e.Name == "Fantasma").DisplayName);
    }

    [Fact]
    public async Task ASourceThatFailsIsAFailureNotAnEmptyList()
    {
        var failing = new FailingSource();

        var result = await Create(failing, new FakeFiles([])).ReadAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("sem acesso", result.Error);
    }

    private sealed class FailingSource : IStartupSource
    {
        public ReadResult<IReadOnlyList<StartupRaw>> Read() => ReadResult.Failure<IReadOnlyList<StartupRaw>>("sem acesso");
    }

    [Fact]
    public async Task DisablingAUserEntryWritesTheDisabledFlagWithoutAskingForARestorePoint()
    {
        var registry = new MemoryRegistry();
        var gate = new CountingGate();
        using var temp = new TempFile();
        using var journal = new ChangeJournal(new JournalStore(temp.At("journal.json"), NullLogger<JournalStore>.Instance), [new RegistryValueHandler(registry)], gate, new FakeTimeProvider(), NullLogger<ChangeJournal>.Instance);
        var service = Create(new FakeSource(), new FakeFiles([]), journal);
        var entry = new StartupEntry("Steam", StartupLocation.UserRun, "x", "x", true, false, "Steam", null);

        var outcome = await service.SetEnabledAsync(entry, enabled: false, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, gate.Calls);
        var stored = Assert.Single(registry.Values).Value;
        Assert.Equal(RegistryValueKind.Binary, stored.Kind);
        Assert.False(StartupApproval.IsEnabled((byte[])stored.Data!));
        Assert.Equal("Desligar da inicialização: Steam", outcome.Entry.Title);
        Assert.Equal("Optimize", outcome.Entry.Module);
    }

    [Fact]
    public async Task ChangingAMachineEntryAsksForARestorePointFirstAndUsesTheMachineKey()
    {
        var registry = new MemoryRegistry();
        var gate = new CountingGate();
        using var temp = new TempFile();
        using var journal = new ChangeJournal(new JournalStore(temp.At("journal.json"), NullLogger<JournalStore>.Instance), [new RegistryValueHandler(registry)], gate, new FakeTimeProvider(), NullLogger<ChangeJournal>.Instance);
        var service = Create(new FakeSource(), new FakeFiles([]), journal);
        var entry = new StartupEntry("Riot Vanguard", StartupLocation.MachineRun, "x", "x", true, false, null, null);

        var outcome = await service.SetEnabledAsync(entry, enabled: false, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, gate.Calls);
        Assert.Contains("HKLM", outcome.Entry.TargetDisplay, StringComparison.Ordinal);
        Assert.EndsWith(@"StartupApproved\Run\Riot Vanguard", outcome.Entry.TargetDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnablingAgainAndUndoingPutThePreviousStateBack()
    {
        var registry = new MemoryRegistry();
        using var temp = new TempFile();
        using var journal = new ChangeJournal(new JournalStore(temp.At("journal.json"), NullLogger<JournalStore>.Instance), [new RegistryValueHandler(registry)], new CountingGate(), new FakeTimeProvider(), NullLogger<ChangeJournal>.Instance);
        var service = Create(new FakeSource(), new FakeFiles([]), journal);
        var entry = new StartupEntry("Spotify", StartupLocation.UserRun, "x", "x", false, false, null, null);
        var address = new RegistryValueAddress(RegistryHive.CurrentUser, RegistryView.Registry64, StartupApproval.KeyOf(StartupLocation.UserRun).KeyPath, "Spotify");
        registry.Write(address, new RegistryValueState(true, true, RegistryValueKind.Binary, Off));

        var applied = await service.SetEnabledAsync(entry, enabled: true, CancellationToken.None);
        Assert.True(StartupApproval.IsEnabled((byte[])registry.Read(address).Data!));

        var undo = journal.Undo(applied.Entry.Id);

        Assert.Equal(UndoStatus.Undone, undo.Status);
        Assert.Equal(Off, (byte[])registry.Read(address).Data!);
    }
}

public sealed class RealRegistryStartupTests
{
    [Fact]
    public async Task ADummyEntryCanBeDisabledVerifiedUndoneAndVerifiedAgainOnTheRealRegistry()
    {
        var name = "m1vClock-Teste-" + Guid.NewGuid().ToString("N");
        var (hive, keyPath) = StartupApproval.KeyOf(StartupLocation.UserRun);
        var address = new RegistryValueAddress(hive, RegistryView.Registry64, keyPath, name);
        var registry = new WindowsRegistryAccess();
        using var temp = new TempFile();
        using var journal = new ChangeJournal(new JournalStore(temp.At("journal.json"), NullLogger<JournalStore>.Instance), [new RegistryValueHandler(registry)], new CountingGate(), new FakeTimeProvider(), NullLogger<ChangeJournal>.Instance);
        var service = new StartupService(new WindowsStartupSource(NullLogger<WindowsStartupSource>.Instance), new WindowsFileDetailsSource(NullLogger<WindowsFileDetailsSource>.Instance), journal, TimeProvider.System);
        var entry = new StartupEntry(name, StartupLocation.UserRun, "x", "x", true, false, null, null);
        try
        {
            Assert.False(registry.Read(address).ValueExists);

            var disabled = await service.SetEnabledAsync(entry, enabled: false, TestContext.Current.CancellationToken);

            Assert.True(disabled.Succeeded);
            var afterDisable = registry.Read(address);
            Assert.True(afterDisable.ValueExists);
            Assert.False(StartupApproval.IsEnabled((byte[])afterDisable.Data!));

            var enabled = await service.SetEnabledAsync(entry, enabled: true, TestContext.Current.CancellationToken);
            Assert.True(enabled.Succeeded);
            Assert.True(StartupApproval.IsEnabled((byte[])registry.Read(address).Data!));

            Assert.Equal(UndoStatus.Undone, journal.Undo(enabled.Entry.Id).Status);
            Assert.False(StartupApproval.IsEnabled((byte[])registry.Read(address).Data!));
            Assert.Equal(UndoStatus.Undone, journal.Undo(disabled.Entry.Id).Status);
            Assert.False(registry.Read(address).ValueExists);
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    [Fact]
    public async Task TheRealStartupEntriesOfThisPcAreReadAndAgreeWithTheRegistryFlags()
    {
        var service = new StartupService(new WindowsStartupSource(NullLogger<WindowsStartupSource>.Instance), new WindowsFileDetailsSource(NullLogger<WindowsFileDetailsSource>.Instance), new NoJournal(), TimeProvider.System);

        var result = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.TryGet(out var report), result.Error);
        Assert.All(report.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Command));
            Assert.Equal(e.ExecutablePath is null || !File.Exists(e.ExecutablePath), e.TargetMissing);
            var (hive, keyPath) = StartupApproval.KeyOf(e.Location);
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(keyPath);
            Assert.Equal(StartupApproval.IsEnabled(key?.GetValue(e.Name) as byte[]), e.Enabled);
        });
        Assert.Equal(report.Entries.Count, report.Enabled + report.Disabled);
    }

    private sealed class NoJournal : IChangeJournal
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<JournalEntry> Entries => [];

        public Task<ApplyOutcome> ApplyAsync(SystemChange change, CancellationToken cancellationToken) => throw new NotSupportedException();

        public UndoOutcome Undo(Guid entryId) => throw new NotSupportedException();

        public UndoAllOutcome UndoAll() => throw new NotSupportedException();
    }
}
