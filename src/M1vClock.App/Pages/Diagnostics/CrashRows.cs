using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Hardware;
using M1vClock.Diagnostics.Crashes;

namespace M1vClock.App.Pages.Diagnostics;

public sealed class CrashRowViewModel(string title, string detail, string meta, string badge, MetricState tone, string trailingTop, string trailingBottom)
{
    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string Meta { get; } = meta;

    public string Badge { get; } = badge;

    public MetricState Tone { get; } = tone;

    public string TrailingTop { get; } = trailingTop;

    public string TrailingBottom { get; } = trailingBottom;

    public bool HasDetail => Detail.Length > 0;

    public bool HasMeta => Meta.Length > 0;

    public bool HasBadge => Badge.Length > 0;

    public bool HasTrailing => TrailingTop.Length > 0 || TrailingBottom.Length > 0;

    public bool HasBadgeOnly => HasBadge && !HasTrailing;

    public string AccessibleName => string.Join(". ", new[] { Badge, Title, TrailingTop, TrailingBottom }.Where(s => s.Length > 0));

    public override string ToString() => AccessibleName;
}

public static class CrashRows
{
    public static string Time(DateTimeOffset time) => time.ToLocalTime().ToString("g", Loc.Culture);

    public static CrashRowViewModel Shutdown(UnexpectedShutdown shutdown)
    {
        ArgumentNullException.ThrowIfNull(shutdown);
        var when = Time(shutdown.Time);
        switch (shutdown.Cause)
        {
            case ShutdownCause.BlueScreen when shutdown.BugCheck is { } bugCheck:
                return new CrashRowViewModel(
                    bugCheck.Name is { } name
                        ? Loc.Format("Diag.Crashes.Cause.BlueScreen", name)
                        : Loc.Format("Diag.Crashes.Cause.BlueScreen.Unnamed", bugCheck.Hex),
                    BugCheckDetail(bugCheck),
                    Loc.Format("Diag.Crashes.Dump.Meta", bugCheck.Hex, Loc.Format("Diag.Crashes.Parameters", string.Join(", ", bugCheck.Parameters.Select(p => $"0x{p:X}")))),
                    Loc.Get("Diag.Severity.Critical"),
                    MetricState.Critical,
                    when,
                    string.Empty);
            case ShutdownCause.PowerButton:
                return new CrashRowViewModel(Loc.Get("Diag.Crashes.Cause.PowerButton"), Loc.Get("Diag.Crashes.Cause.PowerButton.Detail"), string.Empty, string.Empty, MetricState.Neutral, when, string.Empty);
            default:
                return new CrashRowViewModel(Loc.Get("Diag.Crashes.Cause.Unknown"), Loc.Get("Diag.Crashes.Cause.Unknown.Detail"), string.Empty, Loc.Get("Diag.Severity.Warning"), MetricState.Warning, when, string.Empty);
        }
    }

    public static CrashRowViewModel App(AppCrashGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var self = string.Equals(group.App, group.Module, StringComparison.OrdinalIgnoreCase);
        var title = self ? Loc.Format("Diag.Crashes.Apps.TitleSelf", group.App) : Loc.Format("Diag.Crashes.Apps.TitleIn", group.App, group.Module);
        var origin = Loc.Format($"Diag.Crashes.Origin.{group.Origin}", group.Vendor ?? string.Empty);
        var detail = group.ExceptionName is { } name ? $"{origin} {Loc.Get($"Diag.Exception.{name}.Detail")}" : origin;
        var exception = Loc.Format("Diag.Crashes.Apps.Exception", group.ExceptionHex);
        return new CrashRowViewModel(
            title,
            detail,
            group.ExceptionName is { } code ? Loc.Format("Diag.Crashes.Dump.Meta", exception, code) : exception,
            group.Origin == FaultOrigin.GraphicsDriver ? Loc.Get("Diag.Severity.Warning") : string.Empty,
            group.Origin == FaultOrigin.GraphicsDriver ? MetricState.Warning : MetricState.Neutral,
            Loc.Format("Diag.Events.Count", group.Count.ToString("N0", Loc.Culture)),
            Loc.Format("Diag.Events.Last", Time(group.Last)));
    }

    public static CrashRowViewModel Dump(DumpFileInfo dump)
    {
        ArgumentNullException.ThrowIfNull(dump);
        var size = Units.Bytes(dump.Length, Loc.Culture);
        var meta = Loc.Format("Diag.Crashes.Dump.Meta", size, Time(dump.Modified));
        if (dump.BugCheck is not { } bugCheck)
        {
            return new CrashRowViewModel(dump.Name, string.Empty, meta, string.Empty, MetricState.Neutral, string.Empty, string.Empty);
        }

        var title = bugCheck.Name is { } name ? $"{dump.Name} · {name}" : $"{dump.Name} · {bugCheck.Hex}";
        return new CrashRowViewModel(title, BugCheckDetail(bugCheck), meta, Loc.Get("Diag.Severity.Critical"), MetricState.Critical, string.Empty, string.Empty);
    }

    public static IReadOnlyList<InfoRowViewModel> Settings(DumpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return
        [
            new InfoRowViewModel(Loc.Get("Diag.Crashes.Settings.Mode"), Loc.Get($"Diag.Dump.Mode.{settings.Mode}")),
            new InfoRowViewModel(Loc.Get("Diag.Crashes.Settings.Folder"), settings.MinidumpDirectory),
            new InfoRowViewModel(Loc.Get("Diag.Crashes.Settings.File"), settings.DumpFile),
            new InfoRowViewModel(Loc.Get("Diag.Crashes.Settings.AutoReboot"), Loc.Get(settings.AutoReboot ? "Hw.Common.Yes" : "Hw.Common.No")),
            new InfoRowViewModel(Loc.Get("Diag.Crashes.Settings.LogEvent"), Loc.Get(settings.LogsEvent ? "Hw.Common.Yes" : "Hw.Common.No")),
        ];
    }

    public static string Advice(DumpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Mode switch
        {
            DumpMode.Disabled => Loc.Get("Diag.Crashes.Settings.Advice.Off"),
            DumpMode.Unknown => Loc.Get("Diag.Crashes.Settings.Advice.Unknown"),
            _ => string.Empty,
        };
    }

    private static string BugCheckDetail(BugCheckInfo bugCheck) =>
        bugCheck.Name is { } name ? Loc.Get($"Diag.BugCheck.{name}.Detail") : Loc.Get("Diag.Crashes.Cause.BlueScreen.NoDescription");
}
