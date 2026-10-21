using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Benchmarks;

public static class BenchmarksModule
{
    public static IServiceCollection AddBenchmarksModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
