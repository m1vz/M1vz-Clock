using System.Windows;

namespace M1vClock.App.Services;

public interface IUiThread
{
    void Post(Action action);
}

public sealed class WpfUiThread : IUiThread
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
