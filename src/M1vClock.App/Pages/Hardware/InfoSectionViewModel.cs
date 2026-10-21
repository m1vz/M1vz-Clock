using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Core.Privacy;
using M1vClock.Core.Results;

namespace M1vClock.App.Pages.Hardware;

public enum SectionState
{
    Loading,
    Ready,
    Failed,
}

public sealed partial class InfoRowViewModel(string label, string value, bool isSensitive = false) : ObservableObject
{
    public string Label { get; } = label;

    public string Value { get; } = value;

    public bool IsSensitive { get; } = isSensitive;

    public bool CanReveal => IsSensitive && Value != Units.Missing;

    public string Display => !CanReveal || IsRevealed ? Value : SensitiveText.Mask;

    public string RevealToolTip => Loc.Get(IsRevealed ? "Hw.Common.Hide" : "Hw.Common.Show");

    [ObservableProperty]
    public partial bool IsRevealed { get; set; }

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(RevealToolTip));
    }
}

public sealed partial class InfoSectionViewModel(string title, string iconKey) : ObservableObject
{
    public string Title { get; } = title;

    public string IconKey { get; } = iconKey;

    public ObservableCollection<InfoRowViewModel> Rows { get; } = [];

    public ObservableCollection<string> Notes { get; } = [];

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public bool HasNotes => Notes.Count > 0;

    public bool IsLoading => State == SectionState.Loading;

    public bool IsReady => State == SectionState.Ready;

    public bool IsFailed => State == SectionState.Failed;

    public string ErrorDetail => Loc.Format("Hw.Common.ReadError.Detail", Error);

    [ObservableProperty]
    public partial SectionState State { get; set; } = SectionState.Loading;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IRelayCommand? RetryCommand { get; set; }

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState StatusTone { get; set; }

    public void SetNotes(IEnumerable<string> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        Notes.Clear();
        foreach (var note in notes)
        {
            Notes.Add(note);
        }

        OnPropertyChanged(nameof(HasNotes));
    }

    public void SetLoading()
    {
        Rows.Clear();
        SetNotes([]);
        Error = string.Empty;
        State = SectionState.Loading;
    }

    public void SetReady(IEnumerable<InfoRowViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        Error = string.Empty;
        State = SectionState.Ready;
    }

    public void SetFailed(string error)
    {
        Rows.Clear();
        SetNotes([]);
        Error = error;
        State = SectionState.Failed;
    }

    public void Apply<T>(ReadResult<T> result, Func<T, IEnumerable<InfoRowViewModel>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (result.TryGet(out var value))
        {
            SetReady(rows(value));
        }
        else
        {
            SetFailed(result.Error ?? Loc.Get("Hw.Common.Unknown"));
        }
    }

    partial void OnStateChanged(SectionState value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsFailed));
    }

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(ErrorDetail));

    partial void OnSubtitleChanged(string value) => OnPropertyChanged(nameof(HasSubtitle));

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
}
