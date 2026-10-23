using M1vClock.Optimize.Cleanup;
using M1vClock.Optimize.Services;
using M1vClock.Optimize.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize;

public static class OptimizeModule
{
    public static IServiceCollection AddOptimizeModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IStartupSource, WindowsStartupSource>();
        services.AddSingleton<IFileDetailsSource, WindowsFileDetailsSource>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IServiceSource, WmiServiceSource>();
        services.AddSingleton<IServiceManager, ServiceManager>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ICleanupService>(provider => new CleanupService(
            CleanupCatalog.Create(CleanupPaths.Current),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<CleanupService>>()));
        return services;
    }
}
