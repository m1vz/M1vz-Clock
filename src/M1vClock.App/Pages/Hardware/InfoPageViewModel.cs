using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.Navigation;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public abstract partial class InfoPageViewModel : HardwarePageViewModel
{
    protected InfoPageViewModel(PageDescriptor descriptor, ILogger logger)
        : base(descriptor, logger)
    {
    }

    public ObservableCollection<InfoSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    public partial string FooterNote { get; set; } = string.Empty;

    protected void Add(InfoSectionViewModel section)
    {
        ArgumentNullException.ThrowIfNull(section);
        section.RetryCommand = RefreshCommand;
        Sections.Add(section);
    }

    protected void TrimSectionsTo(int count)
    {
        while (Sections.Count > count)
        {
            Sections.RemoveAt(Sections.Count - 1);
        }
    }
}
