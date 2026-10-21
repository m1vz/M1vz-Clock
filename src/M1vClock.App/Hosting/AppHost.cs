using M1vClock.App.Dialogs;
using M1vClock.App.Navigation;
using M1vClock.App.Pages;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Search;
using M1vClock.App.Services;
using M1vClock.App.Shell;
using M1vClock.Benchmarks;
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
        return builder.Build();
    }
}

public sealed record AppInfo(string Version, string DataRoot, bool IsElevated);
