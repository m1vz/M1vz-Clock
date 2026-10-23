using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using M1vClock.Optimize.Tweaks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32;

namespace M1vClock.Optimize.Tests;

internal sealed class FakePower : IPowerSchemeAccess
{
    public static readonly Guid Atlas = new("11111111-1111-1111-1111-111111111111");

    public Guid? ActiveScheme { get; set; } = Atlas;

    public Dictionary<Guid, string> Schemes { get; } = new()
    {
        [Atlas] = "Atlas Power Scheme",
        [TweakCatalog.Balanced] = "Equilibrado",
        [TweakCatalog.HighPerformance] = "Alto desempenho",
    };

    public int Sets { get; private set; }

    public Guid? Active() => ActiveScheme;

    public bool Exists(Guid scheme) => Schemes.ContainsKey(scheme);

    public string? NameOf(Guid scheme) => Schemes.GetValueOrDefault(scheme);

    public void SetActive(Guid scheme)
    {
        if (!Schemes.ContainsKey(scheme))
        {
            throw new InvalidOperationException("Plano inexistente.");
        }

        Sets++;
        ActiveScheme = scheme;
    }
}

internal sealed class FakeMouse : INativeMouse
{
    public bool Works { get; set; } = true;

    public List<MouseAcceleration> Applied { get; } = [];

    public MouseAcceleration? Read() => Applied.Count == 0 ? null : Applied[^1];

    public bool Apply(MouseAcceleration value)
    {
        Applied.Add(value);
        return Works;
    }
}

internal sealed class FailingRegistry(IRegistryAccess inner) : IRegistryAccess
{
    public HashSet<string> FailOnWrite { get; } = new(StringComparer.Ordinal);

    public RegistryValueState Read(RegistryValueAddress address) => inner.Read(address);

    public void Write(RegistryValueAddress address, RegistryValueState state)
    {
        if (FailOnWrite.Contains(address.Target))
        {
            throw new UnauthorizedAccessException("Acesso negado de mentira.");
        }

        inner.Write(address, state);
    }
}

internal sealed class TweakRig : IDisposable
{
    private readonly TempFile _temp = new();

    public TweakRig(IReadOnlyList<TweakDefinition>? catalog = null, Func<MemoryRegistry, IRegistryAccess>? wrap = null)
    {
        Registry = new MemoryRegistry();
        Access = wrap?.Invoke(Registry) ?? Registry;
        Journal = new ChangeJournal(
            new JournalStore(_temp.At("journal.json"), NullLogger<JournalStore>.Instance),
            [new RegistryValueHandler(Access), new PowerSchemeHandler(Power)],
            Gate,
            new FakeTimeProvider(),
            NullLogger<ChangeJournal>.Instance);
        Service = new TweakService(catalog ?? TweakCatalog.Create(), Access, Power, Mouse, Journal, NullLogger<TweakService>.Instance);
    }

    public MemoryRegistry Registry { get; }

    public IRegistryAccess Access { get; }

    public FakePower Power { get; } = new();

    public FakeMouse Mouse { get; } = new();

    public CountingGate Gate { get; } = new();

    public ChangeJournal Journal { get; }

    public TweakService Service { get; }

    public static RegistryValueAddress User(string key, string name) => new(RegistryHive.CurrentUser, RegistryView.Registry64, key, name);

    public static RegistryValueAddress Machine(string key, string name) => new(RegistryHive.LocalMachine, RegistryView.Registry64, key, name);

    public static RegistryValueAddress GameDvrA => User(@"System\GameConfigStore", "GameDVR_Enabled");

