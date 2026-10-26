using M1vClock.Games.Library;
using M1vClock.Games.Profiles;
using M1vClock.Games.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Games.Tests;

public sealed class GogAndUbisoftLibraryTests
{
    private const string WitcherKey = GogLibraryReader.GamesKey + @"\1207664663";
    private const string WitcherFolder = @"C:\GOG Games\The Witcher 3 Wild Hunt GOTY";
    private const string WitcherExe = WitcherFolder + @"\bin\x64\witcher3.exe";
    private const string AnnoKey = UbisoftLibraryReader.InstallsKey + @"\4553";

    private static (MemoryRegistry Registry, MemoryFiles Files) Gog()
    {
        var registry = new MemoryRegistry()
            .Set(WitcherKey, "gameName", "The Witcher 3: Wild Hunt - Game of the Year Edition")
            .Set(WitcherKey, "path", WitcherFolder)
            .Set(WitcherKey, "exe", WitcherExe)
            .Set(WitcherKey, "launchParam", "-net");
        var files = new MemoryFiles().Add(WitcherExe, "exe");
        return (registry, files);
    }

    [Fact]
    public void AGogGameIsReadFromTheRegistryAndLaunchedByItsOwnExecutable()
    {
        var (registry, files) = Gog();

        var game = Assert.Single(new GogLibraryReader(registry, files, NullLogger.Instance).Read());

        Assert.Equal("gog:1207664663", game.Id);
        Assert.Equal(GamePlatform.Gog, game.Platform);
        Assert.Equal(WitcherFolder, game.InstallFolder);
        Assert.Equal(new GameLaunch(WitcherExe, "-net"), game.Launch);
    }

    [Fact]
    public void AGogGameWhoseExecutableIsGoneIsListedButNotLaunchable()
    {
        var (registry, _) = Gog();
        var files = new MemoryFiles().AddDirectory(WitcherFolder);

        var game = Assert.Single(new GogLibraryReader(registry, files, NullLogger.Instance).Read());

        Assert.Null(game.Launch);
    }

    [Theory]
    [InlineData(@"bin\witcher3.exe")]
    [InlineData(@"\\servidor\jogos\witcher3.exe")]
    [InlineData(@"C:\GOG Games\The Witcher 3 Wild Hunt GOTY\bin\x64\witcher3.bat")]
    public void AGogExecutableThatIsRelativeRemoteOrNotAnExeIsNeverLaunched(string exe)
    {
        var (registry, files) = Gog();
        registry.Set(WitcherKey, "exe", exe);
        if (Path.IsPathFullyQualified(exe) && !exe.StartsWith('\\'))
        {
            files.Add(exe, "x");
        }

        var game = Assert.Single(new GogLibraryReader(registry, files, NullLogger.Instance).Read());

        Assert.Null(game.Launch);
    }

    [Fact]
    public void AGogEntryWithoutANameOrWithAFolderThatIsGoneIsSkipped()
    {
        var registry = new MemoryRegistry()
            .Set(GogLibraryReader.GamesKey + @"\1", "path", WitcherFolder)
            .Set(GogLibraryReader.GamesKey + @"\2", "gameName", "Sumiu")
            .Set(GogLibraryReader.GamesKey + @"\2", "path", @"C:\GOG Games\Sumiu");

        Assert.Empty(new GogLibraryReader(registry, new MemoryFiles().AddDirectory(WitcherFolder), NullLogger.Instance).Read());
    }

