using M1vClock.App.Pages.Games;
using M1vClock.Core.Shell;
using M1vClock.Games.Library;
using M1vClock.Games.Profiles;
using M1vClock.Games.Settings;

namespace M1vClock.App.Tests;

public sealed class GameProfilePageTests
{
    private const string Cs2Folder = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive";
    private const string ManualExe = @"D:\Jogos\Kandidatos\Kandidatos.exe";

    private sealed class StubLibrary(IGameSettings settings) : IGameLibraryService
    {
        public Task<GameLibrary> ReadAsync(CancellationToken cancellationToken)
        {
            var manual = ManualGames.Entries(settings.Read(s => s.ManualGames.ToList()), new AllFiles());
            List<GameEntry> games =
            [
                new("730", "Counter-Strike 2", GamePlatform.Steam, Cs2Folder, null, GameState.Installed, GameLaunch.Steam("730")) { FolderExists = true },
                new("Fortnite", "Fortnite", GamePlatform.Epic, @"C:\Program Files\Epic Games\Fortnite", null, GameState.Installed, GameLaunch.Epic("Fortnite")) { FolderExists = true },
                .. manual,
            ];
            return Task.FromResult(new GameLibrary(games, []));
        }
    }

    private sealed class AllFiles : IGameFileSystem
    {
        public bool FileExists(string path) => true;

        public bool DirectoryExists(string path) => true;

        public string ReadAllText(string path) => string.Empty;

        public IEnumerable<string> EnumerateFiles(string directory, string pattern) => [];

        public IEnumerable<string> EnumerateDirectories(string directory) => [];
    }

    private sealed class NoShell : IShellLauncher
    {
        public List<string> Started { get; } = [];

        public void OpenUrl(Uri url) => throw new NotSupportedException();

        public void OpenFolder(string path) => throw new NotSupportedException();

        public void Start(string fileName, string? arguments, string? workingDirectory) => Started.Add(fileName);
    }

    private static async Task<(LibraryPageViewModel Page, GameTestRig Rig, NoShell Shell)> Open()
    {
        var rig = new GameTestRig();
        var shell = new NoShell();
        var page = new LibraryPageViewModel(rig.Services(new StubLibrary(rig.Settings), shell), new CollectingLogger<LibraryPageViewModel>());
        page.OnNavigatedTo();
        await page.RefreshAsync();
        return (page, rig, shell);
    }

    [Fact]
    public async Task TheFilterHasEveryStoreAndManualGamesHaveTheirOwnTab()
    {
        var (page, rig, _) = await Open();
        rig.Settings.Update(s => s.ManualGames.Add(new ManualGame { Name = "Kandidatos", ExePath = ManualExe }));
        await page.RefreshAsync();

        page.PlatformIndex = 6;

        Assert.Equal(["Todos", "Steam", "Epic Games", "Riot Games", "GOG", "Ubisoft", "Manuais"], page.PlatformLabels);
        var row = Assert.Single(page.Items);
        Assert.True(row.IsManual);
    }

    [Fact]
    public async Task AProfileIsEditedSavedAndMarkedOnTheRow()
    {
        var (page, rig, _) = await Open();
        var cs2 = page.Items.Single(r => r.Title == "Counter-Strike 2");
        Assert.Equal("Criar perfil", cs2.ProfileText);

        page.EditProfile(cs2);
        var editor = page.Editor!;
        editor.EngineIndex = 1;
        editor.WindowModeIndex = 1;
        editor.WidthText = "1280";
        editor.HeightText = "960";
        editor.RefreshText = "240";
        editor.PriorityIndex = 2;
        Assert.Equal("-fullscreen -w 1280 -h 960 -freq 240", editor.Preview);
        editor.Save();

        Assert.False(page.IsEditing);
        var saved = rig.Settings.Read(s => s.Profiles["Steam:730"]);
        Assert.Equal(GamePriority.High, saved.Priority);
        Assert.Equal(1280, saved.Width);
        var row = page.Items.Single(r => r.Title == "Counter-Strike 2");
        Assert.True(row.HasProfile);
        Assert.Equal("Editar perfil", row.ProfileText);
        Assert.Equal("Counter-Strike 2, Steam, Instalado, Com perfil", row.AccessibleName);
        Assert.Equal("Perfil de Counter-Strike 2 salvo. Ele vale a partir do próximo clique em Jogar.", page.Notice);
    }

