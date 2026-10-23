namespace M1vClock.Diagnostics.Crashes;

public static class FaultClassifier
{
    private static readonly (string Prefix, string Vendor)[] GraphicsModules =
    [
        ("amdxx", "AMD"),
        ("amdxc", "AMD"),
        ("atidxx", "AMD"),
        ("atiumd", "AMD"),
        ("aticfx", "AMD"),
        ("amdvlk", "AMD"),
        ("nvwgf2um", "NVIDIA"),
        ("nvd3dum", "NVIDIA"),
        ("nvoglv", "NVIDIA"),
        ("nvldumd", "NVIDIA"),
        ("nvlddmkm", "NVIDIA"),
        ("igd1", "Intel"),
        ("igdumd", "Intel"),
        ("ig9icd", "Intel"),
        ("igc64", "Intel"),
        ("igdusc", "Intel"),
    ];

    public static (FaultOrigin Origin, string? Vendor) Classify(AppCrash crash)
    {
        ArgumentNullException.ThrowIfNull(crash);
        var module = Path.GetFileName(crash.Module.Replace('/', '\\'));
        foreach (var (prefix, vendor) in GraphicsModules)
        {
            if (module.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (FaultOrigin.GraphicsDriver, vendor);
            }
        }

        var app = Path.GetFileName(crash.App.Replace('/', '\\'));
        if (string.Equals(module, app, StringComparison.OrdinalIgnoreCase)
            || (crash.ModulePath is not null && crash.AppPath is not null && string.Equals(crash.ModulePath, crash.AppPath, StringComparison.OrdinalIgnoreCase)))
        {
            return (FaultOrigin.Application, null);
        }

        if (crash.ModulePath is { } path && IsWindowsPath(path))
        {
            return (FaultOrigin.Windows, null);
        }

        return (FaultOrigin.ThirdPartyModule, null);
    }

    private static bool IsWindowsPath(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !path.Contains(@"\DriverStore\", StringComparison.OrdinalIgnoreCase)
            && windows.Length > 0
            && path.StartsWith(windows + @"\", StringComparison.OrdinalIgnoreCase);
    }
}
