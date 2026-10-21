using M1vClock.Core.Diagnostics;
using M1vClock.Core.Journal;
using M1vClock.Core.Paths;
using M1vClock.Core.Processes;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.RestorePoints;
using M1vClock.Core.Security;
using M1vClock.Core.Settings;
using M1vClock.Core.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace M1vClock.Core;

public static class CoreModule
{
    public static IServiceCollection AddCoreModule(this IServiceCollection services, AppPaths paths, ElevationInfo elevation)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(elevation);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(paths);
        services.AddSingleton(elevation);
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IShellLauncher, ShellLauncher>();
        services.AddSingleton<ITrustVerifier, TrustVerifier>();
        services.AddSingleton<IRegistryAccess, WindowsRegistryAccess>();
        services.AddSingleton<IChangeHandler, RegistryValueHandler>();
        services.AddSingleton<IRestorePointService, WindowsRestorePointService>();
        services.AddSingleton<IRestorePointGate>(provider => new RestorePointGate(
            provider.GetRequiredService<IRestorePointService>(),
            Path.Combine(paths.EnsureDirectory(paths.State), "restore-point.json"),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<RestorePointGate>>()));
        services.AddSingleton(provider => new JournalStore(
            Path.Combine(paths.EnsureDirectory(paths.Journal), "journal.json"),
            provider.GetRequiredService<ILogger<JournalStore>>()));
        services.AddSingleton<IChangeJournal, ChangeJournal>();
        services.AddSingleton(provider => new SessionTracker(
            paths.EnsureDirectory(paths.State),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new SettingsStore<AppSettings>(
            Path.Combine(paths.EnsureDirectory(paths.UserDirectory(elevation.UserSid)), "settings.json"),
            AppSettings.CurrentSchemaVersion,
            provider.GetServices<ISettingsMigration>(),
            provider.GetRequiredService<ILogger<SettingsStore<AppSettings>>>()));
        return services;
    }
}
