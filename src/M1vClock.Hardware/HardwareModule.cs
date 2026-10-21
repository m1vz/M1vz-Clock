using M1vClock.Core.RegistryValues;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Ram;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Hardware;

public static class HardwareModule
{
    public static IServiceCollection AddHardwareModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWmiReader, WindowsWmiReader>();
        services.AddSingleton(provider => new PlatformCollector(
            provider.GetRequiredService<IWmiReader>(),
            provider.GetRequiredService<IRegistryAccess>(),
            Environment.MachineName));
        services.AddSingleton<IPlatformService, PlatformService>();
        services.AddSingleton<RamCollector>();
        services.AddSingleton<IRamService, RamService>();
        return services;
    }
}
