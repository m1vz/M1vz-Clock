using M1vClock.Core.RegistryValues;
using M1vClock.Games.Library;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Games.Tests;

public sealed class GameLaunchTests
{
    [Theory]
    [InlineData("730", "steam://rungameid/730")]
    [InlineData("3240220", "steam://rungameid/3240220")]
    public void SteamLaunchesByNumericAppId(string appId, string expected)
    {
        var launch = GameLaunch.Steam(appId);

        Assert.NotNull(launch);
        Assert.Equal(expected, launch.Target);
        Assert.Null(launch.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("73a")]
    [InlineData("730/../x")]
    [InlineData("1234567890123")]
    public void SteamRefusesAnythingButDigits(string appId)
    {
        Assert.Null(GameLaunch.Steam(appId));
    }

    [Fact]
    public void EpicLaunchesByTheSafeAppNameAndRefusesAnythingElse()
    {
        Assert.Equal("com.epicgames.launcher://apps/Sugar?action=launch&silent=true", GameLaunch.Epic("Sugar")!.Target);
        Assert.Null(GameLaunch.Epic("a b"));
        Assert.Null(GameLaunch.Epic("x&y"));
        Assert.Null(GameLaunch.Epic("../x"));
        Assert.Null(GameLaunch.Epic(string.Empty));
    }

    [Fact]
    public void RiotNeedsAFullClientPathAndSafeProductAndPatchline()
    {
        var launch = GameLaunch.Riot(@"C:\Riot Games\Riot Client\RiotClientServices.exe", "valorant", "live");

        Assert.NotNull(launch);
        Assert.Equal("--launch-product=valorant --launch-patchline=live", launch.Arguments);
        Assert.Null(GameLaunch.Riot("RiotClientServices.exe", "valorant", "live"));
        Assert.Null(GameLaunch.Riot(@"C:\x.exe", "val orant", "live"));
        Assert.Null(GameLaunch.Riot(@"C:\x.exe", "valorant", "live --evil"));
    }
}

public sealed class SteamLibraryTests
{
    private static MemoryFiles ThisPc()
    {
        var files = new MemoryFiles();
        files.Add($@"{Real.SteamFolder}\steamapps\libraryfolders.vdf", Real.LibraryFolders);
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_730.acf", Real.Cs2Manifest);
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_228980.acf", Real.Manifest("228980", "Steamworks Common Redistributables", "Steamworks Shared", 4, 181249391));
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_3139440.acf", Real.Manifest("3139440", "GunZ: The Duel", "GUNZ THE DUEL", 6, 1639209630));
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_3240220.acf", Real.Manifest("3240220", "Grand Theft Auto V Enhanced", "Grand Theft Auto V Enhanced", 4, 103219693669));
        files.Add($@"{Real.SecondLibrary}\steamapps\appmanifest_730.acf", Real.Manifest("730", "Counter-Strike 2", "Counter-Strike Global Offensive", 4, 70000000000));
        files.Add($@"{Real.SecondLibrary}\steamapps\appmanifest_1422450.acf", Real.Manifest("1422450", "Deadlock", "Deadlock", 6, 39013448358));
        files.Add($@"{Real.SecondLibrary}\steamapps\appmanifest_2050650.acf", Real.Manifest("2050650", "Resident Evil 4", "RESIDENT EVIL 4  BIOHAZARD RE4", 4, 73859672758));
        files.AddDirectory($@"{Real.SteamFolder}\steamapps\common\Counter-Strike Global Offensive");
        files.AddDirectory($@"{Real.SecondLibrary}\steamapps\common\Counter-Strike Global Offensive");
        files.AddDirectory($@"{Real.SteamFolder}\steamapps\common\RESIDENT EVIL 4  BIOHAZARD RE4");
        files.AddDirectory($@"{Real.SecondLibrary}\steamapps\common\RESIDENT EVIL 4  BIOHAZARD RE4");
        return files;
    }

