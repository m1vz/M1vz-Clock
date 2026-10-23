using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.Optimize.Services;

namespace M1vClock.App.Pages.Optimize;

public sealed class ServiceModeOption(ServiceRowViewModel row, ServiceStartMode mode, bool isCurrent)
{
    public ServiceRowViewModel Row { get; } = row;

    public ServiceStartMode Mode { get; } = mode;

    public string Text { get; } = ServiceRows.ModeText(mode);

    public bool IsCurrent { get; } = isCurrent;

    public bool IsEnabled => !IsCurrent && Row.CanChange;

    public string ToolTip => Row.CanChange ? Text : Row.ProtectionText.Length > 0 ? Row.ProtectionText : Loc.Get("Opt.Startup.NeedAdmin");

    public override string ToString() => Text;
}

public sealed class ServiceRowViewModel
{
    public ServiceRowViewModel(ServiceEntry entry, bool isElevated)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        Title = entry.DisplayName;
        Detail = ServiceRows.Shorten(entry.Description);
        Meta = Loc.Format("Opt.Services.Meta", string.IsNullOrWhiteSpace(entry.Company) ? entry.Name : $"{entry.Company} · {entry.Name}", entry.Account ?? string.Empty).TrimEnd(' ', '·');
        Badge = Loc.Get($"Opt.Services.State.{entry.State}");
        Tone = entry.IsRunning ? MetricState.Good : MetricState.Neutral;
        ModeText = Loc.Format("Opt.Services.Mode.Label", ServiceRows.ModeText(entry.StartMode));
        ProtectionText = entry.Protection == ProtectionReason.None ? string.Empty : Loc.Get($"Opt.Services.Protection.{entry.Protection}");
        CanChange = entry.CanChange && isElevated;
        Options = ServiceManager.Changeable.Select(m => new ServiceModeOption(this, m, ServiceRows.IsCurrent(entry.StartMode, m))).ToList();
        ShowOptions = entry.CanChange;
    }

    public ServiceEntry Entry { get; }

    public string Title { get; }

    public string Detail { get; }

    public string Meta { get; }

    public string Badge { get; }

    public MetricState Tone { get; }

    public string ModeText { get; }

    public string ProtectionText { get; }

    public bool CanChange { get; }

    public bool ShowOptions { get; }

    public IReadOnlyList<ServiceModeOption> Options { get; }

    public bool HasDetail => Detail.Length > 0;

    public bool HasProtection => ProtectionText.Length > 0;

    public string AccessibleName => $"{Title}, {Badge}, {ModeText}";

    public override string ToString() => AccessibleName;
}

public static class ServiceRows
{
    public const int DescriptionLength = 160;
    public const int MaximumShown = 200;

    public static string ModeText(ServiceStartMode mode) => Loc.Get($"Opt.Services.Mode.{mode}");

    public static bool IsCurrent(ServiceStartMode current, ServiceStartMode option) =>
        current == option || (option == ServiceStartMode.Automatic && current == ServiceStartMode.AutomaticDelayed);

    public static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var flat = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= DescriptionLength ? flat : string.Concat(flat.AsSpan(0, DescriptionLength - 1), "…");
    }

    public static IReadOnlyList<ServiceRowViewModel> Filter(IReadOnlyList<ServiceEntry> services, bool thirdPartyOnly, string search, bool isElevated, out int matching)
    {
        ArgumentNullException.ThrowIfNull(services);
        var text = (search ?? string.Empty).Trim();
        var found = services
            .Where(s => !thirdPartyOnly || s.Origin == ServiceOrigin.ThirdParty)
            .Where(s => text.Length == 0
                || s.DisplayName.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || s.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || (s.Company?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false)
                || (s.Description?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .ToList();
        matching = found.Count;
        return found.Take(MaximumShown).Select(s => new ServiceRowViewModel(s, isElevated)).ToList();
    }
}
