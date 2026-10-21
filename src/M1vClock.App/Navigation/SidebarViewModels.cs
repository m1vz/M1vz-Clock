using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace M1vClock.App.Navigation;

public sealed partial class SidebarItemViewModel : ObservableObject
{
    private readonly NavigationService _navigation;

    public SidebarItemViewModel(PageDescriptor descriptor, NavigationService navigation)
    {
        Descriptor = descriptor;
        _navigation = navigation;
    }

    public PageDescriptor Descriptor { get; }

    public string Title => Descriptor.Title;

    public string IconKey => Descriptor.IconKey;

    public bool IsAvailable => Descriptor.IsAvailable;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Open() => _navigation.Navigate(Descriptor.Id);
}

public sealed partial class SidebarSectionViewModel : ObservableObject
{
    private readonly Action<SidebarSectionViewModel> _onToggled;

    public SidebarSectionViewModel(SidebarSection section, IReadOnlyList<SidebarItemViewModel> items, bool isExpanded, Action<SidebarSectionViewModel> onToggled)
    {
        Section = section;
        Items = items;
        IsExpanded = isExpanded;
        _onToggled = onToggled;
    }

    public SidebarSection Section { get; }

    public string Title => PageCatalog.SectionTitle(Section);

    public bool HasHeader => Section is not SidebarSection.Top;

    public IReadOnlyList<SidebarItemViewModel> Items { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        _onToggled(this);
    }
}