    private static IReadOnlyList<GameEntry> Read(MemoryFiles files) =>
        new SteamLibraryReader(files, NullLogger.Instance).Read(Real.SteamFolder);

    [Fact]
    public void BothLibrariesOfThisPcAreReadAndTheSteamworksToolIsLeftOut()
    {
        var games = Read(ThisPc());

        Assert.Equal(["1422450", "2050650", "3139440", "3240220", "730"], games.Select(g => g.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(games, g => g.Name.Contains("Redistributables", StringComparison.Ordinal));
        Assert.All(games, g => Assert.Equal(GamePlatform.Steam, g.Platform));
    }

    [Fact]
    public void ASameGameInTwoLibrariesKeepsTheOneTheListSaysHoldsIt()
    {
        var games = Read(ThisPc());

        var cs2 = Assert.Single(games, g => g.Id == "730");
        Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive", cs2.InstallFolder);
        Assert.Equal(73_922_650_178, cs2.SizeBytes);
        Assert.Equal(GameState.UpdatePending, cs2.State);
    }

    [Fact]
    public void WhenOnlyOneFolderExistsThatManifestWinsEvenIfTheListIsElsewhere()
    {
        var files = new MemoryFiles()
            .Add($@"{Real.SteamFolder}\steamapps\libraryfolders.vdf", Real.LibraryFolders)
            .Add($@"{Real.SteamFolder}\steamapps\appmanifest_730.acf", Real.Cs2Manifest)
            .Add($@"{Real.SecondLibrary}\steamapps\appmanifest_730.acf", Real.Manifest("730", "Counter-Strike 2", "Counter-Strike Global Offensive", 4, 70000000000))
            .AddDirectory($@"{Real.SecondLibrary}\steamapps\common\Counter-Strike Global Offensive");

        var cs2 = Assert.Single(Read(files));

        Assert.StartsWith(Real.SecondLibrary, cs2.InstallFolder, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GameState.Installed, cs2.State);
    }

    [Fact]
    public void TheRegistryPathInLowerCaseIsShownWithTheCaseOfTheLibraryList()
    {
        var files = ThisPc();

        var games = new SteamLibraryReader(files, NullLogger.Instance).Read(@"c:\program files (x86)\steam");

        var cs2 = Assert.Single(games, g => g.Id == "730");
        Assert.StartsWith(@"C:\Program Files (x86)\Steam\steamapps", cs2.InstallFolder, StringComparison.Ordinal);
    }

    [Fact]
    public void ARealManifestGivesSteamLaunchAndANormalGameHasNoWarning()
    {
        var games = Read(ThisPc());

        var gta = Assert.Single(games, g => g.Id == "3240220");
        Assert.Equal("steam://rungameid/3240220", gta.Launch!.Target);
        Assert.Equal(GameState.Installed, gta.State);
        Assert.True(gta.IsHealthy);
        Assert.Equal(103_219_693_669, gta.SizeBytes);
    }

    [Theory]
    [InlineData(4, GameState.Installed)]
    [InlineData(6, GameState.UpdatePending)]
    [InlineData(0, GameState.Incomplete)]
    [InlineData(1, GameState.Incomplete)]
    [InlineData(2, GameState.Incomplete)]
    [InlineData(1026, GameState.Updating)]
    [InlineData(1048580, GameState.Updating)]
    [InlineData(36, GameState.NeedsRepair)]
    [InlineData(132, GameState.NeedsRepair)]
    public void TheSteamStateFlagsBecomeAGameState(int flags, GameState expected)
    {
        Assert.Equal(expected, SteamLibraryReader.StateOf(flags));
    }

    [Fact]
    public void OnlyInstalledGamesCanBeLaunched()
    {
        var files = new MemoryFiles()
            .Add($@"{Real.SteamFolder}\steamapps\appmanifest_1.acf", Real.Manifest("1", "Pronto", "Pronto", 4, 10))
            .Add($@"{Real.SteamFolder}\steamapps\appmanifest_2.acf", Real.Manifest("2", "Baixando", "Baixando", 1048580, 10))
            .Add($@"{Real.SteamFolder}\steamapps\appmanifest_3.acf", Real.Manifest("3", "Quebrado", "Quebrado", 36, 10))
            .Add($@"{Real.SteamFolder}\steamapps\appmanifest_4.acf", Real.Manifest("4", "Incompleto", "Incompleto", 1, 10));

        var games = Read(files).ToDictionary(g => g.Name);

        Assert.NotNull(games["Pronto"].Launch);
        Assert.Null(games["Baixando"].Launch);
        Assert.Null(games["Quebrado"].Launch);
        Assert.Null(games["Incompleto"].Launch);
        Assert.False(games["Quebrado"].IsHealthy);
    }

    [Fact]
    public void ABrokenManifestIsSkippedAndTheOthersStillLoad()
    {
        var files = ThisPc();
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_999.acf", Real.Cs2Manifest[..(Real.Cs2Manifest.Length / 2)]);
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_998.acf", "\"AppState\" { \"appid\" \"abc\" \"name\" \"Sem número\" \"installdir\" \"x\" }");
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_997.acf", "\"AppState\" { \"appid\" \"997\" }");
        files.Unreadable.Add($@"{Real.SteamFolder}\steamapps\appmanifest_3139440.acf");

        var games = Read(files);

        Assert.DoesNotContain(games, g => g.Id is "999" or "998" or "997" or "3139440");
        Assert.Contains(games, g => g.Id == "730");
    }