    public static RegistryValueAddress GameDvrB => User(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");

    public static RegistryValueAddress GameMode => User(@"Software\Microsoft\GameBar", "AutoGameModeEnabled");

    public static RegistryValueAddress Hags => Machine(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");

    public static RegistryValueAddress DxSettings => User(@"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings");

    public static RegistryValueAddress MouseSpeed => User(TweakCatalog.MouseKey, "MouseSpeed");

    public static RegistryValueAddress MouseThreshold1 => User(TweakCatalog.MouseKey, "MouseThreshold1");

    public static RegistryValueAddress MouseThreshold2 => User(TweakCatalog.MouseKey, "MouseThreshold2");

    public async Task<TweakEntry> EntryAsync(string id)
    {
        var report = await Service.ReadAsync(TestContext.Current.CancellationToken);
        return report.Value!.Entries.Single(e => e.Id == id);
    }

    public void Dispose()
    {
        Journal.Dispose();
        _temp.Dispose();
    }
}

public sealed class TweakOptionTextTests
{
    [Fact]
    public void AnOptionIsFoundIgnoringCaseAndSpaces()
    {
        Assert.Equal("1", OptionStep.OptionOf("A=0; swapeffectupgradeenable = 1 ;B=2;", "SwapEffectUpgradeEnable"));
        Assert.Null(OptionStep.OptionOf("A=0;", "SwapEffectUpgradeEnable"));
        Assert.Null(OptionStep.OptionOf(string.Empty, "X"));
    }

    [Fact]
    public void SettingAnOptionKeepsEveryOtherOptionInOrder()
    {
        Assert.Equal("VSyncIdleTimeout=0;SwapEffectUpgradeEnable=1;Foo=bar;", OptionStep.WithOption("VSyncIdleTimeout=0;SwapEffectUpgradeEnable=0;Foo=bar;", "SwapEffectUpgradeEnable", "1"));
        Assert.Equal("Foo=bar;SwapEffectUpgradeEnable=1;", OptionStep.WithOption("Foo=bar;", "SwapEffectUpgradeEnable", "1"));
        Assert.Equal("SwapEffectUpgradeEnable=1;", OptionStep.WithOption(string.Empty, "SwapEffectUpgradeEnable", "1"));
    }
}

public sealed class TweakServiceTests
{
    [Fact]
    public async Task OnAFreshWindowsEveryTweakIsReadAsOffExceptTheOnesWhoseDefaultIsOn()
    {
        using var rig = new TweakRig();

        var report = (await rig.Service.ReadAsync(TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(["power", "gamemode", "gamedvr", "hags", "windowed", "mouse"], report.Entries.Select(e => e.Id));
        Assert.Equal(TweakState.On, report.Entries.Single(e => e.Id == "gamemode").State);
        Assert.All(report.Entries.Where(e => e.Id != "gamemode"), e => Assert.Equal(TweakState.Off, e.State));
        Assert.All(report.Entries, e => Assert.False(e.HasOriginalToRestore));
        Assert.Equal(1, report.On);
        Assert.Equal(5, report.Off);
    }

    [Fact]
    public async Task TurningGameDvrOffRecordingWritesBothValuesAndSaysWhatChanges()
    {
        using var rig = new TweakRig();

        var plan = rig.Service.Plan("gamedvr", turnOn: true);
        var outcome = await rig.Service.SetAsync("gamedvr", turnOn: true, TestContext.Current.CancellationToken);

        Assert.NotNull(plan);
        Assert.Equal(2, plan.Changes.Count);
        Assert.All(plan.Changes, c => Assert.Null(c.From));
        Assert.All(plan.Changes, c => Assert.Equal("0", c.To));
        Assert.False(plan.NeedsAdministrator);
        Assert.True(outcome.Succeeded);
        Assert.Equal(TweakState.On, outcome.State);
        Assert.Equal(0, rig.Registry.Read(TweakRig.GameDvrA).Data);
        Assert.Equal(0, rig.Registry.Read(TweakRig.GameDvrB).Data);
        Assert.Equal(0, rig.Gate.Calls);
        Assert.True((await rig.EntryAsync("gamedvr")).HasOriginalToRestore);
    }

    [Fact]
    public async Task TurningItOffAgainWritesTheWindowsDefaultsAndRestoreOriginalsBringsBackTheAbsence()
    {
        using var rig = new TweakRig();
        await rig.Service.SetAsync("gamedvr", turnOn: true, TestContext.Current.CancellationToken);

        var off = await rig.Service.SetAsync("gamedvr", turnOn: false, TestContext.Current.CancellationToken);

        Assert.True(off.Succeeded);
        Assert.Equal(TweakState.Off, off.State);
        Assert.Equal(1, rig.Registry.Read(TweakRig.GameDvrA).Data);

        var restored = await rig.Service.RestoreOriginalsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, restored.Undone);
        Assert.Empty(restored.Failures);
        Assert.False(rig.Registry.Read(TweakRig.GameDvrA).ValueExists);
        Assert.False(rig.Registry.Read(TweakRig.GameDvrB).ValueExists);
        Assert.False((await rig.EntryAsync("gamedvr")).HasOriginalToRestore);
    }

    [Fact]
    public async Task ATweakThatIsAlreadyInTheWantedStateHasNothingToDo()
    {
        using var rig = new TweakRig();

        Assert.Null(rig.Service.Plan("gamemode", turnOn: true));
        Assert.Null(rig.Service.Plan("gamedvr", turnOn: false));
        var outcome = await rig.Service.SetAsync("gamemode", turnOn: true, TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        Assert.Empty(rig.Journal.Entries);
        var entry = await rig.EntryAsync("gamemode");
        Assert.Equal(TweakState.On, entry.State);
        Assert.True(entry.CanChange);
    }

    [Fact]
    public async Task GameModeIsOnWhenAbsentAndTurningItOffThenOnWritesZeroThenOne()
    {
        using var rig = new TweakRig();

        await rig.Service.SetAsync("gamemode", turnOn: false, TestContext.Current.CancellationToken);
        Assert.Equal(0, rig.Registry.Read(TweakRig.GameMode).Data);
        Assert.Equal(TweakState.Off, (await rig.EntryAsync("gamemode")).State);

        await rig.Service.SetAsync("gamemode", turnOn: true, TestContext.Current.CancellationToken);
        Assert.Equal(1, rig.Registry.Read(TweakRig.GameMode).Data);
        Assert.Equal(TweakState.On, (await rig.EntryAsync("gamemode")).State);
    }

    [Fact]
    public async Task HardwareSchedulingNeedsAdministratorARestartAndAskedForARestorePoint()
    {
        using var rig = new TweakRig();

        var entry = await rig.EntryAsync("hags");
        var plan = rig.Service.Plan("hags", turnOn: true);
        var outcome = await rig.Service.SetAsync("hags", turnOn: true, TestContext.Current.CancellationToken);

        Assert.True(entry.NeedsAdministrator);
        Assert.True(entry.NeedsRestart);
        Assert.True(plan!.NeedsAdministrator);
        Assert.True(plan.NeedsRestart);
        Assert.Equal(2, rig.Registry.Read(TweakRig.Hags).Data);
        Assert.True(outcome.RestartNeeded);
        Assert.Equal(1, rig.Gate.Calls);
        Assert.True(outcome.RestorePointProtected);
    }

    [Fact]
    public async Task WindowedOptimizationsChangeOnlyTheirOwnOptionAndTheUndoRestoresTheWholeText()
    {
        using var rig = new TweakRig();
        const string original = "VSyncIdleTimeout=0;SwapEffectUpgradeEnable=0;Foo=bar;";
        rig.Registry.Write(TweakRig.DxSettings, RegistryValueState.Text(original));

        var on = await rig.Service.SetAsync("windowed", turnOn: true, TestContext.Current.CancellationToken);

        Assert.True(on.Succeeded);
        Assert.Equal("VSyncIdleTimeout=0;SwapEffectUpgradeEnable=1;Foo=bar;", rig.Registry.Read(TweakRig.DxSettings).Data);
        await rig.Service.RestoreOriginalsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original, rig.Registry.Read(TweakRig.DxSettings).Data);
    }

    [Fact]
    public async Task WindowedOptimizationsOnAnAbsentValueCreateItAndRestoreRemovesIt()
    {
        using var rig = new TweakRig();

        await rig.Service.SetAsync("windowed", turnOn: true, TestContext.Current.CancellationToken);
        Assert.Equal("SwapEffectUpgradeEnable=1;", rig.Registry.Read(TweakRig.DxSettings).Data);

        await rig.Service.RestoreOriginalsAsync(TestContext.Current.CancellationToken);
        Assert.False(rig.Registry.Read(TweakRig.DxSettings).ValueExists);
    }

    [Fact]
    public async Task MouseAccelerationIsDecidedByTheSpeedAloneAndIsAppliedRightAway()
    {
        using var rig = new TweakRig();
        rig.Registry.Write(TweakRig.MouseSpeed, RegistryValueState.Text("0"));
        rig.Registry.Write(TweakRig.MouseThreshold1, RegistryValueState.Text("6"));
        rig.Registry.Write(TweakRig.MouseThreshold2, RegistryValueState.Text("10"));

        Assert.Equal(TweakState.On, (await rig.EntryAsync("mouse")).State);

        var off = await rig.Service.SetAsync("mouse", turnOn: false, TestContext.Current.CancellationToken);

        Assert.True(off.Succeeded);
        Assert.Equal("1", rig.Registry.Read(TweakRig.MouseSpeed).Data);
        Assert.Equal("6", rig.Registry.Read(TweakRig.MouseThreshold1).Data);
        Assert.Single(rig.Journal.Entries);
        Assert.Equal(new MouseAcceleration(6, 10, 1), rig.Mouse.Applied[^1]);

        var on = await rig.Service.SetAsync("mouse", turnOn: true, TestContext.Current.CancellationToken);

        Assert.True(on.Succeeded);
        Assert.Equal("0", rig.Registry.Read(TweakRig.MouseSpeed).Data);
        Assert.Equal("0", rig.Registry.Read(TweakRig.MouseThreshold1).Data);
        Assert.Equal("0", rig.Registry.Read(TweakRig.MouseThreshold2).Data);
        Assert.Equal(new MouseAcceleration(0, 0, 0), rig.Mouse.Applied[^1]);
    }

    [Fact]
    public async Task IfWindowsDoesNotApplyTheMouseNowTheChangeStaysSavedAndTheOutcomeSaysSo()
    {
        using var rig = new TweakRig();
        rig.Mouse.Works = false;
        rig.Registry.Write(TweakRig.MouseSpeed, RegistryValueState.Text("1"));
        rig.Registry.Write(TweakRig.MouseThreshold1, RegistryValueState.Text("6"));
        rig.Registry.Write(TweakRig.MouseThreshold2, RegistryValueState.Text("10"));

        var outcome = await rig.Service.SetAsync("mouse", turnOn: true, TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.LiveFailed);
        Assert.Equal("0", rig.Registry.Read(TweakRig.MouseSpeed).Data);
    }

    [Fact]
    public async Task TheHighPerformancePlanIsSwitchedToAndTheOneYouHadComesBackWhenTurnedOff()
    {
        using var rig = new TweakRig();

        var on = await rig.Service.SetAsync("power", turnOn: true, TestContext.Current.CancellationToken);

        Assert.True(on.Succeeded);
        Assert.Equal(TweakCatalog.HighPerformance, rig.Power.ActiveScheme);
        var plan = rig.Service.Plan("power", turnOn: false);
        Assert.Equal("Alto desempenho", plan!.Changes.Single().From);
        Assert.Equal("Atlas Power Scheme", plan.Changes.Single().To);

        var off = await rig.Service.SetAsync("power", turnOn: false, TestContext.Current.CancellationToken);

        Assert.True(off.Succeeded);
        Assert.Equal(FakePower.Atlas, rig.Power.ActiveScheme);
        Assert.Equal(TweakState.Off, off.State);
    }

    [Fact]
    public async Task RestoreOriginalsBringsBackThePlanThatWasActiveBeforeEverything()
    {
        using var rig = new TweakRig();
        await rig.Service.SetAsync("power", turnOn: true, TestContext.Current.CancellationToken);
        await rig.Service.SetAsync("power", turnOn: false, TestContext.Current.CancellationToken);
        await rig.Service.SetAsync("power", turnOn: true, TestContext.Current.CancellationToken);

        var restored = await rig.Service.RestoreOriginalsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, restored.Undone);
        Assert.Equal(FakePower.Atlas, rig.Power.ActiveScheme);
    }

    [Fact]
    public async Task WithoutHistoryTurningHighPerformanceOffGoesToBalanced()
    {
        using var rig = new TweakRig();
        rig.Power.ActiveScheme = TweakCatalog.HighPerformance;

        Assert.Equal(TweakState.On, (await rig.EntryAsync("power")).State);
        var off = await rig.Service.SetAsync("power", turnOn: false, TestContext.Current.CancellationToken);

        Assert.True(off.Succeeded);
        Assert.Equal(TweakCatalog.Balanced, rig.Power.ActiveScheme);
    }

    [Fact]
    public async Task WithNoPlanToReturnToTheTweakCannotBeTurnedOffAndSaysWhy()
    {
        using var rig = new TweakRig();
        rig.Power.ActiveScheme = TweakCatalog.HighPerformance;
        rig.Power.Schemes.Remove(TweakCatalog.Balanced);

        var entry = await rig.EntryAsync("power");

        Assert.Equal(TweakState.On, entry.State);
        Assert.Equal(TweakIssue.NoPlanToReturnTo, entry.Issue);
        Assert.False(entry.CanChange);
        Assert.Null(rig.Service.Plan("power", turnOn: false));
    }

    [Fact]
    public async Task AWindowsWithoutTheHighPerformancePlanShowsTheTweakAsUnavailable()
    {
        using var rig = new TweakRig();
        rig.Power.Schemes.Remove(TweakCatalog.HighPerformance);

        var entry = await rig.EntryAsync("power");

        Assert.Equal(TweakState.Unavailable, entry.State);
        Assert.Equal(TweakIssue.NoHighPerformancePlan, entry.Issue);
        Assert.False(entry.CanChange);
    }

    [Fact]
    public async Task WhenTheSecondValueFailsTheFirstIsUndoneAndNothingIsHalfApplied()
    {
        using var rig = new TweakRig(wrap: registry => new FailingRegistry(registry) { FailOnWrite = { TweakRig.GameDvrB.Target } });

        var outcome = await rig.Service.SetAsync("gamedvr", turnOn: true, TestContext.Current.CancellationToken);

        Assert.False(outcome.Succeeded);
        Assert.Contains("Acesso negado de mentira", outcome.Error, StringComparison.Ordinal);
        Assert.False(rig.Registry.Read(TweakRig.GameDvrA).ValueExists);
        Assert.False(rig.Registry.Read(TweakRig.GameDvrB).ValueExists);
        Assert.Equal(TweakState.Off, outcome.State);
        Assert.Contains(rig.Journal.Entries, e => e.Status == ChangeStatus.Undone);
        Assert.Contains(rig.Journal.Entries, e => e.Status == ChangeStatus.ApplyFailed);
        Assert.False((await rig.EntryAsync("gamedvr")).HasOriginalToRestore);
    }

    [Fact]
    public async Task ChangesOfOtherModulesAreNotTouchedByRestoreOriginals()
    {
        using var rig = new TweakRig();
        var other = RegistryValueChanges.Set("Optimize", "Outra coisa", TweakRig.User(@"Software\Teste", "X"), RegistryValueState.DWord(7), affectsSystem: false);
        await rig.Journal.ApplyAsync(other, TestContext.Current.CancellationToken);
        await rig.Service.SetAsync("gamedvr", turnOn: true, TestContext.Current.CancellationToken);

        var restored = await rig.Service.RestoreOriginalsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, restored.Undone);
        Assert.Equal(7, rig.Registry.Read(TweakRig.User(@"Software\Teste", "X")).Data);
    }

    [Fact]
    public void AnUnknownTweakIsRejected()
    {
        using var rig = new TweakRig();

        Assert.Throws<ArgumentException>(() => rig.Service.Plan("nao-existe", turnOn: true));
    }

    [Fact]
    public async Task AMixedGameDvrIsShownAsMixedAndTurningItOnFinishesTheJob()
    {
        using var rig = new TweakRig();
        rig.Registry.Write(TweakRig.GameDvrA, RegistryValueState.DWord(0));

        Assert.Equal(TweakState.Mixed, (await rig.EntryAsync("gamedvr")).State);
        var plan = rig.Service.Plan("gamedvr", turnOn: true);

        Assert.Single(plan!.Changes);
        var outcome = await rig.Service.SetAsync("gamedvr", turnOn: true, TestContext.Current.CancellationToken);
        Assert.Equal(TweakState.On, outcome.State);
    }
}

public sealed class RealTweakTests
{
    private static IReadOnlyList<TweakDefinition> DummyCatalog(string key) =>
    [
        new("dummy-dword", "Valor de teste", false, LiveEffect.None, [new DwordStep(TweakRig.User(key, "Probe"), on: 0, off: 1, absentIsOn: false)]),
        new("dummy-text", "Texto de teste", false, LiveEffect.None, [new OptionStep(TweakRig.User(key, "Options"), "Flag", on: "1", off: "0")]),
    ];

