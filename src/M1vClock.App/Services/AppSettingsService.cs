using M1vClock.Core.Settings;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Services;

public sealed class AppSettingsService
{
    private readonly SettingsStore<AppSettings> _store;
    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(SettingsStore<AppSettings> store, ILogger<AppSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
        var result = store.Load();
        Current = result.Settings;
        LoadStatus = result.Status;
    }

    public event EventHandler? Changed;

    public AppSettings Current { get; }

    public SettingsLoadStatus LoadStatus { get; }

    public void Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        change(Current);
        try
        {
            _store.Save(Current);
        }
        catch (IOException ex)
        {
            AppLog.SettingsSaveFailed(_logger, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.SettingsSaveFailed(_logger, ex);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
