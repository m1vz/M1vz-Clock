using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Services;
using M1vClock.App.Shell;
using M1vClock.Core.Diagnostics;
using M1vClock.Core.Paths;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace M1vClock.App;

public partial class App : Application
{
    private readonly IHost _host;
    private readonly SingleInstance _instance;
    private readonly string _version;
    private ILogger<App>? _logger;
    private SessionTracker? _session;

    public App(IHost host, SingleInstance instance, string version)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(instance);
        _host = host;
        _instance = instance;
        _version = version;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Loc.Culture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        var services = _host.Services;
        _logger = services.GetRequiredService<ILogger<App>>();
        _session = services.GetRequiredService<SessionTracker>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var paths = services.GetRequiredService<AppPaths>();
        var info = services.GetRequiredService<AppInfo>();
        AppLog.Started(_logger, _version, info.IsElevated, paths.Root);
        foreach (var quarantined in paths.QuarantinedDirectories)
        {
            AppLog.DirectoryQuarantined(_logger, quarantined);
        }

        var previous = _session.Start(_version);
        if (previous is not null)
        {
            AppLog.PreviousSessionCrashed(_logger, previous.Version, previous.StartedAt);
        }

        var settings = services.GetRequiredService<AppSettingsService>();
        AppLog.SettingsLoaded(_logger, settings.LoadStatus);
        var options = services.GetRequiredService<StartupOptions>();
        var theme = services.GetRequiredService<ThemeService>();
        if (options.Theme is { } sessionTheme)
        {
            theme.OverrideForSession(sessionTheme);
        }

        theme.ApplyInitial();
        _host.Start();

        var window = services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        window.Start(options, previous);

        _instance.Listen(
            () => Dispatcher.BeginInvoke(() =>
            {
                AppLog.ActivationRequested(_logger);
                window.BringToFront();
            }),
            ex => AppLog.ActivationChannelFailed(_logger, ex));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _session?.EndCleanly();
        if (_logger is not null)
        {
            AppLog.Stopped(_logger);
        }

        _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.UnhandledUiException(_logger, e.Exception);
        }

        _session?.RecordCrash(e.Exception);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception)
        {
            return;
        }

        if (_logger is not null)
        {
            AppLog.UnhandledException(_logger, exception);
        }

        _session?.RecordCrash(exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.UnobservedTaskException(_logger, e.Exception);
        }

        e.SetObserved();
    }
}
