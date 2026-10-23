using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.Results;
using M1vClock.Optimize.Services;
using M1vClock.Optimize.Startup;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32;

namespace M1vClock.Optimize.Tests;

public sealed class ServiceClassifierTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Theory]
    [InlineData("Auto", false, ServiceStartMode.Automatic)]
    [InlineData("Auto", true, ServiceStartMode.AutomaticDelayed)]
    [InlineData("Manual", false, ServiceStartMode.Manual)]
    [InlineData("Manual", true, ServiceStartMode.Manual)]
    [InlineData("Disabled", false, ServiceStartMode.Disabled)]
    [InlineData("Boot", false, ServiceStartMode.Boot)]
    [InlineData("System", false, ServiceStartMode.System)]
    [InlineData("Unknown", false, ServiceStartMode.Unknown)]
    [InlineData("", false, ServiceStartMode.Unknown)]
    public void TheWmiStartModesOfThisPcMapToOurModes(string wmi, bool delayed, ServiceStartMode mode)
    {
        Assert.Equal(mode, ServiceClassifier.ModeOf(wmi, delayed));
    }

    [Theory]
    [InlineData("Running", ServiceRunState.Running)]
    [InlineData("running", ServiceRunState.Running)]
    [InlineData("Stopped", ServiceRunState.Stopped)]
    [InlineData("Start Pending", ServiceRunState.Other)]
    [InlineData("", ServiceRunState.Other)]
    public void TheStatesMapToRunningStoppedOrOther(string wmi, ServiceRunState state)
    {
        Assert.Equal(state, ServiceClassifier.StateOf(wmi));
    }

    [Fact]
    public void AServiceInTheWindowsFolderIsMicrosoftAndOneElsewhereIsThirdPartyUnlessTheCompanyIsMicrosoft()
    {
        Assert.Equal(ServiceOrigin.Microsoft, ServiceClassifier.OriginOf(Windows + @"\System32\svchost.exe", null, Windows));
        Assert.Equal(ServiceOrigin.ThirdParty, ServiceClassifier.OriginOf(@"C:\Program Files (x86)\Kaspersky Lab\Kaspersky 21.27\avp.exe", "AO Kaspersky Lab", Windows));
        Assert.Equal(ServiceOrigin.Microsoft, ServiceClassifier.OriginOf(@"C:\ProgramData\Microsoft\Windows Defender\Platform\4.18\MsMpEng.exe", "Microsoft Corporation", Windows));
        Assert.Equal(ServiceOrigin.Microsoft, ServiceClassifier.OriginOf(null, null, Windows));
        Assert.Equal(ServiceOrigin.Microsoft, ServiceClassifier.OriginOf("  ", null, Windows));
        Assert.Equal(ServiceOrigin.ThirdParty, ServiceClassifier.OriginOf(Windows + "extra" + @"\x.exe", null, Windows));
    }

    [Theory]
    [InlineData("EasyAntiCheat_EOS", "EasyAntiCheat_EOS", null, ProtectionReason.AntiCheat)]
    [InlineData("vgc", "vgc", "Riot Games, Inc.", ProtectionReason.AntiCheat)]
    [InlineData("vgk", "vgk", null, ProtectionReason.AntiCheat)]
    [InlineData("BEService", "BattlEye Service", null, ProtectionReason.AntiCheat)]
    [InlineData("AVP21.27", "Kaspersky Anti-Virus", "AO Kaspersky Lab", ProtectionReason.SecuritySoftware)]
    [InlineData("WinDefend", "Serviço Antivírus do Microsoft Defender", "Microsoft Corporation", ProtectionReason.SecuritySoftware)]
    [InlineData("chromoting", "Chrome Remote Desktop Service", "Google LLC", ProtectionReason.None)]
    [InlineData("DiscordSystemHelper", "Discord System Helper", "Discord Inc.", ProtectionReason.None)]
    [InlineData("EpicGamesUpdater", "Epic Games Updater", "Epic Games, Inc.", ProtectionReason.None)]
    public void ThirdPartyServicesOfThisPcAreProtectedOnlyWhenTheyAreAntiCheatOrSecuritySoftware(string name, string display, string? company, ProtectionReason expected)
    {
        Assert.Equal(expected, ServiceClassifier.ProtectionOf(name, display, company, null, ServiceOrigin.ThirdParty));
    }

    [Theory]
    [InlineData("ResetExplodeHelper", "Reset Explode Helper")]
    [InlineData("PresetService", "Preset Manager")]
    [InlineData("MeuAvpTool", "Navpad")]
    public void ANameThatOnlyContainsAKeywordInsideAWordIsNotProtected(string name, string display)
    {
        Assert.Equal(ProtectionReason.None, ServiceClassifier.ProtectionOf(name, display, null, null, ServiceOrigin.ThirdParty));
    }

    [Fact]
    public void EveryMicrosoftServiceIsProtectedAsWindows()
    {
        Assert.Equal(ProtectionReason.Windows, ServiceClassifier.ProtectionOf("Spooler", "Spooler de Impressão", null, null, ServiceOrigin.Microsoft));
    }
}

