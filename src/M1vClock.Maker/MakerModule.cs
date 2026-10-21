using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Maker;

public static class MakerModule
{
    public static IServiceCollection AddMakerModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
