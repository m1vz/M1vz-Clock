using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Services;
using M1vClock.Hardware.Sensors;

namespace M1vClock.App.Pages.Hardware;

public sealed partial class SensorsPageViewModel : PageViewModel, IDisposable
{
    private readonly ISensorService _sensors;
    private readonly IPawnIoProbe _pawnIo;
    private readonly AppInfo _info;
    private readonly AppSettingsService _settings;
    private readonly IUiThread _ui;
    private readonly Dictionary<string, SensorRowViewModel> _rows = new(StringComparer.Ordinal);
    private IDisposable? _lease;
    private SensorSnapshot? _shown;

    public SensorsPageViewModel(ISensorService sensors, IPawnIoProbe pawnIo, AppInfo info, AppSettingsService settings, IUiThread ui)
        : base(PageCatalog.Find("sensors"))
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(pawnIo);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ui);
        _sensors = sensors;
        _pawnIo = pawnIo;
        _info = info;
        _settings = settings;
        _ui = ui;
    }

    public ObservableCollection<SensorGroupViewModel> Groups { get; } = [];

    public IReadOnlyList<string> FilterLabels { get; } = [.. Enum.GetValues<SensorFilterKind>().Select(SensorRows.FilterLabel)];

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public bool HasNoticeCommand => !string.IsNullOrEmpty(NoticeCommand);

    public bool HasNoMatches => !IsWaiting && !IsFailed && Groups.Count == 0;

    public string FreezeText => Loc.Get(IsFrozen ? "Hw.Sensor.Resume" : "Hw.Sensor.Freeze");

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int FilterIndex { get; set; }

    [ObservableProperty]
    public partial bool IsFrozen { get; set; }

    [ObservableProperty]
    public partial bool IsWaiting { get; set; } = true;

    [ObservableProperty]
    public partial bool IsFailed { get; set; }

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NoticeCommand { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    public override void OnNavigatedTo()
    {
        _sensors.SnapshotReady += OnSnapshot;
        _sensors.StateChanged += OnStateChanged;
        _lease ??= _sensors.Subscribe();
        UpdateState();
        if (_sensors.Latest is { } latest)
        {
            Apply(latest);
        }
    }

    public override void OnNavigatedFrom() => Detach();

    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private void ToggleFreeze()
    {
        IsFrozen = !IsFrozen;
        if (IsFrozen && _shown is { } frozen)
        {
            Summary = Loc.Format("Hw.Sensor.Summary.Frozen", frozen.Readings.Count.ToString(Loc.Culture), frozen.At.LocalDateTime.ToString("T", Loc.Culture));
        }
        else if (!IsFrozen && _sensors.Latest is { } latest)
        {
            Apply(latest);
        }
    }

    partial void OnQueryChanged(string value) => Rebuild();

    partial void OnFilterIndexChanged(int value) => Rebuild();

    partial void OnIsFrozenChanged(bool value) => OnPropertyChanged(nameof(FreezeText));

    partial void OnIsWaitingChanged(bool value) => OnPropertyChanged(nameof(HasNoMatches));

    partial void OnIsFailedChanged(bool value) => OnPropertyChanged(nameof(HasNoMatches));

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnNoticeCommandChanged(string value) => OnPropertyChanged(nameof(HasNoticeCommand));

    private void Detach()
    {
        _sensors.SnapshotReady -= OnSnapshot;
        _sensors.StateChanged -= OnStateChanged;
        _lease?.Dispose();
        _lease = null;
    }

    private void OnSnapshot(object? sender, SensorSnapshot snapshot) => _ui.Post(() => Apply(snapshot));

    private void OnStateChanged(object? sender, EventArgs e) => _ui.Post(UpdateState);

    private void UpdateState()
    {
        var state = _sensors.State;
        IsFailed = state == SensorServiceState.Failed;
        IsWaiting = _shown is null && !IsFailed;
    }

    private void Apply(SensorSnapshot snapshot)
    {
        if (IsFrozen)
        {
            return;
        }

        var unit = _settings.Current.TemperatureUnit;
        var structureChanged = _shown is null;
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reading in snapshot.Readings)
        {
            present.Add(reading.Id);
            if (_rows.TryGetValue(reading.Id, out var row))
            {
                row.Update(reading, unit);
            }
            else
            {
                _rows[reading.Id] = new SensorRowViewModel(reading, unit);
                structureChanged = true;
            }
        }

        foreach (var gone in _rows.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _rows.Remove(gone);
            structureChanged = true;
        }

        _shown = snapshot;
        IsWaiting = false;
        IsFailed = _sensors.State == SensorServiceState.Failed;
        if (structureChanged)
        {
            Rebuild();
        }

        ShowDiagnosis(snapshot);
        Summary = Loc.Format("Hw.Sensor.Summary", snapshot.Readings.Count.ToString(Loc.Culture), snapshot.At.LocalDateTime.ToString("T", Loc.Culture));
    }

    private void ShowDiagnosis(SensorSnapshot snapshot)
    {
        var problem = CpuSensorDiagnostics.Diagnose(snapshot, _info.IsElevated, _pawnIo.IsInstalled);
        Notice = problem == CpuSensorProblem.None ? string.Empty : Loc.Get($"Hw.Sensor.Problem.{problem}");
        NoticeCommand = problem == CpuSensorProblem.PawnIoMissing ? Loc.Get("Hw.Sensor.Problem.PawnIoCommand") : string.Empty;
    }

    private void Rebuild()
    {
        var filter = (SensorFilterKind)Math.Clamp(FilterIndex, 0, Enum.GetValues<SensorFilterKind>().Length - 1);
        var visible = _rows.Values.Where(r => SensorRows.Matches(r, Query, filter));
        var groups = SensorRows.Group(visible);
        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        OnPropertyChanged(nameof(HasNoMatches));
    }
}
