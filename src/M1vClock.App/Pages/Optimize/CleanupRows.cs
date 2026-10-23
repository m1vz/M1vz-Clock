using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Optimize.Cleanup;

namespace M1vClock.App.Pages.Optimize;

public sealed class CleanupRowViewModel
{
    public CleanupRowViewModel(CleanupScan scan, bool isElevated)
    {
        ArgumentNullException.ThrowIfNull(scan);
        Scan = scan;
        var category = scan.Category;
        Title = Loc.Get($"Opt.Cleanup.Cat.{category.Id}.Title");
        Detail = Loc.Get($"Opt.Cleanup.Cat.{category.Id}.Detail");
        SizeText = scan.Files == 0 ? Loc.Get("Opt.Cleanup.Nothing") : Loc.Format("Opt.Cleanup.Size", Units.Bytes(scan.Bytes, Loc.Culture), scan.Files.ToString("N0", Loc.Culture));
        NeedsAdministrator = category.NeedsAdministrator && !isElevated;
        CanClean = scan.Files > 0 && !NeedsAdministrator;
        ActionToolTip = NeedsAdministrator ? Loc.Get("Opt.Cleanup.NeedsAdmin.Tip") : Loc.Get("Opt.Cleanup.Action.Clean");
        UnreadableText = scan.Unreadable > 0 ? Loc.Format("Opt.Cleanup.Unreadable", scan.Unreadable.ToString("N0", Loc.Culture)) : string.Empty;
        Badge = NeedsAdministrator ? Loc.Get("Opt.Cleanup.NeedsAdmin") : category.RebuildsByItself ? Loc.Get("Opt.Cleanup.Rebuilds") : string.Empty;
        Tone = NeedsAdministrator ? MetricState.Warning : MetricState.Neutral;
    }

    public CleanupScan Scan { get; }

    public string Id => Scan.Category.Id;

    public string Title { get; }

    public string Detail { get; }

    public string SizeText { get; }

    public string Badge { get; }

    public MetricState Tone { get; }

    public string UnreadableText { get; }

    public string ActionToolTip { get; }

    public bool NeedsAdministrator { get; }

    public bool CanClean { get; }

    public bool HasBadge => Badge.Length > 0;

    public bool HasUnreadable => UnreadableText.Length > 0;

    public string AccessibleName => $"{Title}, {SizeText}";

    public override string ToString() => AccessibleName;
}
