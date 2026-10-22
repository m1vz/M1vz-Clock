namespace M1vClock.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit,
}

public sealed class AppSettings : IVersionedSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public string Language { get; set; } = "pt-BR";

    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    public bool CheckForUpdates { get; set; } = true;

    public bool NotifyDevices { get; set; } = true;

    public bool BackgroundSensorHistory { get; set; } = true;

    public double StressCpuLimit { get; set; } = 85;

    public double StressGpuLimit { get; set; } = 83;

    public IReadOnlyList<string> CollapsedSidebarSections { get; set; } = [];
}
