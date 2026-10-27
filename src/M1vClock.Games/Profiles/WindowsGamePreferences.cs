using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using Microsoft.Win32;

namespace M1vClock.Games.Profiles;

public static class GpuPreferenceValue
{
    public const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string Name = "GpuPreference";
    private const string HighPerformance = "2";

    public static bool IsHighPerformance(string? value) =>
        Parse(value).Any(p => p.Key == Name && p.Value == HighPerformance);

    public static string? With(string? existing, bool highPerformance)
    {
        var parts = Parse(existing).Where(p => p.Key != Name).ToList();
        if (highPerformance)
        {
            parts.Insert(0, new KeyValuePair<string, string>(Name, HighPerformance));
        }

        return parts.Count == 0 ? null : string.Concat(parts.Select(p => p.Key + "=" + p.Value + ";"));
    }

    private static List<KeyValuePair<string, string>> Parse(string? value)
    {
        var parts = new List<KeyValuePair<string, string>>();
        foreach (var token in (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = token.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                parts.Add(new KeyValuePair<string, string>(token[..equals].Trim(), token[(equals + 1)..].Trim()));
            }
        }

        return parts;
    }
}

public static class CompatibilityLayers
{
    public const string KeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    public const string DisableFullscreenOptimizations = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    public static bool HasFullscreenOptimizationsDisabled(string? value) =>
        Tokens(value).Contains(DisableFullscreenOptimizations, StringComparer.OrdinalIgnoreCase);

    public static string? With(string? existing, bool disableFullscreenOptimizations)
    {
        var tokens = Tokens(existing).Where(t => !string.Equals(t, DisableFullscreenOptimizations, StringComparison.OrdinalIgnoreCase)).ToList();
        if (disableFullscreenOptimizations)
        {
            tokens.Add(DisableFullscreenOptimizations);
        }

        return tokens.Count == 0 ? null : "~ " + string.Join(' ', tokens);
    }

    private static List<string> Tokens(string? value) =>
        (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t != "~").ToList();
}

public sealed record PreferenceChange(string Title, SystemChange Change);

public static class GamePreferences
{
    public const string Module = "Games";

    public static RegistryValueAddress GpuAddress(string exe) => new(RegistryHive.CurrentUser, RegistryView.Registry64, GpuPreferenceValue.KeyPath, exe);

    public static RegistryValueAddress LayersAddress(string exe) => new(RegistryHive.CurrentUser, RegistryView.Registry64, CompatibilityLayers.KeyPath, exe);

    public static IReadOnlyList<PreferenceChange> ChangesFor(IRegistryAccess registry, string exe, GameProfile profile, string gpuTitle, string layersTitle)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(exe);
        var changes = new List<PreferenceChange>();
        Add(changes, registry, GpuAddress(exe), current => GpuPreferenceValue.With(current, profile.HighPerformanceGpu), gpuTitle);
        Add(changes, registry, LayersAddress(exe), current => CompatibilityLayers.With(current, profile.DisableFullscreenOptimizations), layersTitle);
        return changes;
    }

    private static void Add(List<PreferenceChange> changes, IRegistryAccess registry, RegistryValueAddress address, Func<string?, string?> next, string title)
    {
        var state = registry.Read(address);
        var current = state.ValueExists && state.Kind == RegistryValueKind.String ? state.Data as string : null;
        if (state.ValueExists && state.Kind != RegistryValueKind.String)
        {
            return;
        }

        var desired = next(current);
        if (string.Equals(desired, current, StringComparison.Ordinal))
        {
            return;
        }

        var desiredState = desired is null ? RegistryValueState.Absent(keyExists: true) : RegistryValueState.Text(desired);
        changes.Add(new PreferenceChange(title, RegistryValueChanges.Set(Module, title, address, desiredState, affectsSystem: false)));
    }
}
