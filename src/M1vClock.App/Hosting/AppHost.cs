using M1vClock.App.Dialogs;
using M1vClock.App.Navigation;
using M1vClock.App.Pages;
using M1vClock.App.Pages.Diagnostics;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Pages.Optimize;
using M1vClock.App.Pages.Performance;
using M1vClock.App.Search;
using M1vClock.App.Services;
using M1vClock.App.Services.Reports;
using M1vClock.App.Shell;
using M1vClock.Benchmarks;
using M1vClock.Benchmarks.Safety;
using M1vClock.Core;
using M1vClock.Core.Paths;
using M1vClock.Core.Security;
using M1vClock.Diagnostics;
using M1vClock.Games;
using M1vClock.Hardware;
using M1vClock.Integrations;
using M1vClock.Maker;
using M1vClock.Optimize;
using M1vClock.Tuning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace M1vClock.App.Hosting;

public static class AppHost
{
    public static IHost Build(AppPaths paths, ElevationInfo elevation, StartupOptions options, string version)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(options);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddSerilog(Log.Logger, dispose: false);
        builder.Services
            .AddCoreModule(paths, elevation)
            .AddHardwareModule()
            .AddBenchmarksModule()
            .AddTuningModule()
            .AddOptimizeModule()
            .AddDiagnosticsModule()
            .AddGamesModule()
            .AddIntegrationsModule()
            .AddMakerModule();

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new AppInfo(version, paths.Root, elevation.IsElevated));
        builder.Services.AddSingleton<IUiThread, WpfUiThread>();
        builder.Services.AddSingleton<ActivityLog>();
        builder.Services.AddSingleton<IClipboardService, WpfClipboardService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<INotificationService>(provider => provider.GetRequiredService<NotificationService>());
        builder.Services.AddHostedService<DeviceNotifier>();
        builder.Services.AddSingleton<OverviewHistory>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<OverviewHistory>());
        builder.Services.AddSingleton<HeavyTestGate>();
        builder.Services.AddSingleton<IComponentProbe, SensorComponentProbe>();
        builder.Services.AddSingleton<IMachineProfile, MachineProfileProvider>();
        builder.Services.AddSingleton<BenchmarkWorkLocation>();
        builder.Services.AddHostedService<BenchmarkTempSweeper>();
        builder.Services.AddSingleton<SystemPreferences>();
        builder.Services.AddSingleton<AppSettingsService>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<NavigationService>();
        builder.Services.AddSingleton<DialogService>();
        builder.Services.AddSingleton<CommandPaletteViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<DesignSystemPageViewModel>();
        builder.Services.AddTransient<SettingsPageViewModel>();
        builder.Services.AddTransient<HistoryPageViewModel>();
        builder.Services.AddTransient<SystemPageViewModel>();
        builder.Services.AddTransient<GpuPageViewModel>();
        builder.Services.AddTransient<PowerPageViewModel>();
        builder.Services.AddTransient<NetworkDiagnosticViewModel>();
        builder.Services.AddTransient<NetworkPageViewModel>();
        builder.Services.AddTransient<DevicesPageViewModel>();
        builder.Services.AddTransient<SensorsPageViewModel>();
        builder.Services.AddTransient<OverviewPageViewModel>();
        builder.Services.AddTransient<MemoryPageViewModel>();
        builder.Services.AddTransient<StoragePageViewModel>();
        builder.Services.AddSingleton<IBenchJobSource, BenchJobFactory>();
        builder.Services.AddSingleton<IFileDialogs, Win32FileDialogs>();
        builder.Services.AddTransient<BenchmarksPageViewModel>();
        builder.Services.AddSingleton<IStressWorkloadFactory, StressWorkloadFactory>();
        builder.Services.AddTransient<StressPageViewModel>();
        builder.Services.AddTransient<EventsPageViewModel>();
        builder.Services.AddTransient<CrashesPageViewModel>();
        builder.Services.AddTransient<ReliabilityPageViewModel>();
        builder.Services.AddTransient<LogsPageViewModel>();
        builder.Services.AddTransient<ReportPageViewModel>();
        builder.Services.AddTransient<StartupPageViewModel>();
        builder.Services.AddTransient<ServicesPageViewModel>();
        builder.Services.AddSingleton<IReportCollector, ReportCollector>();
        return builder.Build();
    }
}

public sealed record AppInfo(string Version, string DataRoot, bool IsElevated);
