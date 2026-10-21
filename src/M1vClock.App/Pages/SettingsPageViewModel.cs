using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Services;
using M1vClock.Core.Paths;
using M1vClock.Core.Settings;
using M1vClock.Core.Shell;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly ThemeService _theme;
    private readonly IShellLauncher _shell;
    private readonly AppPaths _paths;
    private readonly ILogger<SettingsPageViewModel> _logger;

    public SettingsPageViewModel(ThemeService theme, IShellLauncher shell, AppPaths paths, AppInfo info, ILogger<SettingsPageViewModel> logger)
        : base(PageCatalog.Find("settings"))
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(info);
        _theme = theme;
        _shell = shell;
        _paths = paths;
        _logger = logger;
        ThemeIndex = theme.Preference switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0,
        };
        Version = info.Version;
        DataRoot = info.DataRoot;
        ElevationText = Loc.Get(info.IsElevated ? "Settings.About.Elevated" : "Settings.About.NotElevated");
    }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    public string Version { get; }

    public string DataRoot { get; }

    public string ElevationText { get; }

    [ObservableProperty]
    public partial string ActionError { get; set; } = string.Empty;

    partial void OnThemeIndexChanged(int value) => _theme.SetPreference(value switch
    {
        1 => ThemePreference.Light,
        2 => ThemePreference.Dark,
        _ => ThemePreference.System,
    });

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            ActionError = string.Empty;
            _shell.OpenFolder(_paths.EnsureDirectory(_paths.Logs));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            AppLog.ActionFailed(_logger, ex, "abrir a pasta de logs");
            ActionError = Loc.Get("Settings.About.OpenLogsFailed");
        }
    }
}
