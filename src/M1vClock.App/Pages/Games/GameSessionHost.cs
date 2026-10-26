using M1vClock.App.Localization;
using M1vClock.Games.Library;
using M1vClock.Games.Profiles;

namespace M1vClock.App.Pages.Games;

public interface IGameSessionRunner
{
    Task<GameSessionStatus> RunAsync(GameEntry game, GameProfile profile, GameSessionTitles titles, IProgress<GameSessionStatus> progress, CancellationToken cancellationToken);
}

public sealed class GameSessionRunner(GameSession session) : IGameSessionRunner
{
    public Task<GameSessionStatus> RunAsync(GameEntry game, GameProfile profile, GameSessionTitles titles, IProgress<GameSessionStatus> progress, CancellationToken cancellationToken) =>
        session.RunAsync(game, profile, titles, progress, cancellationToken);
}

public sealed class GameSessionHost(IGameSessionRunner runner) : IDisposable
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancel;
    private Task? _running;

    public event EventHandler? Changed;

    public GameSessionStatus? Status { get; private set; }

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _running is { IsCompleted: false };
            }
        }
    }

    public bool TryStart(GameEntry game, GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(profile);
        lock (_gate)
        {
            if (_running is { IsCompleted: false })
            {
                return false;
            }

            _cancel?.Dispose();
            _cancel = new CancellationTokenSource();
            var titles = new GameSessionTitles(
                Loc.Format("Game.Profile.Journal.Gpu", game.Name),
                Loc.Format("Game.Profile.Journal.Fullscreen", game.Name));
            var progress = new Relay(this);
            var token = _cancel.Token;
            _running = Task.Run(() => runner.RunAsync(game, profile.Copy(), titles, progress, token), token);
        }

        return true;
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cancel?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cancel?.Cancel();
            _cancel?.Dispose();
            _cancel = null;
        }
    }

    public Task WaitAsync()
    {
        lock (_gate)
        {
            return _running ?? Task.CompletedTask;
        }
    }

    private void Publish(GameSessionStatus status)
    {
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Relay(GameSessionHost host) : IProgress<GameSessionStatus>
    {
        public void Report(GameSessionStatus value) => host.Publish(value);
    }
}

public static class GameSessionText
{
    public static string Describe(GameSessionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var text = status.Stage == GameSessionStage.Failed && status.Error is { Length: > 0 } error
            ? Loc.Format("Game.Session.Stage.Failed", status.Game, error)
            : Loc.Format($"Game.Session.Stage.{status.Stage}", status.Game);
        var warnings = status.Warnings.Select(w => Loc.Get($"Game.Session.Warning.{w}")).ToList();
        return warnings.Count == 0 ? text : text + " " + string.Join(" ", warnings);
    }
}
