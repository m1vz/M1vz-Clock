using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Games.Estimate;
using M1vClock.Games.Library;
using M1vClock.Games.Settings;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Games;

public sealed partial class FpsRowViewModel : ObservableObject
{
    public FpsRowViewModel(CatalogGame game, bool installed)
    {
        ArgumentNullException.ThrowIfNull(game);
        Game = game;
        IsInstalled = installed;
    }

    public CatalogGame Game { get; }

    public string Title => Game.Name;

    public string Genre => Game.Genre;

    public bool IsInstalled { get; }

    public string InstalledText { get; } = Loc.Get("Game.Fps.Installed");

    [ObservableProperty]
    public partial string FpsText { get; set; } = "—";

    [ObservableProperty]
    public partial string LowText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RatingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState Tone { get; set; } = MetricState.Neutral;

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Bar { get; set; }

    public string AccessibleName => $"{Title}, {FpsText} FPS, {RatingText}, {Detail}";

    public void Show(double average, double low, FpsRating rating, string detail)
    {
        FpsText = average.ToString("N0", Loc.Culture);
        LowText = low > 0 ? Loc.Format("Game.Fps.Low", low.ToString("N0", Loc.Culture)) : string.Empty;
        RatingText = Loc.Get($"Game.Fps.Rating.{rating}");
        Tone = rating switch
        {
            FpsRating.Excellent or FpsRating.Great => MetricState.Good,
            FpsRating.Playable => MetricState.Neutral,
            FpsRating.Weak => MetricState.Warning,
            _ => MetricState.Critical,
        };
        Detail = detail;
        Bar = Math.Clamp(average / 240.0, 0, 1);
        OnPropertyChanged(nameof(AccessibleName));
    }

    public override string ToString() => AccessibleName;
}

public sealed record FpsServices(IMachineProfile Machine, IGameLibraryService Library, IReferenceFpsSource Reference, IGameSettings Settings);

public sealed partial class FpsPageViewModel : HardwarePageViewModel
{
    private static readonly EstimateResolution[] Resolutions = [EstimateResolution.FullHd, EstimateResolution.QuadHd, EstimateResolution.UltraHd];
    private static readonly EstimatePreset[] Presets = [EstimatePreset.Low, EstimatePreset.Medium, EstimatePreset.High, EstimatePreset.Ultra];
    private static readonly EstimateUpscaling[] Upscalings = [EstimateUpscaling.Off, EstimateUpscaling.Quality, EstimateUpscaling.Performance];

    private readonly FpsServices _services;
    private readonly ILogger<FpsPageViewModel> _logger;
    private readonly List<FpsRowViewModel> _all = [];
    private double? _gpuIndex;
    private double? _cpuIndex;
    private string _cpu = string.Empty;
    private string _gpu = string.Empty;
    private CancellationTokenSource? _query;

    public FpsPageViewModel(FpsServices services, ILogger<FpsPageViewModel> logger)
        : base(PageCatalog.Find("fps"), logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _logger = logger;
        ResolutionLabels = Resolutions.Select(r => Loc.Get($"Game.Fps.Resolution.{r}")).ToList();
        PresetLabels = Presets.Select(p => Loc.Get($"Game.Fps.Preset.{p}")).ToList();
        UpscalingLabels = Upscalings.Select(u => Loc.Get($"Game.Fps.Upscaling.{u}")).ToList();
        PresetIndex = 2;
        RefreshKeyState();
    }

    public ObservableCollection<FpsRowViewModel> Items { get; } = [];

    public IReadOnlyList<string> ResolutionLabels { get; }

    public IReadOnlyList<string> PresetLabels { get; }

    public IReadOnlyList<string> UpscalingLabels { get; }

    [ObservableProperty]
    public partial int ResolutionIndex { get; set; }

    [ObservableProperty]
    public partial int PresetIndex { get; set; }

    [ObservableProperty]
    public partial int UpscalingIndex { get; set; }