public sealed class ServiceManagerTests
{
    private sealed class FakeServices(params ServiceRaw[] items) : IServiceSource
    {
        public ReadResult<IReadOnlyList<ServiceRaw>> Read(CancellationToken cancellationToken) => ReadResult.Success<IReadOnlyList<ServiceRaw>>(items);
    }

    private sealed class FakeCompany(Dictionary<string, string> companies) : IFileDetailsSource
    {
        public FileDetails Describe(string path) => new(true, null, companies.GetValueOrDefault(path));
    }

    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static ServiceRaw Raw(string name, string display, string state, string start, string path, bool delayed = false) =>
        new(name, display, "Descrição de " + name, state, start, delayed, "LocalSystem", path);

    private static ServiceManager Create(IServiceSource source, IFileDetailsSource? files = null, IChangeJournal? journal = null) =>
        new(source, files ?? new FakeCompany([]), journal ?? new NoJournal());

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

    private static FakeServices RealServices() => new(
        Raw("Spooler", "Spooler de Impressão", "Running", "Auto", Windows + @"\System32\spoolsv.exe"),
        Raw("DiagTrack", "Experiências do Usuário Conectado", "Stopped", "Disabled", $"\"{Windows}\\System32\\svchost.exe\" -k utcsvc -p"),
        Raw("AVP21.27", "Kaspersky", "Running", "Auto", "\"C:\\Program Files (x86)\\Kaspersky Lab\\Kaspersky 21.27\\avp.exe\" -r"),
        Raw("chromoting", "Chrome Remote Desktop Service", "Running", "Auto", "\"C:\\Program Files (x86)\\Google\\Chrome Remote Desktop\\154.0.8037.40\\remoting_host.exe\" --type=daemon"),
        Raw("EpicGamesUpdater", "Epic Games Updater", "Stopped", "Manual", "\"C:\\Program Files\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesUpdater.exe\""),
        Raw("EasyAntiCheat_EOS", "EasyAntiCheat (Epic Online Services)", "Stopped", "Manual", "\"C:\\Program Files (x86)\\EasyAntiCheat_EOS\\EasyAntiCheat_EOS.exe\""),
        Raw("WSearch", "Windows Search", "Running", "Auto", $"{Windows}\\System32\\SearchIndexer.exe /Embedding", delayed: true));

    private static FakeCompany Companies() => new(new Dictionary<string, string>
    {
        ["C:\\Program Files (x86)\\Kaspersky Lab\\Kaspersky 21.27\\avp.exe"] = "AO Kaspersky Lab",
        ["C:\\Program Files (x86)\\Google\\Chrome Remote Desktop\\154.0.8037.40\\remoting_host.exe"] = "Google LLC",
        ["C:\\Program Files\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesUpdater.exe"] = "Epic Games, Inc.",
    });

    [Fact]
    public async Task TheRealServicesOfThisPcAreClassifiedAndOrderedThirdPartyFirstRunningFirst()
    {
        var result = await Create(RealServices(), Companies()).ReadAsync(CancellationToken.None);

        Assert.True(result.TryGet(out var report), result.Error);
        Assert.Equal(["Chrome Remote Desktop Service", "Kaspersky", "EasyAntiCheat (Epic Online Services)", "Epic Games Updater", "Spooler de Impressão", "Windows Search", "Experiências do Usuário Conectado"], report.Services.Select(s => s.DisplayName));
        Assert.Equal(7, report.Total);
        Assert.Equal(4, report.ThirdParty);
        Assert.Equal(4, report.Running);
        Assert.Equal(1, report.Disabled);
        Assert.Equal(2, report.AutomaticThirdParty);
    }