    [Fact]
    public void AnUbisoftGameTakesItsNameFromTheUninstallEntryAndLaunchesByItsNumber()
    {
        var registry = new MemoryRegistry()
            .Set(AnnoKey, "InstallDir", "C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Anno 1800/")
            .Set(UbisoftLibraryReader.UninstallKey + "4553", "DisplayName", "Anno 1800");
        var files = new MemoryFiles().AddDirectory(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Anno 1800");

        var game = Assert.Single(new UbisoftLibraryReader(registry, files, NullLogger.Instance).Read());

        Assert.Equal("Anno 1800", game.Name);
        Assert.Equal(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Anno 1800", game.InstallFolder);
        Assert.Equal("uplay://launch/4553/0", game.Launch!.Target);
    }

    [Fact]
    public void WithoutAnUninstallEntryTheUbisoftFolderNameIsUsed()
    {
        var registry = new MemoryRegistry().Set(AnnoKey, "InstallDir", @"D:\Jogos\Anno 1800");

        var game = Assert.Single(new UbisoftLibraryReader(registry, new MemoryFiles().AddDirectory(@"D:\Jogos\Anno 1800"), NullLogger.Instance).Read());

        Assert.Equal("Anno 1800", game.Name);
    }

    [Theory]
    [InlineData("4553", "uplay://launch/4553/0")]
    [InlineData("45a3", null)]
    [InlineData("1/../2", null)]
    public void UbisoftLaunchOnlyAcceptsDigits(string id, string? expected) =>
        Assert.Equal(expected, GameLaunch.Ubisoft(id)?.Target);

    [Fact]
    public async Task ARegistryThatDeniesAccessIsReportedAndTheOtherStoresStillLoad()
    {
        var files = new MemoryFiles().Add(Real.RiotFile, Real.RiotInstalls).Add(@"C:\Riot Games\Riot Client\RiotClientServices.exe", "exe").AddDirectory(@"C:\Riot Games\VALORANT\live");
        var service = new GameLibraryService(files, new FixedLocations(riot: Real.RiotFile), new MemoryRegistry { Denied = true }, new MemoryGameSettings(), NullLogger<GameLibraryService>.Instance);

        var library = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal([GamePlatform.Gog, GamePlatform.Ubisoft], library.Failed);
        Assert.Contains(library.Games, g => g.Platform == GamePlatform.Riot);
    }
}

public sealed class ManualGameTests
{
    private const string Exe = @"D:\Jogos\Minecraft\MinecraftLauncher.exe";

    [Fact]
    public async Task AManualGameAppearsInTheLibraryAndOpensItsExecutableWithItsArguments()
    {
        var settings = new MemoryGameSettings();
        var id = Guid.NewGuid();
        settings.Update(s => s.ManualGames.Add(new ManualGame { Id = id, Name = "Minecraft", ExePath = Exe, Arguments = "--workDir D:\\mc" }));
        var files = new MemoryFiles().Add(Exe, "exe");
        var service = new GameLibraryService(files, new FixedLocations(), new MemoryRegistry(), settings, NullLogger<GameLibraryService>.Instance);

        var game = Assert.Single((await service.ReadAsync(TestContext.Current.CancellationToken)).Games);

        Assert.Equal(GamePlatform.Manual, game.Platform);
        Assert.Equal("Minecraft", game.Name);
        Assert.Equal(new GameLaunch(Exe, "--workDir D:\\mc"), game.Launch);
        Assert.Equal(id, ManualGames.IdOf(game));
        Assert.True(game.FolderExists);
    }

    [Fact]
    public void AManualGameWhoseFileWasDeletedIsFlaggedAndNotLaunchable()
    {
        var game = Assert.Single(ManualGames.Entries([new ManualGame { ExePath = Exe }], new MemoryFiles()));

        Assert.Equal(GameState.Incomplete, game.State);
        Assert.Null(game.Launch);
        Assert.Equal("MinecraftLauncher", game.Name);
    }

    [Fact]
    public void ABrokenManualEntryFromAnEditedFileIsIgnored()
    {
        var entries = ManualGames.Entries([new ManualGame { ExePath = "jogo.exe" }, new ManualGame { ExePath = @"C:\x\script.cmd" }], new MemoryFiles());

        Assert.Empty(entries);
    }

    [Fact]
    public void AddingChecksTheFileTheArgumentsAndDuplicates()
    {
        var files = new MemoryFiles().Add(Exe, "exe");
        var existing = new List<ManualGame> { new() { ExePath = Exe.ToUpperInvariant() } };

        Assert.Equal(ManualGameProblem.NotAnExecutable, ManualGames.Check([], @"C:\x\jogo.lnk", null, files));
        Assert.Equal(ManualGameProblem.Missing, ManualGames.Check([], @"C:\x\jogo.exe", null, files));
        Assert.Equal(ManualGameProblem.UnsafeArguments, ManualGames.Check([], Exe, "-a\n-b", files));
        Assert.Equal(ManualGameProblem.UnsafeArguments, ManualGames.Check([], Exe, new string('a', GameLaunch.MaximumArguments + 1), files));
        Assert.Equal(ManualGameProblem.AlreadyAdded, ManualGames.Check(existing, Exe, null, files));
        Assert.Equal(ManualGameProblem.None, ManualGames.Check([], Exe, "-dev", files));
    }

    [Fact]
    public void ALongNameIsCutAndABlankNameUsesTheFileName()
    {
        Assert.Equal(ManualGames.MaximumName, ManualGames.NameFor(new string('x', 500), Exe).Length);
        Assert.Equal("MinecraftLauncher", ManualGames.NameFor("   ", Exe));
    }

    [Fact]
    public async Task ManualGamesAreSavedToDiskAndReadBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "m1vClock-games-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(folder, "games.json");
            var store = new M1vClock.Core.Settings.SettingsStore<GameSettings>(file, GameSettings.CurrentSchemaVersion, [], NullLogger.Instance);
            var first = new GameSettingsService(store, NullLogger<GameSettingsService>.Instance);
            first.Update(s =>
            {
                s.ManualGames.Add(new ManualGame { Name = "Minecraft", ExePath = Exe });
                s.Profiles["Steam:730"] = new GameProfile { Engine = GameEngine.Source, Width = 1280, Height = 960, Priority = GamePriority.High };
                s.Overlay.Items = ["fps"];
            });

            var second = new GameSettingsService(new M1vClock.Core.Settings.SettingsStore<GameSettings>(file, GameSettings.CurrentSchemaVersion, [], NullLogger.Instance), NullLogger<GameSettingsService>.Instance);

            Assert.Equal("Minecraft", second.Read(s => s.ManualGames.Single().Name));
            Assert.Equal(GamePriority.High, second.Read(s => s.Profiles["Steam:730"].Priority));
            Assert.Equal(["fps"], second.Read(s => s.Overlay.Items));
            Assert.Contains("\"High\"", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}

public sealed class LaunchArgumentTests
{
    private static GameProfile Profile(GameEngine engine, WindowMode mode, int width = 0, int height = 0, int hz = 0, string extra = "") =>
        new() { Engine = engine, WindowMode = mode, Width = width, Height = height, RefreshRate = hz, Extra = extra };

    [Theory]
    [InlineData(WindowMode.Fullscreen, "-fullscreen -w 1920 -h 1080 -freq 240")]
    [InlineData(WindowMode.Windowed, "-windowed -w 1920 -h 1080 -freq 240")]
    [InlineData(WindowMode.Borderless, "-windowed -noborder -w 1920 -h 1080 -freq 240")]
    [InlineData(WindowMode.Default, "-w 1920 -h 1080 -freq 240")]
    public void SourceGamesGetSourceStyleArguments(WindowMode mode, string expected) =>
        Assert.Equal(expected, LaunchArguments.Build(Profile(GameEngine.Source, mode, 1920, 1080, 240)));

    [Theory]
    [InlineData(WindowMode.Fullscreen, "-screen-fullscreen 1 -window-mode exclusive -screen-width 2560 -screen-height 1440")]
    [InlineData(WindowMode.Windowed, "-screen-fullscreen 0 -window-mode windowed -screen-width 2560 -screen-height 1440")]
    [InlineData(WindowMode.Borderless, "-popupwindow -window-mode borderless -screen-width 2560 -screen-height 1440")]
    public void UnityGamesGetUnityStyleArgumentsAndNoRefreshFlag(WindowMode mode, string expected) =>
        Assert.Equal(expected, LaunchArguments.Build(Profile(GameEngine.Unity, mode, 2560, 1440, 144)));

    [Theory]
    [InlineData(WindowMode.Fullscreen, "-fullscreen -ResX=1280 -ResY=720")]
    [InlineData(WindowMode.Borderless, "-windowed -ResX=1280 -ResY=720")]
    public void UnrealGamesGetUnrealStyleArguments(WindowMode mode, string expected) =>
        Assert.Equal(expected, LaunchArguments.Build(Profile(GameEngine.Unreal, mode, 1280, 720)));

    [Fact]
    public void AnUnknownEngineOnlyGetsTheExtraArgumentsTrimmed()
    {
        Assert.Equal("-novid -high", LaunchArguments.Build(Profile(GameEngine.Unknown, WindowMode.Fullscreen, 1920, 1080, 144, "  -novid -high ")));
        Assert.False(LaunchArguments.UsesEngineOptions(Profile(GameEngine.Unknown, WindowMode.Fullscreen)));
        Assert.True(LaunchArguments.UsesEngineOptions(Profile(GameEngine.Unity, WindowMode.Fullscreen)));
    }

    [Fact]
    public void ExtraArgumentsComeAfterTheEngineOnes() =>
        Assert.Equal("-fullscreen -novid", LaunchArguments.Build(Profile(GameEngine.Source, WindowMode.Fullscreen, extra: "-novid")));

    [Theory]
    [InlineData(1920, 0, 0, "", ProfileProblem.ResolutionOutOfRange)]
    [InlineData(100, 100, 0, "", ProfileProblem.ResolutionOutOfRange)]
    [InlineData(20000, 1080, 0, "", ProfileProblem.ResolutionOutOfRange)]
    [InlineData(1920, 1080, 5, "", ProfileProblem.RefreshOutOfRange)]
    [InlineData(1920, 1080, 5000, "", ProfileProblem.RefreshOutOfRange)]
    [InlineData(0, 0, 0, "-a\r\n-b", ProfileProblem.UnsafeExtra)]
    [InlineData(1920, 1080, 360, "-novid", ProfileProblem.None)]
    public void ProfilesAreValidatedBeforeAnythingIsBuilt(int width, int height, int hz, string extra, ProfileProblem expected)
    {
        var profile = Profile(GameEngine.Source, WindowMode.Default, width, height, hz, extra);

        Assert.Equal(expected, LaunchArguments.Validate(profile));
        if (expected != ProfileProblem.None)
        {
            Assert.Throws<ArgumentException>(() => LaunchArguments.Build(profile));
        }
    }

    [Fact]
    public void AnExecutableOrHelperThatIsNotALocalExeIsRefused()
    {
        Assert.Equal(ProfileProblem.BadExecutable, LaunchArguments.Validate(new GameProfile { ExePath = @"\\pc\share\jogo.exe" }));
        Assert.Equal(ProfileProblem.BadHelper, LaunchArguments.Validate(new GameProfile { Helpers = [new HelperProgram { Path = @"C:\x\a.ps1" }] }));
        Assert.Equal(ProfileProblem.None, LaunchArguments.Validate(new GameProfile { Helpers = [new HelperProgram { Path = @"C:\x\a.exe" }] }));
    }

    [Fact]
    public void ProfileKeysCombinePlatformAndIdAndAnEmptyProfileIsRecognized()
    {
        var game = new GameEntry("730", "Counter-Strike 2", GamePlatform.Steam, null, null, GameState.Installed, null);

        Assert.Equal("Steam:730", GameProfile.KeyOf(game));
        Assert.True(new GameProfile().IsEmpty);
        Assert.False(new GameProfile { Extra = "-novid" }.IsEmpty);
    }

    [Fact]
    public void ACopyDoesNotShareHelpersWithTheOriginal()
    {
        var original = new GameProfile { Helpers = [new HelperProgram { Path = @"C:\a.exe" }] };

        var copy = original.Copy();
        copy.Helpers[0].Path = @"C:\b.exe";

        Assert.Equal(@"C:\a.exe", original.Helpers[0].Path);
    }
}

public sealed class EngineDetectorTests
{
    [Fact]
    public void TheMarkersOfTheGamesOnThisPcAreRecognized()
    {
        var files = new MemoryFiles()
            .Add(@"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\engine2.dll", "dll")
            .Add(@"C:\Program Files (x86)\Steam\steamapps\common\TaskbarHero\UnityPlayer.dll", "dll")
            .Add(@"D:\SteamLibrary\steamapps\common\Kandidatos\Kandidatos\UnityPlayer.dll", "dll")
            .AddDirectory(@"D:\SteamLibrary\steamapps\common\PUBG\Engine\Binaries")
            .AddDirectory(@"D:\SteamLibrary\steamapps\common\Grand Theft Auto V");

        Assert.Equal(GameEngine.Source, EngineDetector.Detect(files, @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"));
        Assert.Equal(GameEngine.Unity, EngineDetector.Detect(files, @"C:\Program Files (x86)\Steam\steamapps\common\TaskbarHero"));
        Assert.Equal(GameEngine.Unity, EngineDetector.Detect(files, @"D:\SteamLibrary\steamapps\common\Kandidatos"));
        Assert.Equal(GameEngine.Unreal, EngineDetector.Detect(files, @"D:\SteamLibrary\steamapps\common\PUBG"));
        Assert.Equal(GameEngine.Unknown, EngineDetector.Detect(files, @"D:\SteamLibrary\steamapps\common\Grand Theft Auto V"));
        Assert.Equal(GameEngine.Unknown, EngineDetector.Detect(files, @"D:\nao-existe"));
        Assert.Equal(GameEngine.Unknown, EngineDetector.Detect(files, null));
    }

    [Theory]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive", GameEngine.Source)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Deadlock", GameEngine.Source)]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\TaskbarHero", GameEngine.Unity)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Kandidatos", GameEngine.Unity)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\PUBG", GameEngine.Unreal)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\FPSAimTrainer", GameEngine.Unreal)]
    public void TheRealFoldersOfThisPcAreDetected(string folder, GameEngine expected)
    {
        Assert.SkipUnless(Directory.Exists(folder), "jogo não instalado neste PC");

        Assert.Equal(expected, EngineDetector.Detect(new WindowsGameFileSystem(), folder));
    }
}