    [ObservableProperty]
    public partial bool OnlyInstalled { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string CpuText { get; set; } = "—";

    [ObservableProperty]
    public partial string GpuText { get; set; } = "—";

    [ObservableProperty]
    public partial string CpuDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GpuDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string KeyText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasKey { get; set; }

    [ObservableProperty]
    public partial bool IsQuerying { get; set; }

    public bool HasProblem => Problem.Length > 0;

    public bool HasNotice => Notice.Length > 0;

    public bool CanEstimate => _gpuIndex is not null && _cpuIndex is not null;

    public bool CanQuery => HasKey && !IsQuerying && Items.Count > 0;

    public string KeyStateText => Loc.Get(HasKey ? "Game.Fps.Key.Saved" : "Game.Fps.Key.None");

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    public override void OnNavigatedFrom()
    {
        CancelQuery();
        base.OnNavigatedFrom();
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var machineTask = _services.Machine.GetAsync(cancellationToken);
            var libraryTask = _services.Library.ReadAsync(cancellationToken);
            var machine = await machineTask.ConfigureAwait(true);
            IReadOnlyList<GameEntry> installed;
            try
            {
                installed = (await libraryTask.ConfigureAwait(true)).Games;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLog.PageLoadFailed(_logger, ex, "fps-library");
                installed = [];
            }

            _cpu = machine.Cpu;
            _gpu = machine.Gpu;
            _cpuIndex = FpsEstimator.CpuIndexOf(_cpu);
            _gpuIndex = FpsEstimator.GpuIndexOf(_gpu);
            CpuText = string.IsNullOrWhiteSpace(_cpu) ? "—" : FpsEstimator.ShortCpuName(_cpu);
            GpuText = string.IsNullOrWhiteSpace(_gpu) ? "—" : FpsEstimator.ShortGpuName(_gpu);
            CpuDetail = Describe(_cpuIndex, "Ryzen 7 7700X");
            GpuDetail = Describe(_gpuIndex, "RTX 4070");
            Problem = CanEstimate ? string.Empty : Loc.Get(_gpuIndex is null && _cpuIndex is null ? "Game.Fps.Unknown.Both" : _gpuIndex is null ? "Game.Fps.Unknown.Gpu" : "Game.Fps.Unknown.Cpu");
            _all.Clear();
            foreach (var game in FpsEstimator.Games)
            {
                _all.Add(new FpsRowViewModel(game, installed.Any(g => FpsEstimator.SameGame(game.Name, g.Name))));
            }

            Recalculate();
            OnPropertyChanged(nameof(CanEstimate));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    public void SaveKey()
    {
        if (string.IsNullOrWhiteSpace(KeyText))
        {
            return;
        }

        FpsApiKey.Save(_services.Settings, KeyText);
        KeyText = string.Empty;
        RefreshKeyState();
        Notice = Loc.Get("Game.Fps.Key.Done");
    }

    [RelayCommand]
    public void RemoveKey()
    {
        FpsApiKey.Save(_services.Settings, null);
        RefreshKeyState();
        Notice = Loc.Get("Game.Fps.Key.Removed");
        Recalculate();
    }

    [RelayCommand]
    public async Task QueryAsync()
    {
        if (!CanQuery)
        {
            return;
        }

        CancelQuery();
        var cancel = new CancellationTokenSource();
        _query = cancel;
        IsQuerying = true;
        Notice = string.Empty;
        var settings = CurrentSettings();
        var cpu = FpsEstimator.ShortCpuName(_cpu);
        var gpu = FpsEstimator.ShortGpuName(_gpu);
        var answered = 0;
        try
        {
            foreach (var row in Items.ToList())
            {
                try
                {
                    var reference = await _services.Reference.GetAsync(new ReferenceQuery(cpu, gpu, row.Game.Name, settings.Resolution, settings.Upscaling), cancel.Token).ConfigureAwait(true);
                    var rating = new FpsEstimate(reference.Average, reference.Low1, FpsLimiter.Gpu).Rating;
                    row.Show(Math.Round(reference.Average), Math.Round(reference.Low1), rating, Loc.Get(reference.FromCache ? "Game.Fps.Source.Cached" : "Game.Fps.Source.Service"));
                    answered++;
                }
                catch (ReferenceFpsException ex) when (ex.Failure is ReferenceFailure.NotFound)
                {
                    AppLog.FpsReferenceFailed(_logger, ex, row.Title);
                }
                catch (ReferenceFpsException ex)
                {
                    AppLog.FpsReferenceFailed(_logger, ex, row.Title);
                    Problem = Loc.Get($"Game.Fps.Failure.{ex.Failure}");
                    break;
                }
            }

            Notice = Loc.Format("Game.Fps.Query.Done", answered.ToString("N0", Loc.Culture), Items.Count.ToString("N0", Loc.Culture));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            IsQuerying = false;
            if (ReferenceEquals(_query, cancel))
            {
                _query = null;
            }

            cancel.Dispose();
        }
    }

    partial void OnResolutionIndexChanged(int value) => Recalculate();

    partial void OnPresetIndexChanged(int value) => Recalculate();

    partial void OnUpscalingIndexChanged(int value) => Recalculate();

    partial void OnOnlyInstalledChanged(bool value) => Recalculate();

    partial void OnSearchTextChanged(string value) => Recalculate();

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnHasKeyChanged(bool value)
    {
        OnPropertyChanged(nameof(KeyStateText));
        OnPropertyChanged(nameof(CanQuery));
    }

    partial void OnIsQueryingChanged(bool value) => OnPropertyChanged(nameof(CanQuery));

    private static string Describe(double? index, string reference) =>
        index is { } value
            ? Loc.Format("Game.Fps.Machine.Indexed", value.ToString("N0", Loc.Culture), reference)
            : Loc.Get("Game.Fps.Machine.Unknown");

    private void RefreshKeyState() => HasKey = _services.Reference.IsConfigured;

    private EstimateSettings CurrentSettings() => new(
        Resolutions[Math.Clamp(ResolutionIndex, 0, Resolutions.Length - 1)],
        Presets[Math.Clamp(PresetIndex, 0, Presets.Length - 1)],
        Upscalings[Math.Clamp(UpscalingIndex, 0, Upscalings.Length - 1)]);

    private void CancelQuery()
    {
        _query?.Cancel();
    }

    private void Recalculate()
    {
        CancelQuery();
        Items.Clear();
        var text = (SearchText ?? string.Empty).Trim();
        var settings = CurrentSettings();
        foreach (var row in _all.Where(r => (!OnlyInstalled || r.IsInstalled) && (text.Length == 0 || r.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase))).OrderByDescending(r => r.IsInstalled))
        {
            if (_gpuIndex is { } gpu && _cpuIndex is { } cpu)
            {
                var estimate = FpsEstimator.Estimate(row.Game, gpu, cpu, settings);
                row.Show(estimate.Average, estimate.Low1, estimate.Rating, Loc.Format("Game.Fps.Source.Local", Loc.Get($"Game.Fps.Limiter.{estimate.Limiter}")));
            }

            Items.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanQuery));
    }
}