    [Fact]
    public void WithoutALibraryListOnlyTheMainFolderIsRead()
    {
        var files = new MemoryFiles().Add($@"{Real.SteamFolder}\steamapps\appmanifest_1.acf", Real.Manifest("1", "Único", "Único", 4, 10));

        Assert.Equal(["Único"], Read(files).Select(g => g.Name));
    }

    [Fact]
    public void ABrokenLibraryListFallsBackToTheMainFolder()
    {
        var files = new MemoryFiles()
            .Add($@"{Real.SteamFolder}\steamapps\libraryfolders.vdf", "\"libraryfolders\" { \"0\" {")
            .Add($@"{Real.SteamFolder}\steamapps\appmanifest_1.acf", Real.Manifest("1", "Único", "Único", 4, 10));

        Assert.Equal(["Único"], Read(files).Select(g => g.Name));
    }

    [Fact]
    public void TheOldLibraryListFormatWithPlainPathsIsUnderstood()
    {
        var files = new MemoryFiles()
            .Add($@"{Real.SteamFolder}\steamapps\libraryfolders.vdf", "\"LibraryFolders\" { \"TimeNextStatsReport\" \"1\" \"1\" \"E:\\\\Jogos\" }")
            .Add(@"E:\Jogos\steamapps\appmanifest_5.acf", Real.Manifest("5", "No outro disco", "Outro", 4, 10));

        Assert.Equal(["No outro disco"], Read(files).Select(g => g.Name));
    }

    [Fact]
    public void ARelativeLibraryPathIsNeverFollowed()
    {
        var files = new MemoryFiles()
            .Add($@"{Real.SteamFolder}\steamapps\libraryfolders.vdf", "\"libraryfolders\" { \"0\" { \"path\" \"..\\\\..\\\\x\" } }");

        Assert.Empty(Read(files));
    }
}

public sealed class EpicAndRiotLibraryTests
{
    private static MemoryFiles Epic()
    {
        var files = new MemoryFiles();
        files.Add($@"{Real.EpicFolder}\6C41A16874B6D09128E8CA8188B41067.item", Real.Fortnite);
        files.Add($@"{Real.EpicFolder}\F3E493BC71C39FDC8A367CB47530E1C7.item", Real.RocketLeague);
        return files;
    }

