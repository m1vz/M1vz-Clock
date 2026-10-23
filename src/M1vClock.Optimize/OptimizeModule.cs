using M1vClock.Optimize.Apps;
using M1vClock.Optimize.Cleanup;
using M1vClock.Optimize.Memory;
using M1vClock.Optimize.Services;
using M1vClock.Optimize.Repair;
using M1vClock.Optimize.Startup;
using M1vClock.Optimize.Tweaks;
using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
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
        services.AddSingleton<IMemoryListSource, NtMemoryListSource>();
        services.AddSingleton<IMemoryListService, MemoryListService>();
        services.AddSingleton<IInstalledAppSource, WindowsInstalledAppSource>();
        services.AddSingleton<IInstalledAppService, InstalledAppService>();
        services.AddSingleton<IDismSource, WindowsDismSource>();
        services.AddSingleton<ISfcSource, WindowsSfcSource>();
        services.AddSingleton<IRepairService, RepairService>();
        services.AddSingleton<IPowerSchemeAccess, WindowsPowerSchemeAccess>();
        services.AddSingleton<INativeMouse, WindowsNativeMouse>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IChangeHandler, PowerSchemeHandler>());
        services.AddSingleton<ITweakService>(provider => new TweakService(
            TweakCatalog.Create(),
            provider.GetRequiredService<IRegistryAccess>(),
            provider.GetRequiredService<IPowerSchemeAccess>(),
            provider.GetRequiredService<INativeMouse>(),
            provider.GetRequiredService<IChangeJournal>(),
            provider.GetRequiredService<ILogger<TweakService>>()));
        return services;
    }
}
