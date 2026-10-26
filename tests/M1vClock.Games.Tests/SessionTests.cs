using System.Collections.Concurrent;
using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.RestorePoints;
using M1vClock.Core.Shell;
using M1vClock.Games.Library;
using M1vClock.Games.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace M1vClock.Games.Tests;

internal sealed class RecordingShell : IShellLauncher
{
    public ConcurrentQueue<(string Target, string? Arguments, string? Directory)> Started { get; } = new();

    public Action<string>? OnStart { get; set; }

    public Exception? Throw { get; set; }

    public void OpenUrl(Uri url) => throw new NotSupportedException();

    public void OpenFolder(string path) => throw new NotSupportedException();

    public void Start(string fileName, string? arguments, string? workingDirectory)
    {
        if (Throw is not null)
        {
            throw Throw;
        }

        Started.Enqueue((fileName, arguments, workingDirectory));
        OnStart?.Invoke(fileName);
    }
}

internal sealed class ScriptedProcesses : IGameProcesses
{
    private readonly ConcurrentDictionary<int, RunningProcess> _running = new();

    public ConcurrentQueue<(int Id, GamePriority Priority)> Priorities { get; } = new();

    public ConcurrentQueue<int> Closed { get; } = new();

    public bool DenyPriority { get; set; }

    public void Run(int id, string path) => _running[id] = new RunningProcess(id, path, DateTime.UtcNow);

    public void Exit(int id) => _running.TryRemove(id, out _);

    public IReadOnlyList<RunningProcess> List() => _running.Values.OrderBy(p => p.Id).ToList();

    public bool IsRunning(int processId) => _running.ContainsKey(processId);

    public bool SetPriority(int processId, GamePriority priority)
    {
        Priorities.Enqueue((processId, priority));
        return !DenyPriority;
    }

    public bool Close(int processId)
    {
        Closed.Enqueue(processId);
        Exit(processId);
        return true;
    }
}

internal sealed class KeyedRegistry : IRegistryAccess
{
    public Dictionary<string, RegistryValueState> Values { get; } = new(StringComparer.Ordinal);

    public RegistryValueState Read(RegistryValueAddress address) =>
        Values.TryGetValue(address.Target, out var state) ? state : RegistryValueState.Absent(keyExists: true);

    public void Write(RegistryValueAddress address, RegistryValueState state)
    {
        if (state.ValueExists)
        {
            Values[address.Target] = state;
        }
        else
        {
            Values.Remove(address.Target);
        }
    }
}

internal sealed class NoRestorePoint : IRestorePointGate
{
    public Task<RestorePointGateResult> EnsureForTodayAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new RestorePointGateResult(RestorePointGateStatus.CreatedNow, null));
}

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "m1vClock-games-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string At(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

public sealed class WindowsPreferenceValueTests
{
    [Theory]
    [InlineData(null, true, "GpuPreference=2;")]
    [InlineData("SwapEffectUpgradeEnable=1;", true, "GpuPreference=2;SwapEffectUpgradeEnable=1;")]
    [InlineData("GpuPreference=1;SwapEffectUpgradeEnable=1;", true, "GpuPreference=2;SwapEffectUpgradeEnable=1;")]
    [InlineData("GpuPreference=2;SwapEffectUpgradeEnable=1;", false, "SwapEffectUpgradeEnable=1;")]
    [InlineData("GpuPreference=2;", false, null)]
    [InlineData(null, false, null)]
    public void TheGpuPreferenceKeepsTheOtherWindowsOptionsOfTheSameValue(string? existing, bool high, string? expected)
    {
        Assert.Equal(expected, GpuPreferenceValue.With(existing, high));
        Assert.Equal(high, GpuPreferenceValue.IsHighPerformance(GpuPreferenceValue.With(existing, high)));
    }

