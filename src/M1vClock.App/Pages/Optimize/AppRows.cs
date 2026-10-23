using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Optimize.Apps;

namespace M1vClock.App.Pages.Optimize;

public sealed class AppRowViewModel
{
    public AppRowViewModel(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        App = app;
        Title = app.Name;
        Detail = string.Join(" · ", new[] { app.Publisher, app.Version }.Where(part => !string.IsNullOrWhiteSpace(part)));
        SizeText = app.SizeBytes is { } bytes ? Units.Bytes(bytes, Loc.Culture) : Loc.Get("Opt.Apps.Meta.SizeUnknown");
        var meta = new List<string>();
        if (app.InstalledOn is { } date)
        {
            meta.Add(Loc.Format("Opt.Apps.Meta.Date", Units.Date(date, Loc.Culture)));
        }

        if (app.Scope == AppScope.User)
        {
            meta.Add(Loc.Get("Opt.Apps.Meta.User"));
        }

        Meta = string.Join(" · ", meta);
        CanUninstall = app.Launch is not null;
        UninstallToolTip = CanUninstall ? Loc.Format("Opt.Apps.Confirm.Line", app.Name) : Loc.Get("Opt.Apps.NoUninstaller");
    }

    public InstalledApp App { get; }

    public string Title { get; }

    public string Detail { get; }

    public string SizeText { get; }

    public string Meta { get; }

    public bool CanUninstall { get; }

    public string UninstallToolTip { get; }

    public bool HasDetail => Detail.Length > 0;

    public bool HasMeta => Meta.Length > 0;

    public string AccessibleName => $"{Title}, {SizeText}";

    public override string ToString() => AccessibleName;
}

public static class AppRows
{
    public const int MaximumShown = 200;

    public static IReadOnlyList<AppRowViewModel> Filter(IReadOnlyList<InstalledApp> apps, AppSort sort, string search, out int matching)
    {
        ArgumentNullException.ThrowIfNull(apps);
        var text = (search ?? string.Empty).Trim();
        var found = apps
            .Where(a => text.Length == 0
                || a.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || (a.Publisher?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .ToList();
        matching = found.Count;
        return InstalledAppService.Sort(found, sort).Take(MaximumShown).Select(a => new AppRowViewModel(a)).ToList();
    }
}