    [Fact]
    public async Task ADummyKeyCanBeTurnedOnVerifiedRestoredAndVerifiedAgainOnTheRealRegistry()
    {
        var key = @"Software\m1vClock-Teste-" + Guid.NewGuid().ToString("N");
        var real = new WindowsRegistryAccess();
        var journal = RealJournal(out var holder);
        var realService = new TweakService(DummyCatalog(key), real, new FakePower(), new FakeMouse(), journal, NullLogger<TweakService>.Instance);
        try
        {
            Assert.False(real.Read(TweakRig.User(key, "Probe")).KeyExists);

            var on = await realService.SetAsync("dummy-dword", turnOn: true, TestContext.Current.CancellationToken);
            Assert.True(on.Succeeded);
            Assert.Equal(TweakState.On, on.State);
            Assert.Equal(0, real.Read(TweakRig.User(key, "Probe")).Data);

            var text = await realService.SetAsync("dummy-text", turnOn: true, TestContext.Current.CancellationToken);
            Assert.True(text.Succeeded);
            Assert.Equal("Flag=1;", real.Read(TweakRig.User(key, "Options")).Data);

            var restored = await realService.RestoreOriginalsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, restored.Undone);
            Assert.Empty(restored.Failures);
            Assert.False(real.Read(TweakRig.User(key, "Probe")).ValueExists);
            Assert.False(real.Read(TweakRig.User(key, "Options")).ValueExists);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
            journal.Dispose();
            holder.Dispose();
        }
    }

