using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace M1vClock.App.Dialogs;

public enum DialogTone
{
    Neutral,
    Destructive,
    Error,
}

public sealed record DialogRequest(
    string Title,
    string Message,
    IReadOnlyList<string> Changes,
    string? Undo,
    string ConfirmText,
    string? CancelText,
    DialogTone Tone)
{
    public bool HasChanges => Changes.Count > 0;

    public bool HasUndo => !string.IsNullOrWhiteSpace(Undo);

    public bool HasCancel => !string.IsNullOrWhiteSpace(CancelText);

    public bool IsDestructive => Tone == DialogTone.Destructive;

    public bool IsError => Tone == DialogTone.Error;
}

public sealed partial class DialogService : ObservableObject
{
    private TaskCompletionSource<bool>? _pending;

    public event EventHandler? Opened;

    public event EventHandler? Closing;

    [ObservableProperty]
    public partial DialogRequest? Current { get; private set; }

    public FrameworkElement? Anchor { get; private set; }

    public Task<bool> ShowAsync(DialogRequest request, FrameworkElement? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        _pending?.TrySetResult(false);
        _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Anchor = anchor;
        Current = request;
        Opened?.Invoke(this, EventArgs.Empty);
        return _pending.Task;
    }

    [RelayCommand]
    private void Confirm() => Complete(true);

    [RelayCommand]
    private void Cancel() => Complete(false);

    private void Complete(bool result)
    {
        if (_pending is null)
        {
            return;
        }

        var pending = _pending;
        _pending = null;
        Closing?.Invoke(this, EventArgs.Empty);
        pending.TrySetResult(result);
    }

    public void ClearAfterClose()
    {
        if (_pending is null)
        {
            Current = null;
            Anchor = null;
        }
    }
}
