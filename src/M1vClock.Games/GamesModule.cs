using M1vClock.Games.Library;
using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Games;

public static class GamesModule
{
    public static IServiceCollection AddGamesModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IGameFileSystem, WindowsGameFileSystem>();
        services.AddSingleton<IGameLocations, WindowsGameLocations>();
        services.AddSingleton<IGameLibraryService, GameLibraryService>();
        return services;
    }
}
