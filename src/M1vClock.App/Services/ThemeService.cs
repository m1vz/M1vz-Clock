using System.Windows;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;
using M1vClock.App.DesignSystem.Tokens;
using M1vClock.Core.Settings;

namespace M1vClock.App.Services;

public sealed class ThemeService : IDisposable
{
    private readonly SystemPreferences _preferences;
    private readonly AppSettingsService _settings;
    private ThemePreference? _sessionOverride;

    public ThemeService(SystemPreferences preferences, AppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(settings);
        _preferences = preferences;
        _settings = settings;
        Current = Resolve();
        _preferences.Changed += OnPreferencesChanged;
    }

    public event EventHandler<ThemeChangingEventArgs>? Changing;

    public event EventHandler? Changed;

    public Palette Current { get; private set; }

    public ThemePreference Preference => _sessionOverride ?? _settings.Current.Theme;

    public bool UseMaterial => SystemPreferences.SupportsMica && !_preferences.ReduceTransparency && Current.Kind != PaletteKind.HighContrast;

    public bool IsDark => Current.Kind == PaletteKind.Dark;

    public void OverrideForSession(ThemePreference preference) => _sessionOverride = preference;

    public void SetPreference(ThemePreference preference)
    {
        _sessionOverride = null;
        _settings.Update(s => s.Theme = preference);
        Refresh(animated: true);
    }

    public void ApplyInitial()
    {
        Current = Resolve();
        ApplyResources();
    }

    public void Dispose() => _preferences.Changed -= OnPreferencesChanged;

    private void OnPreferencesChanged(object? sender, EventArgs e) => Refresh(animated: true);

    private void Refresh(bool animated)
    {
        var next = Resolve();
        var paletteChanged = next.Kind != Current.Kind;
        void Apply()
        {
            Current = next;
            ApplyResources();
        }

        if (paletteChanged && animated && Changing is not null)
        {
            Changing.Invoke(this, new ThemeChangingEventArgs(Apply));
        }
        else
        {
            Apply();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Palette Resolve()
    {
        if (_preferences.HighContrast)
        {
            return Palette.HighContrast();
        }

        return Preference switch
        {
            ThemePreference.Light => Palette.Light,
            ThemePreference.Dark => Palette.Dark,
            _ => _preferences.AppsUseLightTheme ? Palette.Light : Palette.Dark,
        };
    }

    private void ApplyResources()
    {
        var resources = Application.Current.Resources;
        foreach (var (name, color) in Current.Tokens())
        {
            resources[$"Color.{name}"] = color;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[$"Brush.{name}"] = brush;
        }

        var scale = _preferences.TextScale;
        foreach (var style in TypeScale.All)
        {
            resources[$"Size.{style.Name}"] = Math.Round(style.Size * scale, 1);
            resources[$"LineHeight.{style.Name}"] = Math.Round(style.LineHeight * scale, 1);
        }

        SpringAnimator.ReduceMotion = _preferences.ReduceMotion;
        resources["Motion.Reduced"] = _preferences.ReduceMotion;
    }
}

public sealed class ThemeChangingEventArgs(Action apply) : EventArgs
{
    public Action Apply { get; } = apply;
}
