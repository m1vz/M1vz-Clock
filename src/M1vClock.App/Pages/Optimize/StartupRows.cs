using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.Optimize.Startup;

namespace M1vClock.App.Pages.Optimize;

public sealed class StartupRowViewModel
{
    public StartupRowViewModel(StartupEntry entry, bool isElevated)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        Title = entry.DisplayName;
        Detail = string.IsNullOrWhiteSpace(entry.Company) ? entry.Name : $"{entry.Company} · {entry.Name}";
        Meta = Loc.Format("Opt.Startup.Meta", Loc.Get($"Opt.Startup.Where.{entry.Location}"), entry.ExecutablePath ?? entry.Command);
        (Badge, Tone) = entry.TargetMissing
            ? (Loc.Get("Opt.Startup.Status.Missing"), MetricState.Warning)
            : entry.Enabled
                ? (Loc.Get("Opt.Startup.Status.Enabled"), MetricState.Neutral)
                : (Loc.Get("Opt.Startup.Status.Disabled"), MetricState.Good);
        ActionText = Loc.Get(entry.Enabled ? "Opt.Startup.Action.Disable" : "Opt.Startup.Action.Enable");
        CanChange = entry.Scope == StartupScope.User || isElevated;
        ActionToolTip = CanChange ? ActionText : Loc.Get("Opt.Startup.NeedAdmin");
    }

    public StartupEntry Entry { get; }

    public string Title { get; }

    public string Detail { get; }

    public string Meta { get; }

    public string Badge { get; }

    public MetricState Tone { get; }

    public string ActionText { get; }

    public string ActionToolTip { get; }

    public bool CanChange { get; }

    public string AccessibleName => $"{Title}, {Badge}";

    public override string ToString() => AccessibleName;
}
