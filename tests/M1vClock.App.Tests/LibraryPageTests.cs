using System.ComponentModel;
using System.IO;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Pages.Games;
using M1vClock.Core.Shell;
using M1vClock.Games.Library;

namespace M1vClock.App.Tests;

public sealed class LibraryPageViewModelTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private sealed class StubLibrary(GameLibrary library) : IGameLibraryService
    {
        public GameLibrary Library { get; set; } = library;

        public Task<GameLibrary> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Library);
    }

    private sealed class RecordingShell : IShellLauncher
    {
        public List<(string Target, string? Arguments, string? Directory)> Started { get; } = [];

        public List<string> Folders { get; } = [];

        public Exception? Failure { get; set; }

        public void OpenUrl(Uri url) => throw new NotSupportedException();

        public void OpenFolder(string path)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Folders.Add(path);
        }

        public void Start(string fileName, string? arguments, string? workingDirectory)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Started.Add((fileName, arguments, workingDirectory));
        }
    }

    private static GameEntry Steam(string id, string name, long? size, GameState state = GameState.Installed, bool folder = true) =>
        new(id, name, GamePlatform.Steam, $@"C:\Jogos\{name}", size, state, state is GameState.Installed or GameState.UpdatePending ? GameLaunch.Steam(id) : null) { FolderExists = folder };

    private static GameLibrary ThisPc() => new(
    [
        Steam("730", "Counter-Strike 2", 70 * Gb, GameState.UpdatePending),
        Steam("3240220", "Grand Theft Auto V Enhanced", 96 * Gb),
        Steam("5", "Quebrado", 2 * Gb, GameState.NeedsRepair),
        new GameEntry("Fortnite", "Fortnite", GamePlatform.Epic, @"C:\Program Files\Epic Games\Fortnite", 35 * Gb, GameState.Installed, GameLaunch.Epic("Fortnite")) { FolderExists = true },
        new GameEntry("VALORANT/live", "VALORANT", GamePlatform.Riot, @"C:\Riot Games\VALORANT\live", null, GameState.Installed,
            GameLaunch.Riot(@"C:\Riot Games\Riot Client\RiotClientServices.exe", "valorant", "live")) { FolderExists = true },
    ],
    []);

    private static LibraryPageViewModel Create(StubLibrary library, RecordingShell? shell = null, GameTestRig? rig = null) =>
        new((rig ?? new GameTestRig()).Services(library, shell ?? new RecordingShell()), new CollectingLogger<LibraryPageViewModel>());

    [Fact]
    public async Task TheSummaryAndTheRowsComeFromTheLibraryAndStartSortedByName()
    {
        var page = Create(new StubLibrary(ThisPc()));

        await page.RefreshAsync();

        Assert.Equal("5", page.TotalText);
        Assert.Equal("3", page.PlatformsText);
        Assert.Equal("1", page.AttentionText);
        Assert.Equal("203 GB", page.SizeText);
        Assert.Equal(["Counter-Strike 2", "Fortnite", "Grand Theft Auto V Enhanced", "Quebrado", "VALORANT"], page.Items.Select(r => r.Title));
        Assert.False(page.IsLoading);
        Assert.False(page.IsEmpty);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task ARowShowsPlatformSizeFolderStateAndToneForEachKindOfGame()
    {
        var page = Create(new StubLibrary(ThisPc()));
        await page.RefreshAsync();

        var cs2 = page.Items.Single(r => r.Title == "Counter-Strike 2");
        var broken = page.Items.Single(r => r.Title == "Quebrado");
        var valorant = page.Items.Single(r => r.Title == "VALORANT");

        Assert.Equal(@"Steam · 70 GB · C:\Jogos\Counter-Strike 2", cs2.Meta);
        Assert.Equal("Atualização pendente", cs2.StateText);
        Assert.Equal(MetricState.Neutral, cs2.Tone);
        Assert.Equal("Precisa de reparo", broken.StateText);
        Assert.Equal(MetricState.Critical, broken.Tone);
        Assert.Equal(@"Riot Games · tamanho não informado · C:\Riot Games\VALORANT\live", valorant.Meta);
        Assert.Equal(MetricState.Good, valorant.Tone);
        Assert.Equal("Counter-Strike 2, Steam, Atualização pendente", cs2.AccessibleName);
    }

    [Fact]
    public async Task ThePlatformFilterTheSortAndTheSearchNarrowTheRows()
    {
        var page = Create(new StubLibrary(ThisPc()));
        await page.RefreshAsync();

        page.PlatformIndex = 1;
        Assert.Equal(["Counter-Strike 2", "Grand Theft Auto V Enhanced", "Quebrado"], page.Items.Select(r => r.Title));

        page.SortIndex = 1;
        Assert.Equal(["Grand Theft Auto V Enhanced", "Counter-Strike 2", "Quebrado"], page.Items.Select(r => r.Title));

        page.SearchText = "grand";
        Assert.Equal(["Grand Theft Auto V Enhanced"], page.Items.Select(r => r.Title));

        page.SearchText = "nada disso";
        Assert.Empty(page.Items);
        Assert.True(page.IsEmpty);

        page.SearchText = string.Empty;
        page.PlatformIndex = 3;
        Assert.Equal(["VALORANT"], page.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task PlayingASteamGameAsksSteamAndSaysSo()
    {
        var shell = new RecordingShell();
        var page = Create(new StubLibrary(ThisPc()), shell);
        await page.RefreshAsync();

        page.Play(page.Items.Single(r => r.Title == "Counter-Strike 2"));

        var started = Assert.Single(shell.Started);
        Assert.Equal("steam://rungameid/730", started.Target);
        Assert.Null(started.Arguments);
        Assert.Null(started.Directory);
        Assert.Equal("Pedido enviado ao Steam para abrir Counter-Strike 2.", page.Notice);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task PlayingTheRiotGameStartsTheClientWithTheProductArgumentsInItsOwnFolder()
    {
        var shell = new RecordingShell();
        var page = Create(new StubLibrary(ThisPc()), shell);
        await page.RefreshAsync();

        page.Play(page.Items.Single(r => r.Title == "VALORANT"));

        var started = Assert.Single(shell.Started);
        Assert.Equal(@"C:\Riot Games\Riot Client\RiotClientServices.exe", started.Target);
        Assert.Equal("--launch-product=valorant --launch-patchline=live", started.Arguments);
        Assert.Equal(@"C:\Riot Games\Riot Client", started.Directory);
        Assert.Equal("Pedido enviado ao Riot Games para abrir VALORANT.", page.Notice);
    }

    [Fact]
    public async Task AGameThatIsNotReadyHasPlayOffAndNothingIsStarted()
    {
        var shell = new RecordingShell();
        var page = Create(new StubLibrary(ThisPc()), shell);
        await page.RefreshAsync();
        var broken = page.Items.Single(r => r.Title == "Quebrado");

        page.Play(broken);
        page.Play(null);

        Assert.False(broken.CanPlay);
        Assert.Equal("Este jogo não está pronto para abrir agora.", broken.PlayToolTip);
        Assert.Empty(shell.Started);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task WindowsRefusingToOpenTheGameShowsTheReasonAndNoSuccess()
    {
        var shell = new RecordingShell { Failure = new Win32Exception("O sistema não pode encontrar o arquivo") };
        var page = Create(new StubLibrary(ThisPc()), shell);
        await page.RefreshAsync();

        page.Play(page.Items.Single(r => r.Title == "Fortnite"));

        Assert.Equal("Não foi possível abrir Fortnite: O sistema não pode encontrar o arquivo", page.Problem);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task OpenFolderOpensTheInstallFolderAndIsOffWhenTheFolderIsGone()
    {
        var shell = new RecordingShell();
        var library = new GameLibrary([Steam("1", "Existe", Gb), Steam("2", "Sumiu", Gb, folder: false)], []);
        var page = Create(new StubLibrary(library), shell);
        await page.RefreshAsync();

        page.OpenFolder(page.Items.Single(r => r.Title == "Existe"));
        var gone = page.Items.Single(r => r.Title == "Sumiu");
        page.OpenFolder(gone);

        Assert.Equal([@"C:\Jogos\Existe"], shell.Folders);
        Assert.False(gone.CanOpenFolder);
        Assert.Equal("A pasta deste jogo não foi encontrada.", gone.FolderToolTip);
    }

    [Fact]
    public async Task AFolderThatCannotBeOpenedShowsTheReason()
    {
        var shell = new RecordingShell { Failure = new DirectoryNotFoundException("Pasta não existe") };
        var page = Create(new StubLibrary(ThisPc()), shell);
        await page.RefreshAsync();

        page.OpenFolder(page.Items.Single(r => r.Title == "Counter-Strike 2"));

        Assert.Equal("Não foi possível abrir a pasta de Counter-Strike 2: Pasta não existe", page.Problem);
    }

    [Fact]
    public async Task APlatformThatFailedToReadIsReportedWhileTheOthersAreStillListed()
    {
        var library = new GameLibrary([Steam("1", "Um", Gb)], [GamePlatform.Epic]);
        var page = Create(new StubLibrary(library));

        await page.RefreshAsync();

        Assert.Equal("Não foi possível ler a biblioteca do Epic Games.", page.Problem);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task AnEmptyLibraryShowsTheEmptyStateAndZeros()
    {
        var page = Create(new StubLibrary(new GameLibrary([], [])));

        await page.RefreshAsync();

        Assert.True(page.IsEmpty);
        Assert.Equal("0", page.TotalText);
        Assert.Equal("0", page.PlatformsText);
    }

    [Fact]
    public async Task MoreThanThreeHundredGamesShowTheFirstOnesAndSayHowManyThereAre()
    {
        var many = Enumerable.Range(0, 320).Select(i => Steam((1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), $"Jogo {i:000}", i * 1024L)).ToList();
        var page = Create(new StubLibrary(new GameLibrary(many, [])));

        await page.RefreshAsync();

        Assert.Equal(300, page.Items.Count);
        Assert.Equal("Mostrando 300 de 320 jogos. Use a busca para achar os outros.", page.ShowingText);
    }
}