    [Fact]
    public async Task AnInvalidProfileShowsWhyAndIsNotSaved()
    {
        var (page, rig, _) = await Open();
        page.EditProfile(page.Items.Single(r => r.Title == "Counter-Strike 2"));
        var editor = page.Editor!;

        editor.WidthText = "1920";
        Assert.Equal("Preencha largura e altura juntas, com números entre 320 e 16384.", editor.Problem);
        editor.HeightText = "abc";
        editor.Save();

        Assert.True(page.IsEditing);
        Assert.False(rig.Settings.Read(s => s.Profiles.ContainsKey("Steam:730")));
        editor.HeightText = "1080";
        editor.RefreshText = "5";
        Assert.Equal("A taxa deve ficar entre 24 e 1000 Hz, ou vazia.", editor.Problem);
        editor.Cancel();
        Assert.False(page.IsEditing);
    }

    [Fact]
    public async Task SavingAnUntouchedProfileDoesNotCreateOne()
    {
        var (page, rig, _) = await Open();
        page.EditProfile(page.Items.Single(r => r.Title == "Fortnite"));

        page.Editor!.Save();

        Assert.Empty(rig.Settings.Read(s => s.Profiles));
    }

    [Fact]
    public async Task EachStoreExplainsWhereTheArgumentsGo()
    {
        var (page, rig, _) = await Open();
        rig.Settings.Update(s => s.ManualGames.Add(new ManualGame { Name = "Kandidatos", ExePath = ManualExe }));
        await page.RefreshAsync();

        page.EditProfile(page.Items.Single(r => r.Title == "Counter-Strike 2"));
        var steam = page.Editor!;
        page.Editor!.Cancel();
        page.EditProfile(page.Items.Single(r => r.Title == "Fortnite"));
        var epic = page.Editor!;
        page.Editor!.Cancel();
        page.EditProfile(page.Items.Single(r => r.Title == "Kandidatos"));
        var manual = page.Editor!;

        Assert.True(steam.IsSteam);
        Assert.StartsWith("A Steam não aceita argumentos", steam.ArgumentsNote, StringComparison.Ordinal);
        Assert.False(epic.IsSteam);
        Assert.StartsWith("Esta loja abre o jogo", epic.ArgumentsNote, StringComparison.Ordinal);
        Assert.Equal("Estes argumentos vão junto quando o m1vClock abre o executável.", manual.ArgumentsNote);
    }

    [Fact]
    public async Task SteamOptionsAreWrittenOnlyWhenSteamAllowsAndTheResultIsShown()
    {
        var (page, rig, _) = await Open();
        rig.Steam.Options["730"] = "-novid";
        page.EditProfile(page.Items.Single(r => r.Title == "Counter-Strike 2"));
        var editor = page.Editor!;
        Assert.Equal("Opções de inicialização na Steam: -novid", editor.SteamCurrentText);

        editor.ExtraText = "-novid -high";
        rig.Steam.Next = SteamOptionsResult.SteamRunning;
        editor.WriteSteam();
        Assert.Equal("Feche a Steam antes de gravar: ela reescreve o arquivo ao sair e apagaria a mudança.", editor.Problem);
        Assert.Equal("-novid", rig.Steam.Options["730"]);

        rig.Steam.Next = SteamOptionsResult.Written;
        editor.WriteSteam();
        Assert.Equal("-novid -high", rig.Steam.Options["730"]);
        Assert.Equal("Opções de inicialização na Steam: -novid -high", editor.SteamCurrentText);
        Assert.False(editor.HasProblem);
        Assert.True(editor.HasNotice);
    }

