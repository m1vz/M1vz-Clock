using M1vClock.Core.Settings;
using M1vClock.Games.Library;
using M1vClock.Games.Overlay;
using M1vClock.Games.Profiles;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Settings;

public sealed class ManualGame
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string ExePath { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;
}

public sealed class GameSettings : IVersionedSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<ManualGame> ManualGames { get; set; } = [];

    public Dictionary<string, GameProfile> Profiles { get; set; } = new(StringComparer.Ordinal);

    public OverlaySettings Overlay { get; set; } = new();

    public string? ProtectedFpsApiKey { get; set; }
}

public interface IGameSettings
{
    event EventHandler? Changed;

    T Read<T>(Func<GameSettings, T> read);

    void Update(Action<GameSettings> change);
}

public sealed class GameSettingsService : IGameSettings
{
    private readonly SettingsStore<GameSettings> _store;
    private readonly ILogger<GameSettingsService> _logger;
    private readonly Lock _gate = new();
    private readonly GameSettings _current;

    public GameSettingsService(SettingsStore<GameSettings> store, ILogger<GameSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
        _current = store.Load().Settings;
    }

    public event EventHandler? Changed;

    public T Read<T>(Func<GameSettings, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        lock (_gate)
        {
            return read(_current);
        }
    }

    public void Update(Action<GameSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            change(_current);
            try
            {
                _store.Save(_current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GamesLog.SettingsSaveFailed(_logger, ex);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
