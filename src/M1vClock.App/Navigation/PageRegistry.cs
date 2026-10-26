using M1vClock.App.Pages;
using M1vClock.App.Pages.Diagnostics;
using M1vClock.App.Pages.Games;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Pages.Optimize;
using M1vClock.App.Pages.Performance;
using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.App.Navigation;

public static class PageRegistry
{
    private static readonly Dictionary<string, Func<IServiceProvider, PageViewModel>> Factories = new(StringComparer.Ordinal)
    {
        ["design-system"] = Resolve<DesignSystemPageViewModel>,
        ["settings"] = Resolve<SettingsPageViewModel>,
        ["history"] = Resolve<HistoryPageViewModel>,
        ["system"] = Resolve<SystemPageViewModel>,
        ["gpu"] = Resolve<GpuPageViewModel>,
        ["power"] = Resolve<PowerPageViewModel>,
        ["memory"] = Resolve<MemoryPageViewModel>,
        ["storage"] = Resolve<StoragePageViewModel>,
        ["network"] = Resolve<NetworkPageViewModel>,
        ["devices"] = Resolve<DevicesPageViewModel>,
        ["sensors"] = Resolve<SensorsPageViewModel>,
        ["overview"] = Resolve<OverviewPageViewModel>,
        ["benchmarks"] = Resolve<BenchmarksPageViewModel>,
        ["stress"] = Resolve<StressPageViewModel>,
        ["events"] = Resolve<EventsPageViewModel>,
        ["crashes"] = Resolve<CrashesPageViewModel>,
        ["reliability"] = Resolve<ReliabilityPageViewModel>,
        ["logs"] = Resolve<LogsPageViewModel>,
        ["report"] = Resolve<ReportPageViewModel>,
        ["startup"] = Resolve<StartupPageViewModel>,
        ["services"] = Resolve<ServicesPageViewModel>,
        ["cleanup"] = Resolve<CleanupPageViewModel>,
        ["standby"] = Resolve<StandbyPageViewModel>,
        ["apps"] = Resolve<AppsPageViewModel>,
        ["tweaks"] = Resolve<TweaksPageViewModel>,
        ["repair"] = Resolve<RepairPageViewModel>,
        ["library"] = Resolve<LibraryPageViewModel>,
        ["bottleneck"] = Resolve<BottleneckPageViewModel>,
        ["fps"] = Resolve<FpsPageViewModel>,
    };

    public static IReadOnlyCollection<string> Ids => Factories.Keys;

    public static bool Has(string id) => Factories.ContainsKey(id);

    public static PageViewModel Create(PageDescriptor descriptor, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(services);
        return Factories.TryGetValue(descriptor.Id, out var factory) ? factory(services) : new PlaceholderPageViewModel(descriptor);
    }

    private static T Resolve<T>(IServiceProvider services)
        where T : PageViewModel => services.GetRequiredService<T>();
}
