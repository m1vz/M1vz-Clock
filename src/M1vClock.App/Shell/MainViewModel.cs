using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.Dialogs;
using M1vClock.App.Hosting;
using M1vClock.App.Navigation;
using M1vClock.App.Search;
using M1vClock.App.Services;

namespace M1vClock.App.Shell;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettingsService _settings;

    public MainViewModel(
        NavigationService navigation,
        CommandPaletteViewModel palette,
        DialogService dialogs,
        AppSettingsService settings,
        AppInfo info)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(settings);
        Navigation = navigation;
        Palette = palette;
        Dialogs = dialogs;
        Info = info;
        _settings = settings;

        var collapsed = settings.Current.CollapsedSidebarSections.ToHashSet(StringComparer.Ordinal);
        var items = PageCatalog.All.ToDictionary(p => p.Id, p => new SidebarItemViewModel(p, navigation));
        Sections = PageCatalog.All
            .Where(p => p.Section != SidebarSection.App)
            .GroupBy(p => p.Section)
            .Select(g => new SidebarSectionViewModel(
                g.Key,
                g.Select(p => items[p.Id]).ToList(),
                !collapsed.Contains(g.Key.ToString()),
                OnSectionToggled))
            .ToList();
        FooterItems = PageCatalog.All.Where(p => p.Section == SidebarSection.App).Select(p => items[p.Id]).ToList();
        AllItems = items.Values.ToList();
        navigation.Navigated += (_, e) =>
        {
            foreach (var item in AllItems)
            {
                item.IsSelected = item.Descriptor.Id == e.Page.Descriptor.Id;
            }

            var section = Sections.FirstOrDefault(s => s.Items.Any(i => i.IsSelected));
            if (section is { IsExpanded: false })
            {
                section.IsExpanded = true;
                OnSectionToggled(section);
            }

            CurrentPage = e.Page;
        };
    }

    public NavigationService Navigation { get; }

    public CommandPaletteViewModel Palette { get; }

    public DialogService Dialogs { get; }

    public AppInfo Info { get; }

    public IReadOnlyList<SidebarSectionViewModel> Sections { get; }

    public IReadOnlyList<SidebarItemViewModel> FooterItems { get; }

    public IReadOnlyList<SidebarItemViewModel> AllItems { get; }

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; private set; }

    private void OnSectionToggled(SidebarSectionViewModel section)
    {
        var collapsed = Sections.Where(s => !s.IsExpanded).Select(s => s.Section.ToString()).ToList();
        _settings.Update(s => s.CollapsedSidebarSections = collapsed);
    }
}