    private static ChangeJournal RealJournal(out TempFile holder)
    {
        holder = new TempFile();
        return new ChangeJournal(
            new JournalStore(holder.At("journal.json"), NullLogger<JournalStore>.Instance),
            [new RegistryValueHandler(new WindowsRegistryAccess()), new PowerSchemeHandler(new FakePower())],
            new CountingGate(),
            new FakeTimeProvider(),
            NullLogger<ChangeJournal>.Instance);
    }

    [Fact]
    public async Task ThisPcCatalogIsReadWithoutAnyChangeAndWithoutFailures()
    {
        using var temp = new TempFile();
        using var journal = new ChangeJournal(
            new JournalStore(temp.At("journal.json"), NullLogger<JournalStore>.Instance),
            [new RegistryValueHandler(new WindowsRegistryAccess()), new PowerSchemeHandler(new WindowsPowerSchemeAccess())],
            new CountingGate(),
            new FakeTimeProvider(),
            NullLogger<ChangeJournal>.Instance);
        var service = new TweakService(new WindowsRegistryAccess(), new WindowsPowerSchemeAccess(), new WindowsNativeMouse(), journal, NullLogger<TweakService>.Instance);

        var report = (await service.ReadAsync(TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(6, report.Entries.Count);
        Assert.All(report.Entries, e => Assert.NotEqual(TweakIssue.ReadFailed, e.Issue));
        Assert.Empty(journal.Entries);
    }

    [Fact]
    public void ThisPcPowerSchemesAreReadForReal()
    {
        var power = new WindowsPowerSchemeAccess();

        var active = power.Active();

        Assert.NotNull(active);
        Assert.True(power.Exists(active.Value));
        Assert.False(string.IsNullOrWhiteSpace(power.NameOf(active.Value)));
        Assert.False(power.Exists(Guid.NewGuid()));
        Assert.Null(power.NameOf(Guid.NewGuid()));
    }

    [Fact]
    public void ThisPcMouseSettingsAreReadAndReappliedUnchanged()
    {
        var mouse = new WindowsNativeMouse();

        var current = mouse.Read();

        Assert.NotNull(current);
        Assert.True(mouse.Apply(current.Value));
        Assert.Equal(current, mouse.Read());
    }
}
