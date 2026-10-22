using M1vClock.Benchmarks.Results;
using M1vClock.Benchmarks.Safety;
using M1vClock.Benchmarks.Stress;
using M1vClock.Core.Paths;
using M1vClock.Core.Security;
using M1vClock.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks;

public static class BenchmarksModule
{
    public const string HistoryFileName = "benchmarks.json";

    public static IServiceCollection AddBenchmarksModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(provider =>
        {
            var paths = provider.GetRequiredService<AppPaths>();
            var elevation = provider.GetRequiredService<ElevationInfo>();
            var folder = paths.EnsureDirectory(paths.UserDirectory(elevation.UserSid));
            return BenchmarkHistory.CreateStore(Path.Combine(folder, HistoryFileName), provider.GetRequiredService<ILogger<BenchmarkHistory>>());
        });
        services.AddSingleton(provider =>
        {
            var history = new BenchmarkHistory(
                provider.GetRequiredService<SettingsStore<BenchmarkHistoryFile>>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<BenchmarkHistory>>());
            history.Load();
            return history;
        });
        services.AddSingleton<SafetyMonitor>();
        services.AddSingleton<StressSession>();
        return services;
    }
}