    private static IReadOnlyList<GameEntry> ReadEpic(MemoryFiles files) =>
        new EpicLibraryReader(files, NullLogger.Instance).Read(Real.EpicFolder);

    [Fact]
    public void TheTwoEpicGamesOfThisPcAreReadWithTheirNamesSizesAndLaunch()
    {
        var games = ReadEpic(Epic()).OrderBy(g => g.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(["Fortnite", "Rocket League®"], games.Select(g => g.Name));
        Assert.Equal(37_704_736_207, games[0].SizeBytes);
        Assert.Equal(@"C:\Program Files\Epic Games\rocketleague", games[1].InstallFolder);
        Assert.Equal("com.epicgames.launcher://apps/Sugar?action=launch&silent=true", games[1].Launch!.Target);
        Assert.All(games, g => Assert.Equal(GamePlatform.Epic, g.Platform));
        Assert.All(games, g => Assert.Equal(GameState.Installed, g.State));
    }

    [Fact]
    public void AddonsEnginesAndDlcAreNotListedAsGames()
    {
        var files = Epic();
        files.Add($@"{Real.EpicFolder}\A.item", """{"DisplayName":"Pacote","AppName":"Pacote","MainGameAppName":"Fortnite","AppCategories":["games","addons"]}""");
        files.Add($@"{Real.EpicFolder}\B.item", """{"DisplayName":"Unreal Engine","AppName":"UE","AppCategories":["engines"]}""");
        files.Add($@"{Real.EpicFolder}\C.item", """{"DisplayName":"Sem categorias","AppName":"X"}""");

        Assert.Equal(2, ReadEpic(files).Count);
    }

    [Fact]
    public void AnIncompleteInstallIsFlaggedAndNotLaunchable()
    {
        var files = new MemoryFiles().Add($@"{Real.EpicFolder}\A.item", """{"DisplayName":"Baixando","AppName":"Baixando","bIsIncompleteInstall":true,"AppCategories":["games"]}""");

        var game = Assert.Single(ReadEpic(files));

        Assert.Equal(GameState.Incomplete, game.State);
        Assert.Null(game.Launch);
        Assert.False(game.IsHealthy);
    }

    [Fact]
    public void AnUnsafeAppNameGivesNoLaunch()
    {
        var files = new MemoryFiles().Add($@"{Real.EpicFolder}\A.item", """{"DisplayName":"Estranho","AppName":"a b&c","AppCategories":["games"]}""");

        Assert.Null(Assert.Single(ReadEpic(files)).Launch);
    }

    [Fact]
    public void BrokenOrUnreadableEpicFilesAreSkipped()
    {
        var files = Epic();
        files.Add($@"{Real.EpicFolder}\D.item", "{ isso não é json");
        files.Add($@"{Real.EpicFolder}\E.item", "[1,2,3]");
        files.Add($@"{Real.EpicFolder}\F.item", "{}");
        files.Unreadable.Add($@"{Real.EpicFolder}\6C41A16874B6D09128E8CA8188B41067.item");

        Assert.Equal(["Rocket League®"], ReadEpic(files).Select(g => g.Name));
    }

    private static MemoryFiles Riot() => new MemoryFiles()
        .Add(Real.RiotFile, Real.RiotInstalls)
        .Add(@"C:\Riot Games\Riot Client\RiotClientServices.exe", "exe")
        .AddDirectory(@"C:\Riot Games\VALORANT\live");

    private static IReadOnlyList<GameEntry> ReadRiot(MemoryFiles files) =>
        new RiotLibraryReader(files, NullLogger.Instance).Read(Real.RiotFile);

    [Fact]
    public void ValorantOfThisPcIsReadAndLaunchedThroughTheRiotClient()
    {
        var game = Assert.Single(ReadRiot(Riot()));

        Assert.Equal("VALORANT", game.Name);
        Assert.Equal(GamePlatform.Riot, game.Platform);
        Assert.Equal(@"C:\Riot Games\VALORANT\live", game.InstallFolder);
        Assert.Null(game.SizeBytes);
        Assert.Equal(@"C:\Riot Games\Riot Client\RiotClientServices.exe", game.Launch!.Target);
        Assert.Equal("--launch-product=valorant --launch-patchline=live", game.Launch.Arguments);
    }

    [Fact]
    public void AnInstallThatNoLongerExistsOrAClientThatIsGoneIsHandledSafely()
    {
        var withoutFolder = new MemoryFiles().Add(Real.RiotFile, Real.RiotInstalls).Add(@"C:\Riot Games\Riot Client\RiotClientServices.exe", "exe");
        var withoutClient = new MemoryFiles().Add(Real.RiotFile, Real.RiotInstalls).AddDirectory(@"C:\Riot Games\VALORANT\live");

        Assert.Empty(ReadRiot(withoutFolder));
        Assert.Null(Assert.Single(ReadRiot(withoutClient)).Launch);
    }

    [Fact]
    public void AnUnknownRiotProductIsListedButNotLaunchable()
    {
        var files = new MemoryFiles()
            .Add(Real.RiotFile, """{"associated_client":{"C:/Riot Games/Novo Jogo/live/":"C:/Riot Games/Riot Client/RiotClientServices.exe"}}""")
            .Add(@"C:\Riot Games\Riot Client\RiotClientServices.exe", "exe")
            .AddDirectory(@"C:\Riot Games\Novo Jogo\live");

        var game = Assert.Single(ReadRiot(files));

        Assert.Equal("Novo Jogo", game.Name);
        Assert.Null(game.Launch);
    }

    [Theory]
    [InlineData("{ não é json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"associated_client":[]}""")]
    [InlineData("""{"associated_client":{"C:/x/live/":5}}""")]
    public void BrokenRiotFilesGiveNoGames(string text)
    {
        var files = new MemoryFiles().Add(Real.RiotFile, text);

        Assert.Empty(ReadRiot(files));
    }
}

public sealed class GameLibraryServiceTests
{
    private static MemoryFiles Everything()
    {
        var files = new MemoryFiles();
        files.Add($@"{Real.SteamFolder}\steamapps\libraryfolders.vdf", Real.LibraryFolders);
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_3240220.acf", Real.Manifest("3240220", "Grand Theft Auto V Enhanced", "GTA", 4, 103219693669));
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_3139440.acf", Real.Manifest("3139440", "GunZ: The Duel", "GunZ", 6, 1639209630));
        files.Add($@"{Real.EpicFolder}\A.item", Real.Fortnite);
        files.Add(Real.RiotFile, Real.RiotInstalls);
        files.Add(@"C:\Riot Games\Riot Client\RiotClientServices.exe", "exe");
        files.AddDirectory(@"C:\Riot Games\VALORANT\live");
        return files;
    }

