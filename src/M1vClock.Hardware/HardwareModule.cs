using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Hardware;

public static class HardwareModule
{
    public static IServiceCollection AddHardwareModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
