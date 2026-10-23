using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.Optimize.Tweaks;

namespace M1vClock.App.Pages.Optimize;

public sealed class TweakRowViewModel
{
    public TweakRowViewModel(TweakEntry entry, bool isElevated)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        Id = entry.Id;
        Title = Loc.Get($"Opt.Tweaks.{entry.Id}.Title");
        Description = Loc.Get($"Opt.Tweaks.{entry.Id}.Description");
        Effect = Loc.Get($"Opt.Tweaks.{entry.Id}.Effect");
        StateText = Loc.Get($"Opt.Tweaks.State.{entry.State}");
        Tone = entry.State switch
        {
            TweakState.On => MetricState.Good,
            TweakState.Mixed => MetricState.Warning,
            _ => MetricState.Neutral,
        };
        IssueText = entry.Issue == TweakIssue.None ? string.Empty : Loc.Get($"Opt.Tweaks.Issue.{entry.Issue}");
        var meta = new List<string>();
        if (entry.NeedsRestart)
        {
            meta.Add(Loc.Get("Opt.Tweaks.Meta.Restart"));
        }

        if (entry.NeedsAdministrator)
        {
            meta.Add(Loc.Get("Opt.Tweaks.Meta.Admin"));
        }

        if (entry.HasOriginalToRestore)
        {
            meta.Add(Loc.Get("Opt.Tweaks.Meta.Changed"));
        }

        Meta = string.Join(" · ", meta);
        var missingAdministrator = entry.NeedsAdministrator && !isElevated;
        CanChange = entry.CanChange && !missingAdministrator;
        TurnOn = entry.State != TweakState.On;
        ActionText = Loc.Get(TurnOn ? "Opt.Tweaks.Action.On" : "Opt.Tweaks.Action.Off");
        ActionToolTip = missingAdministrator ? Loc.Get("Opt.Tweaks.Tip.NeedsAdmin") : IssueText;
    }

    public TweakEntry Entry { get; }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public string Effect { get; }

    public string StateText { get; }

    public MetricState Tone { get; }

    public string IssueText { get; }

    public string Meta { get; }

    public bool CanChange { get; }

    public bool TurnOn { get; }

    public string ActionText { get; }

    public string ActionToolTip { get; }

    public bool HasIssue => IssueText.Length > 0;

    public bool HasMeta => Meta.Length > 0;

    public string AccessibleName => $"{Title}, {StateText}";

    public override string ToString() => AccessibleName;
}
