using M1vClock.App.Pages;
using M1vClock.App.Pages.Hardware;
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
