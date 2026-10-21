using M1vClock.App.Localization;

namespace M1vClock.App.Navigation;

public enum SidebarSection
{
    Top,
    Hardware,
    Performance,
    Games,
    System,
    Devices,
    App,
}

public sealed record PageDescriptor(string Id, SidebarSection Section, string Icon, int Phase)
{
    public string Title => Loc.Get($"Page.{Key}.Title");

    public string Description => Loc.Get($"Page.{Key}.Description");

    public IReadOnlyList<string> Keywords =>
        Loc.Get($"Page.{Key}.Keywords").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public string Key => string.Concat(Id.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    public string IconKey => $"Icon.{Icon}";

    public bool IsAvailable => Phase <= PageCatalog.CurrentPhase;
}

public static class PageCatalog
{
    public const int CurrentPhase = 1;

    public static IReadOnlyList<PageDescriptor> All { get; } =
    [
        new("overview", SidebarSection.Top, "Overview", 2),
        new("system", SidebarSection.Hardware, "System", 2),
        new("sensors", SidebarSection.Hardware, "Sensors", 2),
        new("gpu", SidebarSection.Hardware, "Gpu", 2),
        new("memory", SidebarSection.Hardware, "Memory", 2),
        new("storage", SidebarSection.Hardware, "Storage", 2),
        new("power", SidebarSection.Hardware, "Power", 2),
        new("network", SidebarSection.Hardware, "Network", 2),
        new("devices", SidebarSection.Hardware, "Devices", 2),
        new("benchmarks", SidebarSection.Performance, "Benchmarks", 3),
        new("stress", SidebarSection.Performance, "Stress", 3),
        new("overclock", SidebarSection.Performance, "Overclock", 5),
        new("library", SidebarSection.Games, "Library", 5),
        new("fps", SidebarSection.Games, "Fps", 5),
        new("bottleneck", SidebarSection.Games, "Bottleneck", 5),
        new("overlay", SidebarSection.Games, "Overlay", 5),
        new("cleanup", SidebarSection.System, "Cleanup", 4),
        new("standby", SidebarSection.System, "Standby", 4),
        new("tweaks", SidebarSection.System, "Tweaks", 4),
        new("services", SidebarSection.System, "Services", 4),
        new("startup", SidebarSection.System, "Startup", 4),
        new("apps", SidebarSection.System, "Apps", 4),
        new("repair", SidebarSection.System, "Repair", 4),
        new("events", SidebarSection.System, "Events", 4),
        new("crashes", SidebarSection.System, "Crashes", 4),
        new("reliability", SidebarSection.System, "Reliability", 4),
        new("logs", SidebarSection.System, "Logs", 4),
        new("report", SidebarSection.System, "Report", 4),
        new("drivers", SidebarSection.Devices, "Drivers", 6),
        new("rgb", SidebarSection.Devices, "Rgb", 6),
        new("maker-ports", SidebarSection.Devices, "MakerPorts", 6),
        new("maker-drivers", SidebarSection.Devices, "MakerDrivers", 6),
        new("history", SidebarSection.App, "History", 1),
        new("settings", SidebarSection.App, "Settings", 1),
        new("design-system", SidebarSection.App, "DesignSystem", 1),
    ];

    public static PageDescriptor Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Página desconhecida: {id}");

    public static string SectionTitle(SidebarSection section) => Loc.Get($"Section.{section}");
}
