using System.IO;
using M1vClock.App.Dialogs;
using M1vClock.App.Pages.Games;
using M1vClock.Core.Shell;
using M1vClock.Games.Library;
using M1vClock.Games.Profiles;
using M1vClock.Games.Settings;

namespace M1vClock.App.Tests;

internal sealed class MemoryGameSettings : IGameSettings
{
    private readonly GameSettings _settings = new();

    public event EventHandler? Changed;

    public T Read<T>(Func<GameSettings, T> read) => read(_settings);

    public void Update(Action<GameSettings> change)
    {
        change(_settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class StubSteamOptions : ISteamLaunchOptions
{
    public Dictionary<string, string> Options { get; } = new(StringComparer.Ordinal);

    public SteamOptionsResult Next { get; set; } = SteamOptionsResult.Written;

    public string? Read(string appId) => Options.TryGetValue(appId, out var text) ? text : null;

    public SteamOptionsResult Write(string appId, string? options)
    {
        if (Next == SteamOptionsResult.Written)
        {
            Options[appId] = options ?? string.Empty;
        }

        return Next;
    }
}

internal sealed class StubGameFiles : IGameFileSystem
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Files.Contains(path);

    public bool DirectoryExists(string path) => Folders.Contains(path);

    public string ReadAllText(string path) => throw new FileNotFoundException(path);

    public IEnumerable<string> EnumerateFiles(string directory, string pattern) => [];

    public IEnumerable<string> EnumerateDirectories(string directory) => [];
}

internal sealed class RecordingRunner : IGameSessionRunner
{
    public List<(GameEntry Game, GameProfile Profile)> Runs { get; } = [];

    public TaskCompletionSource<GameSessionStatus> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<GameSessionStatus> RunAsync(GameEntry game, GameProfile profile, GameSessionTitles titles, IProgress<GameSessionStatus> progress, CancellationToken cancellationToken)
    {
        lock (Runs)
        {
            Runs.Add((game, profile));
        }

        progress.Report(new GameSessionStatus(GameSessionStage.Running, game.Name, 100, []));
        using var registration = cancellationToken.Register(() => Finish.TrySetResult(new GameSessionStatus(GameSessionStage.Cancelled, game.Name, null, [])));
        var final = await Finish.Task.ConfigureAwait(false);
        progress.Report(final);
        return final;
    }
}

internal sealed class GameTestRig
{
    public MemoryGameSettings Settings { get; } = new();

    public StubSteamOptions Steam { get; } = new();

    public StubDialogs Files { get; } = new();

    public StubGameFiles FileSystem { get; } = new();

    public RecordingRunner Runner { get; } = new();

    public DialogService Dialogs { get; } = new();

    public ProfileEditorServices Editor => new(Settings, Steam, Files, FileSystem);

    public GameSessionHost Sessions { get; }

    public GameTestRig()
    {
        Sessions = new GameSessionHost(Runner);
    }

    public LibraryServices Services(IGameLibraryService library, IShellLauncher shell) =>
        new(library, shell, Sessions, Editor, Dialogs, new InlineUiThread());
}
