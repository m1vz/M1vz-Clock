using M1vClock.Core.Paths;
using M1vClock.Core.Security;
using M1vClock.Core.Settings;
using M1vClock.Games.Estimate;
using M1vClock.Games.Frames;
using M1vClock.Games.Library;
using M1vClock.Games.Profiles;
using M1vClock.Games.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games;

public static class GamesModule
{
    public static IServiceCollection AddGamesModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IGameFileSystem, WindowsGameFileSystem>();
        services.AddSingleton<IGameLocations, WindowsGameLocations>();
        services.AddSingleton<IGameRegistry, WindowsGameRegistry>();
        services.AddSingleton(provider =>
        {
            var paths = provider.GetRequiredService<AppPaths>();
            var elevation = provider.GetRequiredService<ElevationInfo>();
            return new SettingsStore<GameSettings>(
                Path.Combine(paths.EnsureDirectory(paths.UserDirectory(elevation.UserSid)), "games.json"),
                GameSettings.CurrentSchemaVersion,
                [],
                provider.GetRequiredService<ILogger<SettingsStore<GameSettings>>>());
        });
        services.AddSingleton<IGameSettings, GameSettingsService>();
        services.AddSingleton<IReferenceFpsSource>(provider =>
        {
            var paths = provider.GetRequiredService<AppPaths>();
            var elevation = provider.GetRequiredService<ElevationInfo>();
            return new HowManyFpsClient(
                new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) },
                provider.GetRequiredService<IGameSettings>(),
                Path.Combine(paths.EnsureDirectory(paths.UserDirectory(elevation.UserSid)), "fps-cache.json"),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<HowManyFpsClient>>());
        });
        services.AddSingleton<IGameLibraryService, GameLibraryService>();
        services.AddSingleton<IGameProcesses, WindowsGameProcesses>();
        services.AddSingleton<GameSession>();
        services.AddSingleton<ISteamLaunchOptions, SteamLaunchOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IFrameEventSource, EtwFrameEventSource>();
        services.AddSingleton<IProcessNames, WindowsProcessNames>();
        services.AddSingleton<IForegroundWindow, WindowsForegroundWindow>();
        services.AddSingleton<FrameMonitor>();
        services.AddSingleton<IFrameMonitor>(provider => provider.GetRequiredService<FrameMonitor>());
        return services;
    }
}
