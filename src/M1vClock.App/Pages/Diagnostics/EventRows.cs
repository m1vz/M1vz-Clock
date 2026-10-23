using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.Diagnostics.Events;

namespace M1vClock.App.Pages.Diagnostics;

public sealed class EventGroupViewModel
{
    public EventGroupViewModel(EventGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Group = group;
        var known = group.Known;
        Title = known is null ? group.Provider : Loc.Get($"Diag.Known.{known.Key}.Title");
        Detail = known is null ? group.Message ?? string.Empty : Loc.Get($"Diag.Known.{known.Key}.Detail");
        Message = known is null ? string.Empty : group.Message ?? string.Empty;
        KindText = known is null ? string.Empty : Loc.Get($"Diag.Kind.{known.Kind}");
        SourceText = Loc.Format("Diag.Events.Source", group.Provider, group.Id.ToString(Loc.Culture));
        LogText = Loc.Get($"Diag.Events.Log.{group.Log}");
        CountText = Loc.Format("Diag.Events.Count", group.Count.ToString("N0", Loc.Culture));
        LastText = Loc.Format("Diag.Events.Last", group.Last.ToLocalTime().ToString("g", Loc.Culture));
        SeverityText = Loc.Get($"Diag.Severity.{group.Severity}");
        Tone = group.Severity switch
        {
            EventSeverity.Critical => MetricState.Critical,
            EventSeverity.Error => MetricState.Warning,
            _ => MetricState.Neutral,
        };
    }

    public EventGroup Group { get; }

    public string Title { get; }

    public string Detail { get; }

    public string Message { get; }

    public string KindText { get; }

    public string SourceText { get; }

    public string LogText { get; }

    public string CountText { get; }

    public string LastText { get; }

    public string SeverityText { get; }

    public MetricState Tone { get; }

    public bool HasDetail => Detail.Length > 0;

    public bool HasMessage => Message.Length > 0;

    public bool HasKind => KindText.Length > 0;

    public string AccessibleName => $"{SeverityText}: {Title}. {CountText}. {LastText}";

    public override string ToString() => AccessibleName;
}
