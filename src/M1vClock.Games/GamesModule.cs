using M1vClock.Games.Frames;
using M1vClock.Games.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace M1vClock.Games;

public static class GamesModule
{
    public static IServiceCollection AddGamesModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IGameFileSystem, WindowsGameFileSystem>();
        services.AddSingleton<IGameLocations, WindowsGameLocations>();
        services.AddSingleton<IGameLibraryService, GameLibraryService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IFrameEventSource, EtwFrameEventSource>();
        services.AddSingleton<IProcessNames, WindowsProcessNames>();
        services.AddSingleton<IForegroundWindow, WindowsForegroundWindow>();
        services.AddSingleton<FrameMonitor>();
        services.AddSingleton<IFrameMonitor>(provider => provider.GetRequiredService<FrameMonitor>());
        return services;
    }
}
