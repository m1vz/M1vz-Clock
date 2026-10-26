using M1vClock.Core.Paths;
using M1vClock.Tuning.Gpu;
using M1vClock.Tuning.Stability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace M1vClock.Tuning;

public static class TuningModule
{
    public static IServiceCollection AddTuningModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IHardwareErrorSource, WindowsHardwareErrorSource>();
        services.AddSingleton<AdlxReader>();
        services.AddSingleton(provider => new NvidiaTuner(
            () => NvmlLibrary.TryOpen(provider.GetRequiredService<ILogger<NvidiaTuner>>()),
            provider.GetRequiredService<ILogger<NvidiaTuner>>()));
        services.AddSingleton<IGpuTuner>(provider => provider.GetRequiredService<NvidiaTuner>());
        services.AddSingleton(provider =>
        {
            var paths = provider.GetRequiredService<AppPaths>();
            return new SessionGuard(
                Path.Combine(paths.EnsureDirectory(paths.State), "gpu-tuning.json"),
                provider.GetRequiredService<IHardwareErrorSource>(),
                provider.GetRequiredService<IGpuTuner>(),
                provider.GetRequiredService<ILogger<SessionGuard>>());
        });
        return services;
    }
}
