using M1vClock.Core.RegistryValues;
using M1vClock.Hardware.Devices;
using M1vClock.Hardware.Gpu;
using M1vClock.Hardware.Network;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Power;
using M1vClock.Hardware.Sensors;
using M1vClock.Hardware.Ram;
using M1vClock.Hardware.Storage;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddSingleton(GpuSpecDatabase.Default);
        services.AddSingleton<IDxgiAdapterSource, VorticeDxgiAdapterSource>();
        services.AddSingleton<ISystemLibraries, WindowsSystemLibraries>();
        services.AddSingleton<GpuCollector>();
        services.AddSingleton<IGpuService, GpuService>();
        services.AddSingleton<PowerCollector>();
        services.AddSingleton<IPowerService, PowerService>();
        services.AddSingleton<StorageCollector>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<UsbCollector>();
        services.AddSingleton<IUsbDeviceService, UsbDeviceService>();
        services.AddSingleton<IDeviceChangeSource, WmiDeviceChangeSource>();
        services.AddSingleton<IDeviceWatcher, DeviceWatcher>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISensorSource, LibreHardwareSensorSource>();
        services.AddSingleton<ISensorService, SensorService>();
        services.AddSingleton<IPawnIoProbe, RegistryPawnIoProbe>();
        services.AddSingleton<INetworkInterfaceSource, SystemNetworkInterfaceSource>();
        services.AddSingleton<INetworkProbe, SystemNetworkProbe>();
        services.AddSingleton(NetworkTestOptions.Default);
        services.AddSingleton<INetworkService, NetworkService>();
        return services;
    }
}