    [Fact]
    public async Task OnlyThirdPartyServicesThatAreNeitherSecurityNorAntiCheatCanBeChanged()
    {
        var result = await Create(RealServices(), Companies()).ReadAsync(CancellationToken.None);

        var report = result.Value!;
        Assert.Equal(["EpicGamesUpdater", "chromoting"], report.Services.Where(s => s.CanChange).Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.Equal(ProtectionReason.SecuritySoftware, report.Services.Single(s => s.Name == "AVP21.27").Protection);
        Assert.Equal(ProtectionReason.AntiCheat, report.Services.Single(s => s.Name == "EasyAntiCheat_EOS").Protection);
        Assert.Equal(ProtectionReason.Windows, report.Services.Single(s => s.Name == "Spooler").Protection);
    }

    [Fact]
    public async Task TheDelayedFlagMakesAnAutomaticServiceDelayedAndTheCompanyComesFromTheFile()
    {
        var report = (await Create(RealServices(), Companies()).ReadAsync(CancellationToken.None)).Value!;

        Assert.Equal(ServiceStartMode.AutomaticDelayed, report.Services.Single(s => s.Name == "WSearch").StartMode);
        Assert.True(report.Services.Single(s => s.Name == "WSearch").StartsAutomatically);
        Assert.Equal("Google LLC", report.Services.Single(s => s.Name == "chromoting").Company);
        Assert.Equal(@"C:\Program Files (x86)\Google\Chrome Remote Desktop\154.0.8037.40\remoting_host.exe", report.Services.Single(s => s.Name == "chromoting").ImagePath);
    }

    [Fact]
    public async Task ALocalSessionManagerLikeServiceWithNoFileIsAWindowsServiceAndCannotBeChanged()
    {
        var source = new FakeServices(Raw("LSM", "LSM", "Running", "Unknown", string.Empty), Raw("Estranho", "Estranho", "Running", "Unknown", "\"C:\\Programas\\estranho.exe\""));

        var report = (await Create(source, new FakeCompany([])).ReadAsync(CancellationToken.None)).Value!;

        var lsm = report.Services.Single(s => s.Name == "LSM");
        Assert.Equal(ServiceOrigin.Microsoft, lsm.Origin);
        Assert.Equal(ProtectionReason.Windows, lsm.Protection);
        Assert.False(lsm.CanChange);
        var strange = report.Services.Single(s => s.Name == "Estranho");
        Assert.Equal(ServiceOrigin.ThirdParty, strange.Origin);
        Assert.Equal(ProtectionReason.None, strange.Protection);
        Assert.False(strange.CanChange);
    }

    [Fact]
    public async Task ASourceThatFailsIsAFailure()
    {
        var result = await Create(new FailingServices()).ReadAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("WMI quebrado", result.Error);
    }

    private sealed class FailingServices : IServiceSource
    {
        public ReadResult<IReadOnlyList<ServiceRaw>> Read(CancellationToken cancellationToken) => ReadResult.Failure<IReadOnlyList<ServiceRaw>>("WMI quebrado");
    }

    [Theory]
    [InlineData(ServiceStartMode.Automatic, 2)]
    [InlineData(ServiceStartMode.Manual, 3)]
    [InlineData(ServiceStartMode.Disabled, 4)]
    public void TheRegistryStartValuesAreTheOnesWindowsUses(ServiceStartMode mode, int value)
    {
        Assert.Equal(value, ServiceManager.StartValueOf(mode));
    }

    [Theory]
    [InlineData(ServiceStartMode.AutomaticDelayed)]
    [InlineData(ServiceStartMode.Boot)]
    [InlineData(ServiceStartMode.System)]
    [InlineData(ServiceStartMode.Unknown)]
    public void ModesThatCannotBeChosenAreRefused(ServiceStartMode mode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceManager.StartValueOf(mode));
    }

    private static (ServiceManager Manager, MemoryRegistry Registry, CountingGate Gate, ChangeJournal Journal, TempFile Temp) WithJournal()
    {
        var registry = new MemoryRegistry();
        var gate = new CountingGate();
        var temp = new TempFile();
        var journal = new ChangeJournal(new JournalStore(temp.At("journal.json"), NullLogger<JournalStore>.Instance), [new RegistryValueHandler(registry)], gate, new FakeTimeProvider(), NullLogger<ChangeJournal>.Instance);
        return (Create(RealServices(), Companies(), journal), registry, gate, journal, temp);
    }

