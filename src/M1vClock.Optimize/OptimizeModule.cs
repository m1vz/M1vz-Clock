using M1vClock.Optimize.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Optimize;

public static class OptimizeModule
{
    public static IServiceCollection AddOptimizeModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IStartupSource, WindowsStartupSource>();
        services.AddSingleton<IFileDetailsSource, WindowsFileDetailsSource>();
        services.AddSingleton<IStartupService, StartupService>();
        return services;
    }
}