    private static GameLibraryService Create(MemoryFiles files, IGameLocations? locations = null) =>
        new(files, locations ?? new FixedLocations(Real.SteamFolder, Real.EpicFolder, Real.RiotFile), NullLogger<GameLibraryService>.Instance);

    [Fact]
    public async Task AllPlatformsAreCombinedAndSortedByName()
    {
        var library = await Create(Everything()).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Fortnite", "Grand Theft Auto V Enhanced", "GunZ: The Duel", "VALORANT"], library.Games.Select(g => g.Name));
        Assert.Empty(library.Failed);
        Assert.Equal(3, library.Platforms);
        Assert.Equal(103_219_693_669 + 1_639_209_630 + 37_704_736_207, library.KnownBytes);
        Assert.Equal(0, library.NeedAttention);
    }

    [Fact]
    public async Task TheLibraryRemembersWhichInstallFoldersReallyExist()
    {
        var files = Everything();
        files.AddDirectory(@"C:\Program Files (x86)\Steam\steamapps\common\GTA");

        var library = await Create(files).ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(library.Games.Single(g => g.Id == "3240220").FolderExists);
        Assert.False(library.Games.Single(g => g.Id == "3139440").FolderExists);
        Assert.True(library.Games.Single(g => g.Platform == GamePlatform.Riot).FolderExists);
        Assert.False(library.Games.Single(g => g.Platform == GamePlatform.Epic).FolderExists);
    }