    [Theory]
    [InlineData(null, true, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    [InlineData("~ RUNASADMIN", true, "~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    [InlineData("~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE", false, "~ RUNASADMIN")]
    [InlineData("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", false, null)]
    [InlineData("~ disabledxmaximizedwindowedmode", true, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    public void TheCompatibilityFlagKeepsTheOtherFlags(string? existing, bool disable, string? expected)
    {
        Assert.Equal(expected, CompatibilityLayers.With(existing, disable));
        Assert.Equal(disable, CompatibilityLayers.HasFullscreenOptimizationsDisabled(expected));
    }

    [Fact]
    public void OnlyWhatDiffersBecomesAChangeAndAValueOfAnotherTypeIsLeftAlone()
    {
        const string exe = @"C:\Jogos\jogo.exe";
        var registry = new KeyedRegistry();
        registry.Values[GamePreferences.GpuAddress(exe).Target] = RegistryValueState.Text("GpuPreference=2;");
        registry.Values[GamePreferences.LayersAddress(exe).Target] = RegistryValueState.DWord(1);

        var changes = GamePreferences.ChangesFor(registry, exe, new GameProfile { HighPerformanceGpu = true, DisableFullscreenOptimizations = true }, "GPU", "Tela cheia");

        Assert.Empty(changes);
        var off = GamePreferences.ChangesFor(registry, exe, new GameProfile(), "GPU", "Tela cheia");
        var change = Assert.Single(off);
        Assert.Equal("GPU", change.Title);
        Assert.False(change.Change.AffectsSystem);
    }
}

public sealed class GameSessionTests
{
    private const string Folder = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive";
    private const string GameExe = Folder + @"\game\bin\win64\cs2.exe";
    private const string Helper = @"C:\Ferramentas\Crosshair\crosshair.exe";

    private static readonly GameSessionTiming Fast = new(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(5));

    private static readonly GameSessionTitles Titles = new("GPU de alto desempenho", "Otimizações de tela cheia");

    private static GameEntry Cs2() => new("730", "Counter-Strike 2", GamePlatform.Steam, Folder, null, GameState.Installed, GameLaunch.Steam("730"));

    private static (GameSession Session, RecordingShell Shell, ScriptedProcesses Processes, KeyedRegistry Registry, ChangeJournal Journal, TempFolder Temp) Create(MemoryFiles? files = null)
    {
        var temp = new TempFolder();
        var registry = new KeyedRegistry();
        var journal = new ChangeJournal(new JournalStore(temp.At("journal.json"), NullLogger<JournalStore>.Instance), [new RegistryValueHandler(registry)], new NoRestorePoint(), TimeProvider.System, NullLogger<ChangeJournal>.Instance);
        var shell = new RecordingShell();
        var processes = new ScriptedProcesses();
        var session = new GameSession(shell, files ?? new MemoryFiles().Add(Helper, "exe"), processes, journal, registry, TimeProvider.System, NullLogger<GameSession>.Instance) { Timing = Fast };
        return (session, shell, processes, registry, journal, temp);
    }

    private static async Task<List<GameSessionStatus>> Run(GameSession session, GameEntry game, GameProfile profile, Action? whileRunning = null)
    {
        var seen = new ConcurrentQueue<GameSessionStatus>();
        var progress = new SyncProgress(s =>
        {
            seen.Enqueue(s);
            if (s.Stage == GameSessionStage.Running)
            {
                whileRunning?.Invoke();
            }
        });
        await session.RunAsync(game, profile, Titles, progress, TestContext.Current.CancellationToken);
        return seen.ToList();
    }

    [Fact]
    public async Task TheGameIsOpenedFoundRaisedInPriorityFollowedUntilItClosesAndTheHelperClosesWithIt()
    {
        var (session, shell, processes, _, journal, temp) = Create();
        using var cleanup = temp;
        using var journalScope = journal;
        shell.OnStart = target =>
        {
            if (target == Helper)
            {
                processes.Run(50, Helper);
            }
            else
            {
                processes.Run(100, GameExe);
            }
        };
        var profile = new GameProfile { Priority = GamePriority.High, Helpers = [new HelperProgram { Path = Helper }] };

        var seen = await Run(session, Cs2(), profile, () => _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            processes.Exit(100);
        }));

        Assert.Equal([Helper, "steam://rungameid/730"], shell.Started.Select(s => s.Target));
        Assert.Equal([(100, GamePriority.High)], processes.Priorities);
        Assert.Equal([50], processes.Closed);
        Assert.Equal(GameSessionStage.Finished, seen[^1].Stage);
        Assert.Contains(seen, s => s.Stage == GameSessionStage.Running && s.ProcessId == 100);
        Assert.Empty(seen[^1].Warnings);
    }

    [Fact]
    public async Task AHelperThatWasAlreadyOpenIsNeverClosed()
    {
        var (session, shell, processes, _, journal, temp) = Create();
        using var cleanup = temp;
        using var journalScope = journal;
        processes.Run(50, Helper);
        shell.OnStart = target =>
        {
            if (target != Helper)
            {
                processes.Run(100, GameExe);
            }
        };

        await Run(session, Cs2(), new GameProfile { Helpers = [new HelperProgram { Path = Helper }] }, () => processes.Exit(100));

        Assert.Empty(processes.Closed);
        Assert.True(processes.IsRunning(50));
    }

    [Fact]
    public async Task AGameThatNeverAppearsEndsAsNotDetected()
    {
        var (session, _, _, _, journal, temp) = Create();
        using var cleanup = temp;
        using var journalScope = journal;

        var seen = await Run(session, Cs2(), new GameProfile());

        Assert.Equal(GameSessionStage.NotDetected, seen[^1].Stage);
    }

    [Fact]
    public async Task ALauncherThatClosesAndOpensTheRealGameIsFollowedToTheEnd()
    {
        var (session, shell, processes, _, journal, temp) = Create();
        using var cleanup = temp;
        using var journalScope = journal;
        shell.OnStart = _ => processes.Run(100, Folder + @"\launcher.exe");
        var ran = 0;

        var seen = await Run(session, Cs2(), new GameProfile(), () =>
        {
            if (Interlocked.Increment(ref ran) == 1)
            {
                processes.Exit(100);
                processes.Run(101, GameExe);
            }
            else
            {
                processes.Exit(101);
            }
        });

        Assert.Equal([100, 101], seen.Where(s => s.Stage == GameSessionStage.Running).Select(s => s.ProcessId!.Value));
        Assert.Equal(GameSessionStage.Finished, seen[^1].Stage);
    }

    [Fact]
    public async Task APriorityTheAntiCheatRefusesBecomesAWarningNotAFailure()
    {
        var (session, shell, processes, _, journal, temp) = Create();
        using var cleanup = temp;
        using var journalScope = journal;
        processes.DenyPriority = true;
        shell.OnStart = _ => processes.Run(100, GameExe);

        var seen = await Run(session, Cs2(), new GameProfile { Priority = GamePriority.AboveNormal }, () => processes.Exit(100));

        Assert.Equal(GameSessionStage.Finished, seen[^1].Stage);
        Assert.Equal([GameSessionWarning.PriorityDenied], seen[^1].Warnings);
    }

    [Fact]
    public async Task AManualGameGetsItsArgumentsPlusTheProfileOnesAndItsPreferencesGoThroughTheJournal()
    {
        const string exe = @"D:\Jogos\Kandidatos\Kandidatos.exe";
        var (session, shell, processes, registry, journal, temp) = Create(new MemoryFiles().Add(exe, "exe"));
        using var cleanup = temp;
        using var journalScope = journal;
        var game = new GameEntry("manual:x", "Kandidatos", GamePlatform.Manual, @"D:\Jogos\Kandidatos", null, GameState.Installed, GameLaunch.Executable(exe, "-log"));
        shell.OnStart = _ => processes.Run(100, exe);
        var profile = new GameProfile { Engine = GameEngine.Unity, WindowMode = WindowMode.Borderless, HighPerformanceGpu = true, DisableFullscreenOptimizations = true };

        await Run(session, game, profile, () => processes.Exit(100));

        var started = Assert.Single(shell.Started);
        Assert.Equal((exe, "-log -popupwindow -window-mode borderless", @"D:\Jogos\Kandidatos"), started);
        Assert.Equal("GpuPreference=2;", registry.Read(GamePreferences.GpuAddress(exe)).Data);
        Assert.Equal("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", registry.Read(GamePreferences.LayersAddress(exe)).Data);
        Assert.Equal(2, journal.Entries.Count(e => e.Module == GamePreferences.Module && e.Status == ChangeStatus.Applied));
        Assert.Empty(journal.UndoAll().Failures);
        Assert.False(registry.Read(GamePreferences.GpuAddress(exe)).ValueExists);
    }

    [Fact]
    public async Task ArgumentsForAStoreThatCannotTakeThemBecomeAWarningAndSteamIsLaunchedPlain()
    {
        var (session, shell, processes, _, journal, temp) = Create();
        using var cleanup = temp;
        using var journalScope = journal;
        var fortnite = new GameEntry("Fortnite", "Fortnite", GamePlatform.Epic, @"C:\Program Files\Epic Games\Fortnite", null, GameState.Installed, GameLaunch.Epic("Fortnite"));
        var profile = new GameProfile { Extra = "-USEALLAVAILABLECORES" };

        var epic = await Run(session, fortnite, profile);
        var steam = await Run(session, Cs2(), profile);

        Assert.Equal([GameSessionWarning.ArgumentsNotSupported], epic[^1].Warnings);
        Assert.Empty(steam[^1].Warnings);
        Assert.All(shell.Started, s => Assert.Null(s.Arguments));
    }

    [Fact]
    public async Task AMissingHelperOrAShellErrorIsReported()
    {
        var (session, shell, _, _, journal, temp) = Create(new MemoryFiles());
        using var cleanup = temp;
        using var journalScope = journal;

        var missing = await Run(session, Cs2(), new GameProfile { Helpers = [new HelperProgram { Path = Helper }] });
        shell.Throw = new InvalidOperationException("O Explorer não está em execução.");
        var failed = await Run(session, Cs2(), new GameProfile());

        Assert.Contains(GameSessionWarning.HelperFailed, missing[^1].Warnings);
        Assert.Equal(GameSessionStage.Failed, failed[^1].Stage);
        Assert.Equal("O Explorer não está em execução.", failed[^1].Error);
    }

    [Fact]
    public async Task AnInvalidProfileOrAGameWithoutLaunchFailsBeforeTouchingAnything()
    {
        var (session, shell, _, registry, journal, temp) = Create();
        using var cleanup = temp;
        using var journalScope = journal;
        var noLaunch = Cs2() with { Launch = null };

        var bad = await Run(session, Cs2(), new GameProfile { Width = 10, Height = 10, HighPerformanceGpu = true, ExePath = GameExe });
        var none = await Run(session, noLaunch, new GameProfile());

        Assert.Equal(GameSessionStage.Failed, bad[^1].Stage);
        Assert.Equal(GameSessionStage.Failed, none[^1].Stage);
        Assert.Empty(shell.Started);
        Assert.Empty(registry.Values);
    }

    [Fact]
    public async Task CancellingStopsFollowingAndStillClosesTheHelpers()
    {
        var (session, shell, processes, _, journal, temp) = Create();
        using var cleanup = temp;
        using var journalScope = journal;
        using var cancel = new CancellationTokenSource();
        shell.OnStart = target => processes.Run(target == Helper ? 50 : 100, target == Helper ? Helper : GameExe);
        var progress = new SyncProgress(s =>
        {
            if (s.Stage == GameSessionStage.Running)
            {
                cancel.Cancel();
            }
        });

        var result = await session.RunAsync(Cs2(), new GameProfile { Helpers = [new HelperProgram { Path = Helper }] }, Titles, progress, cancel.Token);

        Assert.Equal(GameSessionStage.Cancelled, result.Stage);
        Assert.Equal([50], processes.Closed);
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe", true)]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive Extra\x.exe", false)]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\UnityCrashHandler64.exe", false)]
    [InlineData(@"C:\Windows\explorer.exe", false)]
    public void OnlyProcessesInsideTheGameFolderBelongToIt(string path, bool expected) =>
        Assert.Equal(expected, GameSession.BelongsToGame(new RunningProcess(1, path, null), Cs2(), new GameProfile()));

    private sealed class SyncProgress(Action<GameSessionStatus> report) : IProgress<GameSessionStatus>
    {
        public void Report(GameSessionStatus value) => report(value);
    }
}

public sealed class SteamLaunchOptionsTests
{
    private const string Config = """
        "UserLocalConfigStore"
        {
        	"Software"
        	{
        		"Valve"
        		{
        			"Steam"
        			{
        				"Apps"
        				{
        					"730"
        					{
        						"LastPlayed"		"1790043894"
        						"LaunchOptions"		"-novid"
        					}
        				}
        				"SourceModInstallPath"		"C:\\Program Files (x86)\\Steam\\steamapps\\sourcemods"
        			}
        		}
        	}
        }

        """;

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void ExistingOptionsAreReplacedInPlaceAndEverythingElseStaysTheSame()
    {
        var updated = SteamLaunchOptions.Apply(Lf(Config), "730", "-novid -high");

        Assert.Equal(Lf(Config).Replace("\"-novid\"", "\"-novid -high\"", StringComparison.Ordinal), updated);
        Assert.Equal("-novid -high", SteamLaunchOptions.Get(Vdf.Parse(updated), "730"));
    }

    [Fact]
    public void ANewGameGetsItsOwnGroupAndEmptyOptionsAreRemoved()
    {
        var added = SteamLaunchOptions.Apply(Lf(Config), "271590", "-fullscreen");
        var removed = SteamLaunchOptions.Apply(Lf(Config), "730", "  ");

        Assert.Equal("-fullscreen", SteamLaunchOptions.Get(Vdf.Parse(added), "271590"));
        Assert.Equal("-novid", SteamLaunchOptions.Get(Vdf.Parse(added), "730"));
        Assert.Null(SteamLaunchOptions.Get(Vdf.Parse(removed), "730"));
        Assert.Equal("1790043894", Vdf.Parse(removed).Child("Software")!.Child("Valve")!.Child("Steam")!.Child("Apps")!.Child("730")!.Text("LastPlayed"));
    }

    [Fact]
    public void QuotesAndBackslashesInOptionsSurviveTheRoundTrip()
    {
        var updated = SteamLaunchOptions.Apply(Lf(Config), "730", "+exec \"autoexec.cfg\" -path C:\\cfg");

        Assert.Equal("+exec \"autoexec.cfg\" -path C:\\cfg", SteamLaunchOptions.Get(Vdf.Parse(updated), "730"));
    }

    [Fact]
    public void AFileThatWouldNotComeBackIdenticalIsNeverRewritten()
    {
        Assert.Throws<FormatException>(() => SteamLaunchOptions.Apply("\"UserLocalConfigStore\" { \"a\" \"b\" }", "730", "-x"));
        Assert.Throws<FormatException>(() => SteamLaunchOptions.Apply("\"Outro\"\n{\n}\n", "730", "-x"));
    }

    [Fact]
    public void WritingKeepsTheFirstOriginalAsBackupAndNeverOverwritesIt()
    {
        using var temp = new TempFolder();
        var file = temp.At("localconfig.vdf");
        File.WriteAllText(file, Lf(Config));

        Assert.Equal(SteamOptionsResult.Written, SteamLaunchOptions.WriteFile(file, "730", "-high", NullLogger.Instance));
        Assert.Equal(SteamOptionsResult.Written, SteamLaunchOptions.WriteFile(file, "730", "-low", NullLogger.Instance));
        Assert.Equal(SteamOptionsResult.Unchanged, SteamLaunchOptions.WriteFile(file, "730", "-low", NullLogger.Instance));

        Assert.Equal(Lf(Config), File.ReadAllText(file + SteamLaunchOptions.BackupSuffix));
        Assert.Equal("-low", SteamLaunchOptions.Get(Vdf.Parse(File.ReadAllText(file)), "730"));
        Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
    }

    [Fact]
    public void ABrokenFileIsReportedAndLeftUntouched()
    {
        using var temp = new TempFolder();
        var file = temp.At("localconfig.vdf");
        File.WriteAllText(file, "\"UserLocalConfigStore\"\n{\n");

        Assert.Equal(SteamOptionsResult.UnsupportedFormat, SteamLaunchOptions.WriteFile(file, "730", "-x", NullLogger.Instance));
        Assert.Equal("\"UserLocalConfigStore\"\n{\n", File.ReadAllText(file));
        Assert.False(File.Exists(file + SteamLaunchOptions.BackupSuffix));
    }

    [Fact]
    public void WithSteamOpenNothingIsWrittenAndUnsafeOptionsAreRefused()
    {
        var processes = new ScriptedProcesses();
        processes.Run(1, @"C:\Program Files (x86)\Steam\steam.exe");
        var options = new SteamLaunchOptions(new FixedLocations(steam: @"C:\Program Files (x86)\Steam"), processes, NullLogger<SteamLaunchOptions>.Instance);

        Assert.True(options.SteamIsRunning());
        Assert.Equal(SteamOptionsResult.UnsafeOptions, options.Write("73a", "-x"));
        Assert.Equal(SteamOptionsResult.UnsafeOptions, options.Write("730", "-x\n-y"));
    }

    [Fact]
    public void TheRealLocalConfigOfThisPcComesBackIdenticalAfterReading()
    {
        var steam = new WindowsGameLocations(new WindowsRegistryAccess()).SteamFolder;
        Assert.SkipWhen(steam is null, "Steam não instalada");
        var options = new SteamLaunchOptions(new FixedLocations(steam: steam), new ScriptedProcesses(), NullLogger<SteamLaunchOptions>.Instance);
        var file = options.ConfigFile();
        Assert.SkipWhen(file is null, "sem localconfig.vdf");

        var text = Lf(File.ReadAllText(file!));
        var root = Vdf.Parse(text);

        Assert.Equal(text, Vdf.Serialize(root));
        var updated = SteamLaunchOptions.Apply(text, "730", "-m1vclock-teste");
        Assert.Equal("-m1vclock-teste", SteamLaunchOptions.Get(Vdf.Parse(updated), "730"));
        Assert.Equal(text.Length, Lf(File.ReadAllText(file!)).Length);
    }
}

public sealed class RealWindowsProfileTests
{
    [Fact]
    public void ThisTestProcessIsListedWithItsRealPathAndIsRunning()
    {
        var processes = new WindowsGameProcesses(NullLogger<WindowsGameProcesses>.Instance);

        var self = Assert.Single(processes.List(), p => p.Id == Environment.ProcessId);

        Assert.Equal(Environment.ProcessPath, self.Path, ignoreCase: true);
        Assert.NotNull(self.StartedUtc);
        Assert.True(processes.IsRunning(Environment.ProcessId));
        Assert.False(processes.IsRunning(int.MaxValue - 1));
    }

    [Fact]
    public async Task TheRealPreferencesOfAFakeExecutableAreWrittenToTheUserRegistryAndUndone()
    {
        var exe = $@"C:\m1vClock-Teste-{Guid.NewGuid():N}\jogo.exe";
        using var temp = new TempFolder();
        var registry = new WindowsRegistryAccess();
        using var journal = new ChangeJournal(new JournalStore(temp.At("journal.json"), NullLogger<JournalStore>.Instance), [new RegistryValueHandler(registry)], new NoRestorePoint(), TimeProvider.System, NullLogger<ChangeJournal>.Instance);
        try
        {
            foreach (var change in GamePreferences.ChangesFor(registry, exe, new GameProfile { HighPerformanceGpu = true, DisableFullscreenOptimizations = true }, "GPU", "Tela cheia"))
            {
                Assert.True((await journal.ApplyAsync(change.Change, TestContext.Current.CancellationToken)).Succeeded);
            }

            using (var gpu = Registry.CurrentUser.OpenSubKey(GpuPreferenceValue.KeyPath))
            using (var layers = Registry.CurrentUser.OpenSubKey(CompatibilityLayers.KeyPath))
            {
                Assert.Equal("GpuPreference=2;", gpu?.GetValue(exe));
                Assert.Equal("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", layers?.GetValue(exe));
            }

            Assert.Empty(journal.UndoAll().Failures);
        }
        finally
        {
            using var gpu = Registry.CurrentUser.OpenSubKey(GpuPreferenceValue.KeyPath, writable: true);
            gpu?.DeleteValue(exe, throwOnMissingValue: false);
            using var layers = Registry.CurrentUser.OpenSubKey(CompatibilityLayers.KeyPath, writable: true);
            layers?.DeleteValue(exe, throwOnMissingValue: false);
        }

        using var after = Registry.CurrentUser.OpenSubKey(GpuPreferenceValue.KeyPath);
        Assert.Null(after?.GetValue(exe));
    }
}
