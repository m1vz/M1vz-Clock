using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Tuning;

public static class TuningModule
{
    public static IServiceCollection AddTuningModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
