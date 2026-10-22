using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace M1vClock.App.Services;

public enum ToastTone
{
    Info,
    Success,
    Warning,
}

public sealed record ToastRequest(string Title, string Text, string IconKey, ToastTone Tone = ToastTone.Info);

public interface INotificationService
{
    void Show(ToastRequest request);
}

public sealed partial class ToastViewModel : ObservableObject
{
    private readonly Action<ToastViewModel> _dismiss;

    public ToastViewModel(ToastRequest request, Action<ToastViewModel> dismiss)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dismiss);
        Title = request.Title;
        Text = request.Text;
        IconKey = request.IconKey;
        Tone = request.Tone;
        _dismiss = dismiss;
    }

    public string Title { get; }

    public string Text { get; }

    public string IconKey { get; }

    public ToastTone Tone { get; }

    [ObservableProperty]
    public partial bool IsExiting { get; set; }

    [RelayCommand]
    private void Dismiss() => _dismiss(this);
}

public sealed class NotificationService(IUiThread ui, TimeProvider clock) : INotificationService
{
    public const int MaxVisible = 3;

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(250);

    private readonly Dictionary<ToastViewModel, List<ITimer>> _timers = [];

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public void Show(ToastRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ui.Post(() => Add(request));
    }

    private void Add(ToastRequest request)
    {
        var toast = new ToastViewModel(request, Dismiss);
        Toasts.Add(toast);
        Schedule(toast, Lifetime, () => Dismiss(toast));
        while (Toasts.Count(t => !t.IsExiting) > MaxVisible)
        {
            Dismiss(Toasts.First(t => !t.IsExiting));
        }
    }

    private void Dismiss(ToastViewModel toast)
    {
        if (toast.IsExiting || !Toasts.Contains(toast))
        {
            return;
        }

        toast.IsExiting = true;
        Schedule(toast, ExitDuration, () => Remove(toast));
    }

    private void Remove(ToastViewModel toast)
    {
        Toasts.Remove(toast);
        if (_timers.Remove(toast, out var timers))
        {
            foreach (var timer in timers)
            {
                timer.Dispose();
            }
        }
    }

    private void Schedule(ToastViewModel toast, TimeSpan delay, Action action)
    {
        var timer = clock.CreateTimer(_ => ui.Post(action), null, delay, Timeout.InfiniteTimeSpan);
        if (!_timers.TryGetValue(toast, out var timers))
        {
            timers = [];
            _timers[toast] = timers;
        }

        timers.Add(timer);
    }
}
