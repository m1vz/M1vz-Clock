using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Hardware;
using M1vClock.Benchmarks.Safety;

namespace M1vClock.App.Pages.Performance;

public sealed record BenchChoice(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class BenchOptionViewModel : ObservableObject
{
    private readonly string _titleKey;

    public BenchOptionViewModel(string id, string titleKey, IReadOnlyList<BenchChoice> choices, int selectedIndex = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentOutOfRangeException.ThrowIfZero(choices.Count);
        Id = id;
        _titleKey = titleKey;
        Choices = choices;
        SelectedIndex = Math.Clamp(selectedIndex, 0, choices.Count - 1);
    }

    public string Id { get; }

    public string Title => Loc.Get(_titleKey);

    public IReadOnlyList<BenchChoice> Choices { get; }

    public double SegmentWidth => Choices.Count * Math.Max(96, Choices.Max(c => c.Label.Length) * 7.4 + 32);

    public string Value => Choices[Math.Clamp(SelectedIndex, 0, Choices.Count - 1)].Value;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    partial void OnSelectedIndexChanged(int value) => OnPropertyChanged(nameof(Value));
}

public readonly record struct BenchPoll(string PhaseKey, double Fraction, string Detail)
{
    public static readonly BenchPoll Idle = new(string.Empty, 0, string.Empty);
}

public sealed record BenchJobDraft(
    string Test,
    int ScoreVersion,
    double Score,
    IReadOnlyDictionary<string, double> Metrics,
    bool Sustained = false);

public sealed record BenchJobOutcome(IReadOnlyList<BenchJobDraft> Drafts, IReadOnlyList<InfoRowViewModel> Rows, IReadOnlyList<string> Notes);

public interface IBenchJob
{
    string Id { get; }

    string IconKey { get; }

    string Title { get; }

    string Description { get; }

    IReadOnlyList<BenchOptionViewModel> Options { get; }

    IReadOnlyList<string> HistoryTests { get; }

    ThermalComponent? Thermal { get; }

    IPreviewSource? Preview { get; }

    string? UnavailableReason { get; }

    void Refresh();

    BenchPoll Poll();

    Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken);
}