    [Fact]
    public async Task ChangingAThirdPartyServiceWritesTheStartValueAfterARestorePointAndCanBeUndone()
    {
        var (manager, registry, gate, journal, temp) = WithJournal();
        using (temp)
        using (journal)
        {
            var entry = (await manager.ReadAsync(CancellationToken.None)).Value!.Services.Single(s => s.Name == "chromoting");
            var address = new RegistryValueAddress(RegistryHive.LocalMachine, RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Services\chromoting", "Start");
            registry.Write(address, RegistryValueState.DWord(2));

            var applied = await manager.SetStartModeAsync(entry, ServiceStartMode.Manual, CancellationToken.None);

            Assert.True(applied.Succeeded);
            Assert.Equal(1, gate.Calls);
            Assert.Equal(3, registry.Read(address).Data);
            Assert.Equal("Mudar o início do serviço Chrome Remote Desktop Service para manual", applied.Entry.Title);
            Assert.Equal("Optimize", applied.Entry.Module);

            Assert.Equal(UndoStatus.Undone, journal.Undo(applied.Entry.Id).Status);
            Assert.Equal(2, registry.Read(address).Data);
        }
    }

    [Theory]
    [InlineData("AVP21.27")]
    [InlineData("EasyAntiCheat_EOS")]
    [InlineData("Spooler")]
    public async Task AProtectedServiceIsRefusedBeforeAnythingIsWritten(string name)
    {
        var (manager, registry, gate, journal, temp) = WithJournal();
        using (temp)
        using (journal)
        {
            var entry = (await manager.ReadAsync(CancellationToken.None)).Value!.Services.Single(s => s.Name == name);

            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SetStartModeAsync(entry, ServiceStartMode.Disabled, CancellationToken.None));

            Assert.Empty(registry.Values);
            Assert.Equal(0, gate.Calls);
            Assert.Empty(journal.Entries);
        }
    }

    [Theory]
    [InlineData("..\\Outro")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("nome\nquebrado")]
    public async Task AServiceNameThatCouldEscapeTheServicesKeyIsRefused(string name)
    {
        var (manager, registry, _, journal, temp) = WithJournal();
        using (temp)
        using (journal)
        {
            var entry = new ServiceEntry(name, "x", null, ServiceRunState.Stopped, ServiceStartMode.Manual, null, null, ServiceOrigin.ThirdParty, null, ProtectionReason.None);

            await Assert.ThrowsAsync<ArgumentException>(() => manager.SetStartModeAsync(entry, ServiceStartMode.Disabled, CancellationToken.None));

            Assert.Empty(registry.Values);
        }
    }
}

public sealed class RealMachineServiceTests
{
    private static ServiceManager Create() => new(
        new WmiServiceSource(NullLogger<WmiServiceSource>.Instance),
        new WindowsFileDetailsSource(NullLogger<WindowsFileDetailsSource>.Instance),
        new NoJournal());

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

    [Fact]
    public async Task TheServicesOfThisPcAreReadAndEveryOneOfThemIsConsistent()
    {
        var result = await Create().ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.TryGet(out var report), result.Error);
        Assert.True(report.Total > 100);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.All(report.Services, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.DisplayName));
            Assert.True(!s.CanChange || s.Protection == ProtectionReason.None);
            Assert.True(!s.CanChange || s.StartMode is ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed or ServiceStartMode.Manual or ServiceStartMode.Disabled);
            if (s.ImagePath is not null && s.ImagePath.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(ServiceOrigin.Microsoft, s.Origin);
                Assert.False(s.CanChange);
            }

            if (s.Origin == ServiceOrigin.Microsoft)
            {
                Assert.False(s.CanChange);
            }
        });
        Assert.Contains(report.Services, s => s.Origin == ServiceOrigin.Microsoft);
    }

    [Fact]
    public async Task EveryProtectedThirdPartyServiceOfThisPcIsReallySecurityOrAntiCheat()
    {
        var report = (await Create().ReadAsync(TestContext.Current.CancellationToken)).Value!;

        var protectedThirdParty = report.Services.Where(s => s.Origin == ServiceOrigin.ThirdParty && !s.CanChange).ToList();

        Assert.All(protectedThirdParty, s => Assert.True(s.Protection is ProtectionReason.SecuritySoftware or ProtectionReason.AntiCheat, s.Name));
        Assert.All(report.Services.Where(s => s.Name.StartsWith("AVP", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)), s => Assert.False(s.CanChange, s.Name));
    }
}