    [Fact]
    public async Task APlatformThatCannotBeReadIsReportedAndTheOthersStillLoad()
    {
        var files = Everything();
        files.ThrowOnEnumerate = Real.EpicFolder;

        var library = await Create(files).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal([GamePlatform.Epic], library.Failed);
        Assert.DoesNotContain(library.Games, g => g.Platform == GamePlatform.Epic);
        Assert.Contains(library.Games, g => g.Platform == GamePlatform.Steam);
        Assert.Contains(library.Games, g => g.Platform == GamePlatform.Riot);
    }

    [Fact]
    public async Task PlatformsThatAreNotInstalledAreSimplySkipped()
    {
        var library = await Create(Everything(), new FixedLocations(steam: Real.SteamFolder)).ReadAsync(TestContext.Current.CancellationToken);

        Assert.All(library.Games, g => Assert.Equal(GamePlatform.Steam, g.Platform));
        Assert.Empty(library.Failed);
        Assert.Empty((await Create(new MemoryFiles(), new FixedLocations()).ReadAsync(TestContext.Current.CancellationToken)).Games);
    }

    [Fact]
    public async Task AGameNeedingAttentionIsCounted()
    {
        var files = Everything();
        files.Add($@"{Real.SteamFolder}\steamapps\appmanifest_5.acf", Real.Manifest("5", "Quebrado", "Q", 36, 10));

        var library = await Create(files).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, library.NeedAttention);
    }

    [Fact]
    public async Task SortingBySizePutsTheBiggestFirstAndUnknownSizesLast()
    {
        var library = await Create(Everything()).ReadAsync(TestContext.Current.CancellationToken);

        var bySize = GameLibraryService.Sort(library.Games, GameSort.Size);

        Assert.Equal(["Grand Theft Auto V Enhanced", "Fortnite", "GunZ: The Duel", "VALORANT"], bySize.Select(g => g.Name));
    }
}

public sealed class RealGameLibraryTests
{
    [Fact]
    public async Task ThisPcLibraryIsReadForRealWithoutDuplicatesOrBlankNames()
    {
        var service = new GameLibraryService(new WindowsGameFileSystem(), new WindowsGameLocations(new WindowsRegistryAccess()), NullLogger<GameLibraryService>.Instance);

        var library = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(library.Failed);
        Assert.All(library.Games, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
        Assert.Equal(library.Games.Count, library.Games.Select(g => (g.Platform, g.Id)).Distinct().Count());
        Assert.DoesNotContain(library.Games, g => g.Name == "Steamworks Common Redistributables");
        Assert.All(library.Games.Where(g => g.Platform == GamePlatform.Steam), g => Assert.True(g.Id.All(char.IsAsciiDigit)));
        Assert.All(library.Games.Where(g => g.Launch is not null), g => Assert.False(string.IsNullOrWhiteSpace(g.Launch!.Target)));
    }

    [Fact]
    public void TheLocationsOfThisPcPointToRealFoldersOrNothing()
    {
        var locations = new WindowsGameLocations(new WindowsRegistryAccess());

        Assert.True(locations.SteamFolder is null || Directory.Exists(locations.SteamFolder));
        Assert.True(locations.EpicManifestsFolder is null || Directory.Exists(locations.EpicManifestsFolder));
        Assert.True(locations.RiotInstallsFile is null || File.Exists(locations.RiotInstallsFile));
    }
}