    [Fact]
    public async Task HelpersAndTheExecutableArePickedFromFilesAndANetworkPathIsRefused()
    {
        var (page, rig, _) = await Open();
        page.EditProfile(page.Items.Single(r => r.Title == "Counter-Strike 2"));
        var editor = page.Editor!;

        rig.Files.OpenPath = @"C:\Ferramentas\crosshair.exe";
        editor.AddHelper();
        rig.Files.OpenPath = @"\\servidor\x\cheat.exe";
        editor.AddHelper();
        Assert.Equal("Programas auxiliares precisam ser arquivos .exe deste PC, com argumentos de até 1024 caracteres.", editor.Problem);
        rig.Files.OpenPath = Cs2Folder + @"\game\bin\win64\cs2.exe";
        editor.PickExe();
        editor.HighPerformanceGpu = true;
        editor.Helpers[0].CloseWithGame = false;
        editor.Save();

        var saved = rig.Settings.Read(s => s.Profiles["Steam:730"]);
        var helper = Assert.Single(saved.Helpers);
        Assert.Equal(@"C:\Ferramentas\crosshair.exe", helper.Path);
        Assert.False(helper.CloseWithGame);
        Assert.Equal(Cs2Folder + @"\game\bin\win64\cs2.exe", saved.ExePath);
        Assert.True(saved.HighPerformanceGpu);
    }

    [Fact]
    public async Task PlayingAGameWithAProfileStartsAFollowedSessionThatCanBeStopped()
    {
        var (page, rig, shell) = await Open();
        rig.Settings.Update(s => s.Profiles["Steam:730"] = new GameProfile { Priority = GamePriority.High });

        page.Play(page.Items.Single(r => r.Title == "Counter-Strike 2"));
        await WaitFor(() => page.SessionActive);

        Assert.Empty(shell.Started);
        Assert.Equal(GamePriority.High, Assert.Single(rig.Runner.Runs).Profile.Priority);
        Assert.Equal("Counter-Strike 2 em execução.", page.SessionText);

        page.Play(page.Items.Single(r => r.Title == "Counter-Strike 2"));
        Assert.Equal("Já tem um jogo sendo acompanhado. Pare o acompanhamento antes de abrir outro com perfil.", page.Problem);

        page.StopSession();
        await rig.Sessions.WaitAsync();
        await WaitFor(() => !page.SessionActive);
        Assert.Equal("Parei de acompanhar Counter-Strike 2.", page.SessionText);
    }

    [Fact]
    public async Task AGameWithoutAProfileStillOpensDirectly()
    {
        var (page, rig, shell) = await Open();

        page.Play(page.Items.Single(r => r.Title == "Fortnite"));

        Assert.Single(shell.Started);
        Assert.Empty(rig.Runner.Runs);
        Assert.False(page.HasSession);
    }

    [Fact]
    public async Task AManualGameIsAddedFromAFileAndRemovedAfterConfirmation()
    {
        var (page, rig, _) = await Open();
        rig.FileSystem.Files.Add(ManualExe);
        rig.Files.OpenPath = ManualExe;

        await page.AddGameAsync();
        Assert.Equal("Kandidatos entrou na biblioteca.", page.Notice);
        await page.AddGameAsync();
        Assert.Equal("Este jogo já está na biblioteca.", page.Problem);

        var row = page.Items.Single(r => r.Title == "Kandidatos");
        rig.Settings.Update(s => s.Profiles[GameProfile.KeyOf(row.Game)] = new GameProfile { Extra = "-log" });
        var removing = page.RemoveGameAsync(row);
        Assert.Equal("Remover Kandidatos da biblioteca?", rig.Dialogs.Current?.Title);
        rig.Dialogs.ConfirmCommand.Execute(null);
        await removing;

        Assert.Empty(rig.Settings.Read(s => s.ManualGames));
        Assert.Empty(rig.Settings.Read(s => s.Profiles));
        Assert.DoesNotContain(page.Items, r => r.Title == "Kandidatos");
    }

    [Fact]
    public async Task AFileThatIsNotAnExeOrIsGoneIsNotAdded()
    {
        var (page, rig, _) = await Open();

        rig.Files.OpenPath = @"C:\Jogos\atalho.lnk";
        await page.AddGameAsync();
        Assert.Equal("Escolha um arquivo .exe deste PC (não de uma pasta de rede).", page.Problem);

        rig.Files.OpenPath = @"C:\Jogos\sumiu.exe";
        await page.AddGameAsync();
        Assert.Equal("O arquivo escolhido não existe mais.", page.Problem);
        Assert.Empty(rig.Settings.Read(s => s.ManualGames));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
