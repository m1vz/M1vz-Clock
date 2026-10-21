using System.Windows;
using Microsoft.Win32;

namespace M1vClock.App.Services;

public sealed class SystemPreferences : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AccessibilityKey = @"Software\Microsoft\Accessibility";

    public SystemPreferences()
    {
        Read();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
    }

    public event EventHandler? Changed;

    public bool ReduceMotion { get; private set; }

    public bool ReduceTransparency { get; private set; }

    public bool HighContrast { get; private set; }

    public bool AppsUseLightTheme { get; private set; }

    public double TextScale { get; private set; } = 1;

    public static bool SupportsMica => Environment.OSVersion.Version.Build >= 22621;

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
    }

    private void Read()
    {
        ReduceMotion = !SystemParameters.ClientAreaAnimation;
        HighContrast = SystemParameters.HighContrast;
        using var personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        ReduceTransparency = personalize?.GetValue("EnableTransparency") is int transparency && transparency == 0;
        AppsUseLightTheme = personalize?.GetValue("AppsUseLightTheme") is not int light || light != 0;
        using var accessibility = Registry.CurrentUser.OpenSubKey(AccessibilityKey);
        TextScale = accessibility?.GetValue("TextScaleFactor") is int factor && factor is >= 100 and <= 225 ? factor / 100.0 : 1;
    }

    private void Refresh()
    {
        var before = (ReduceMotion, ReduceTransparency, HighContrast, AppsUseLightTheme, TextScale);
        Read();
        if (before != (ReduceMotion, ReduceTransparency, HighContrast, AppsUseLightTheme, TextScale))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatch();

    private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Dispatch();

    private void Dispatch()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Refresh();
        }
        else
        {
            dispatcher.BeginInvoke(Refresh);
        }
    }
}
