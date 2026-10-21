using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.Localization;
using M1vClock.App.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Navigation;

public abstract class PageViewModel(PageDescriptor descriptor) : ObservableObject
{
    public PageDescriptor Descriptor { get; } = descriptor;

    public string Title => Descriptor.Title;

    public string Subtitle => Descriptor.Description;

    public virtual void OnNavigatedTo()
    {
    }

    public virtual void OnNavigatedFrom()
    {
    }
}

public sealed class PlaceholderPageViewModel(PageDescriptor descriptor) : PageViewModel(descriptor)
{
    public string Footnote => Loc.Format("Placeholder.Footnote", Descriptor.Phase);
}

public sealed class NavigatedEventArgs(PageViewModel page, int direction) : EventArgs
{
    public PageViewModel Page { get; } = page;

    public int Direction { get; } = direction;
}

public sealed class NavigationService(IServiceProvider services, ILogger<NavigationService> logger)
{
    public event EventHandler<NavigatedEventArgs>? Navigated;

    public PageViewModel? Current { get; private set; }

    public void Navigate(string pageId)
    {
        var descriptor = PageCatalog.Find(pageId);
        if (Current?.Descriptor.Id == descriptor.Id)
        {
            return;
        }

        var previous = Current;
        var direction = previous is null ? 0 : Math.Sign(IndexOf(descriptor) - IndexOf(previous.Descriptor));
        var page = Create(descriptor);
        previous?.OnNavigatedFrom();
        if (previous is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Current = page;
        page.OnNavigatedTo();
        AppLog.Navigated(logger, descriptor.Id);
        Navigated?.Invoke(this, new NavigatedEventArgs(page, direction));
    }

    private PageViewModel Create(PageDescriptor descriptor) => descriptor.Id switch
    {
        "design-system" => services.GetRequiredService<DesignSystemPageViewModel>(),
        "settings" => services.GetRequiredService<SettingsPageViewModel>(),
        "history" => services.GetRequiredService<HistoryPageViewModel>(),
        _ => new PlaceholderPageViewModel(descriptor),
    };

    private static int IndexOf(PageDescriptor descriptor)
    {
        for (var i = 0; i < PageCatalog.All.Count; i++)
        {
            if (PageCatalog.All[i].Id == descriptor.Id)
            {
                return i;
            }
        }

        return 0;
    }
}
