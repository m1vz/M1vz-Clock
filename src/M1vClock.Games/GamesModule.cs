using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Games;

public static class GamesModule
{
    public static IServiceCollection AddGamesModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
