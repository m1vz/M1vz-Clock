using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Optimize;

public static class OptimizeModule
{
    public static IServiceCollection AddOptimizeModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
