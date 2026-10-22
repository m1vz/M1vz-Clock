using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Benchmarks.Io;
using M1vClock.Benchmarks.Results;
using M1vClock.Benchmarks.Safety;
using M1vClock.Benchmarks.Statistics;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Performance;

public sealed partial class BenchTabViewModel(IBenchJob job) : ObservableObject
{
    public IBenchJob Job { get; } = job;

    public string Title => Job.Title;

    public string IconKey => Job.IconKey;

    public override string ToString() => Title;
}

public sealed partial class BenchmarksPageViewModel : PageViewModel, IDisposable
{
    public const int HistoryPageSize = 8;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly HeavyTestGate _gate;
    private readonly SafetyMonitor _monitor;
    private readonly BenchmarkHistory _history;
    private readonly IMachineProfile _machine;
    private readonly IFileDialogs _dialogs;
    private readonly AppSettingsService _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<BenchmarksPageViewModel> _logger;
    private CancellationTokenSource? _run;
    private BenchmarkRecord? _baseline;
    private int _historyLimit = HistoryPageSize;
    private bool _disposed;

    public BenchmarksPageViewModel(
        IBenchJobSource jobs,
        HeavyTestGate gate,
        SafetyMonitor monitor,
        BenchmarkHistory history,
        IMachineProfile machine,
        IFileDialogs dialogs,
        AppSettingsService settings,
        TimeProvider clock,
        ILogger<BenchmarksPageViewModel> logger)
        : base(PageCatalog.Find("benchmarks"))
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _gate = gate;
        _monitor = monitor;
        _history = history;
        _machine = machine;
        _dialogs = dialogs;
        _settings = settings;
        _clock = clock;
        _logger = logger;
        Tabs = [.. jobs.Create().Select(job => new BenchTabViewModel(job))];
        TemperatureCard = new OverviewCardViewModel(Loc.Get("Bench.Live.Temperature"));
        LoadCard = new OverviewCardViewModel(Loc.Get("Bench.Live.Load"));
        PowerCard = new OverviewCardViewModel(Loc.Get("Bench.Live.Power"));
        ClockCard = new OverviewCardViewModel(Loc.Get("Bench.Live.Clock"));
        foreach (var tab in Tabs)
        {
            if (tab.Job is IAvailabilityNotifier notifier)
            {
                notifier.AvailabilityChanged += OnAvailabilityChanged;
            }
        }

        Selected = Tabs[0].Job;
        _history.Changed += OnHistoryChanged;
        _gate.Changed += OnGateChanged;
        RefreshHistory();
    }

    public IReadOnlyList<BenchTabViewModel> Tabs { get; }

    public OverviewCardViewModel TemperatureCard { get; }

    public OverviewCardViewModel LoadCard { get; }

    public OverviewCardViewModel PowerCard { get; }

    public OverviewCardViewModel ClockCard { get; }

    public ObservableCollection<BenchHistoryRowViewModel> HistoryRows { get; } = [];

    public IBenchJob Selected { get; private set; }

    public IReadOnlyList<BenchOptionViewModel> Options => Selected.Options;

    public string TestTitle => Selected.Title;

    public string TestDescription => Selected.Description;

    public string TestIconKey => Selected.IconKey;

    public string ButtonText => Loc.Get(IsRunning ? "Bench.Cancel" : "Bench.Start");

    public bool CanStart => !IsRunning && !_gate.IsBusy && Selected.UnavailableReason is null;

    public bool CanToggle => IsRunning || CanStart;

    public bool IsIdle => !IsRunning;

    public bool MessageIsGood => HasMessage && MessageTone == MetricState.Good;

    public bool MessageIsWarning => HasMessage && MessageTone is MetricState.Warning or MetricState.Neutral;

    public bool MessageIsCritical => HasMessage && MessageTone == MetricState.Critical;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool HasUnavailable => Selected.UnavailableReason is not null;

    public string UnavailableText => Selected.UnavailableReason ?? string.Empty;

    public bool HasResult => Result is not null;

    public bool HasComparison => Comparison is not null;

    public bool HasHistory => HistoryRows.Count > 0;

    public bool CanShowMore => _history.Records.Count(r => Selected.HistoryTests.Contains(r.Test)) > _historyLimit;

    public bool ShowLive => IsRunning && Selected.Thermal is not null;

    public bool ShowPreview => IsRunning && Preview is not null;

    public bool OtherTestRunning => _gate.IsBusy && !IsRunning;

    public string OtherTestText => Loc.Format("Bench.OtherRunning", _gate.CurrentName ?? string.Empty);

    public string ProtectionText => ThermalProtected
        ? Loc.Format("Bench.Protection.On", Selected.Thermal == ThermalComponent.Gpu ? SafetyLimits.GpuDefault : SafetyLimits.CpuDefault)
        : Loc.Get("Bench.Protection.Off");

    public bool HasProtectionText => IsRunning && Selected.Thermal is not null;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string PhaseText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState MessageTone { get; set; }

    [ObservableProperty]
    public partial IPreviewSource? Preview { get; set; }

    [ObservableProperty]
    public partial InfoSectionViewModel? Result { get; set; }

    [ObservableProperty]
    public partial string ScoreHeadline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScoreCaption { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SecondScoreHeadline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SecondScoreCaption { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string VersusPrevious { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState VersusTone { get; set; }

    [ObservableProperty]
    public partial InfoSectionViewModel? Comparison { get; set; }

    [ObservableProperty]
    public partial bool ThermalProtected { get; set; }

    public bool HasSecondScore => !string.IsNullOrEmpty(SecondScoreHeadline);

    public bool HasVersus => !string.IsNullOrEmpty(VersusPrevious);

    public override void OnNavigatedTo()
    {
        foreach (var tab in Tabs)
        {
            tab.Job.Refresh();
        }

        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(HasUnavailable));
        OnPropertyChanged(nameof(UnavailableText));
        _gate.StopRequested += OnStopRequested;
    }

    public override void OnNavigatedFrom()
    {
        _gate.StopRequested -= OnStopRequested;
        _run?.Cancel();
    }

    public void Dispose()
    {
        foreach (var tab in Tabs)
        {
            if (tab.Job is IAvailabilityNotifier notifier)
            {
                notifier.AvailabilityChanged -= OnAvailabilityChanged;
            }
        }

        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _history.Changed -= OnHistoryChanged;
        _gate.Changed -= OnGateChanged;
        _gate.StopRequested -= OnStopRequested;
        _run?.Cancel();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ToggleAsync()
    {
        if (IsRunning)
        {
            _run?.Cancel();
            return;
        }

        await RunAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public void ShowMore()
    {
        _historyLimit += HistoryPageSize;
        RefreshHistory();
    }

    [RelayCommand]
    public void Export()
    {
        var path = _dialogs.PickSave(Loc.Get("Bench.Export.Title"), Loc.Get("Bench.File.Filter"), $"m1vclock-benchmarks-{_clock.GetLocalNow():yyyyMMdd}.json");
        if (path is null)
        {
            return;
        }

        try
        {
            _history.Export(path);
            SetMessage(Loc.Format("Bench.Export.Done", Path.GetFileName(path)), MetricState.Good);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.BenchmarkFailed(_logger, ex, "export");
            SetMessage(Loc.Get("Bench.Export.Failed"), MetricState.Critical);
        }
    }

    [RelayCommand]
    public void Import()
    {
        var path = _dialogs.PickOpen(Loc.Get("Bench.Import.Title"), Loc.Get("Bench.File.Filter"));
        if (path is null)
        {
            return;
        }

        var before = _history.Records.Count;
        var outcome = _history.Import(path);
        if (outcome.Failure != ImportFailure.None)
        {
            SetMessage(Loc.Get($"Bench.Import.Failure.{outcome.Failure}"), MetricState.Critical);
            return;
        }

        var added = _history.Records.Count - before;
        SetMessage(
            outcome.Rejected > 0
                ? Loc.Format("Bench.Import.DoneWithRejected", added, outcome.Rejected)
                : Loc.Format("Bench.Import.Done", added),
            outcome.Rejected > 0 ? MetricState.Warning : MetricState.Good);
    }

    [RelayCommand]
    private void SelectBaseline(BenchHistoryRowViewModel row)
    {
        if (row is null)
        {
            return;
        }

        _baseline = ReferenceEquals(_baseline, row.Record) || _baseline?.Id == row.Record.Id ? null : row.Record;
        RefreshHistory();
        UpdateComparison();
    }

    [RelayCommand]
    private void DeleteRecord(BenchHistoryRowViewModel row)
    {
        if (row is null)
        {
            return;
        }

        if (_baseline?.Id == row.Record.Id)
        {
            _baseline = null;
        }

        _history.Remove(row.Record.Id);
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (IsRunning || value < 0 || value >= Tabs.Count)
        {
            return;
        }

        Selected = Tabs[value].Job;
        _baseline = null;
        _historyLimit = HistoryPageSize;
        Result = null;
        Comparison = null;
        Message = string.Empty;
        ScoreHeadline = string.Empty;
        SecondScoreHeadline = string.Empty;
        VersusPrevious = string.Empty;
        foreach (var name in new[]
        {
            nameof(Selected), nameof(Options), nameof(TestTitle), nameof(TestDescription), nameof(TestIconKey), nameof(CanStart), nameof(CanToggle),
            nameof(HasUnavailable), nameof(UnavailableText), nameof(ShowLive), nameof(HasProtectionText), nameof(CanShowMore),
        })
        {
            OnPropertyChanged(name);
        }

        RefreshHistory();
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(ShowLive));
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(HasProtectionText));
        OnPropertyChanged(nameof(OtherTestRunning));
    }

    partial void OnMessageChanged(string value) => NotifyMessage();

    partial void OnMessageToneChanged(MetricState value) => NotifyMessage();

    private void NotifyMessage()
    {
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(MessageIsGood));
        OnPropertyChanged(nameof(MessageIsWarning));
        OnPropertyChanged(nameof(MessageIsCritical));
    }

    partial void OnPreviewChanged(IPreviewSource? value) => OnPropertyChanged(nameof(ShowPreview));

    partial void OnResultChanged(InfoSectionViewModel? value) => OnPropertyChanged(nameof(HasResult));

    partial void OnComparisonChanged(InfoSectionViewModel? value) => OnPropertyChanged(nameof(HasComparison));

    partial void OnSecondScoreHeadlineChanged(string value) => OnPropertyChanged(nameof(HasSecondScore));

    partial void OnVersusPreviousChanged(string value) => OnPropertyChanged(nameof(HasVersus));

    partial void OnThermalProtectedChanged(bool value) => OnPropertyChanged(nameof(ProtectionText));

    private void SetMessage(string text, MetricState tone)
    {
        Message = text;
        MessageTone = tone;
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        RefreshHistory();
        UpdateComparison();
    }

    private void OnAvailabilityChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, Selected))
        {
            return;
        }

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(HasUnavailable));
        OnPropertyChanged(nameof(UnavailableText));
    }

    private void OnGateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(OtherTestRunning));
        OnPropertyChanged(nameof(OtherTestText));
    }

    private void OnStopRequested(object? sender, EventArgs e) => _run?.Cancel();

    private async Task RunAsync()
    {
        var job = Selected;
        if (job.UnavailableReason is not null)
        {
            SetMessage(job.UnavailableReason, MetricState.Warning);
            return;
        }

        using var run = new CancellationTokenSource();
        _run = run;
        HeavyTestLease? lease = null;
        var watching = false;
        EventHandler<SafetyTrip>? onTrip = null;
        try
        {
            lease = await _gate.EnterAsync($"benchmark:{job.Id}", run.Token).ConfigureAwait(true);
            if (lease is null)
            {
                SetMessage(Loc.Format("Bench.OtherRunning", _gate.CurrentName ?? string.Empty), MetricState.Warning);
                return;
            }

            ClearForRun();
            IsRunning = true;
            watching = StartWatching(job, run, out onTrip);
            var machine = await _machine.GetAsync(run.Token).ConfigureAwait(true);
            var started = _clock.GetTimestamp();
            using var polling = new CancellationTokenSource();
            var poller = PollAsync(job, started, polling.Token);
            BenchJobOutcome outcome;
            try
            {
                outcome = await job.RunAsync(run.Token).ConfigureAwait(true);
            }
            finally
            {
                await polling.CancelAsync().ConfigureAwait(true);
                await poller.ConfigureAwait(true);
            }

            Present(job, outcome, machine);
        }
        catch (OperationCanceledException)
        {
            SetMessage(StopMessage(), MetricState.Warning);
            AppLog.BenchmarkStopped(_logger, job.Id, _monitor.Trip?.Reason.ToString() ?? "user");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException or InsufficientMemoryException)
        {
            AppLog.BenchmarkFailed(_logger, ex, job.Id);
            SetMessage(FailureMessage(ex), MetricState.Critical);
        }
        finally
        {
            if (watching)
            {
                _monitor.Tripped -= onTrip;
                await _monitor.StopAsync().ConfigureAwait(true);
            }

            lease?.Dispose();
            _run = null;
            Preview = null;
            IsRunning = false;
            Progress = 0;
            PhaseText = string.Empty;
            DetailText = string.Empty;
            ElapsedText = string.Empty;
            OnPropertyChanged(nameof(Options));
        }
    }

    private bool StartWatching(IBenchJob job, CancellationTokenSource run, out EventHandler<SafetyTrip>? onTrip)
    {
        onTrip = null;
        ThermalProtected = false;
        if (job.Thermal is not { } component)
        {
            return false;
        }

        if (!_monitor.CanRead(component))
        {
            return false;
        }

        onTrip = (_, _) => run.Cancel();
        _monitor.Tripped += onTrip;
        _monitor.Start([component], SafetyLimits.Defaults(component == ThermalComponent.Gpu));
        ThermalProtected = true;
        return true;
    }

    private async Task PollAsync(IBenchJob job, long started, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
            {
                Update(job, started);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private void Update(IBenchJob job, long started)
    {
        var poll = job.Poll();
        Progress = poll.Fraction;
        PhaseText = string.IsNullOrEmpty(poll.PhaseKey) ? string.Empty : Loc.Get(poll.PhaseKey);
        var elapsed = _clock.GetElapsedTime(started);
        ElapsedText = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", Loc.Culture);
        DetailText = poll.Detail.Length > 0 ? poll.Detail : EstimateText(poll.Fraction, elapsed);
        if (job.Preview is { } preview && !ReferenceEquals(Preview, preview))
        {
            Preview = preview;
        }

        if (job.Thermal is not null && _monitor.Latest is { } sample)
        {
            ShowLiveSample(job.Thermal.Value == ThermalComponent.Cpu ? sample.Cpu : sample.Gpu);
        }
    }

    private static string EstimateText(double fraction, TimeSpan elapsed)
    {
        if (fraction < 0.03 || fraction >= 1)
        {
            return string.Empty;
        }

        var remaining = TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - fraction) / fraction);
        return Loc.Format("Bench.Remaining", remaining.ToString(remaining.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", Loc.Culture));
    }

    private void ShowLiveSample(ComponentSample? sample)
    {
        var unit = _settings.Current.TemperatureUnit;
        var temperature = SensorFormat.Value(SensorKind.Temperature, sample?.Temperature, unit).Split(' ');
        TemperatureCard.Set(temperature[0], temperature.Length > 1 ? temperature[1] : string.Empty, string.Empty, MetricState.Neutral, string.Empty);
        SetCard(LoadCard, SensorKind.Load, sample?.Load);
        SetCard(PowerCard, SensorKind.Power, sample?.Power);
        SetCard(ClockCard, SensorKind.Clock, sample?.Clock);
    }

    private static void SetCard(OverviewCardViewModel card, SensorKind kind, float? value)
    {
        var text = SensorFormat.Value(kind, value).Split(' ');
        card.Set(text[0], text.Length > 1 ? text[1] : string.Empty, string.Empty, MetricState.Neutral, string.Empty);
    }

    private void ClearForRun()
    {
        Message = string.Empty;
        Result = null;
        Comparison = null;
        ScoreHeadline = string.Empty;
        SecondScoreHeadline = string.Empty;
        VersusPrevious = string.Empty;
        Preview = null;
        Progress = 0;
        PhaseText = Loc.Get("Bench.Preparing");
        DetailText = string.Empty;
        ElapsedText = string.Empty;
        ThermalProtected = false;
        foreach (var card in new[] { TemperatureCard, LoadCard, PowerCard, ClockCard })
        {
            card.Set("—", string.Empty, string.Empty, MetricState.Neutral, string.Empty);
        }
    }

    private void Present(IBenchJob job, BenchJobOutcome outcome, BenchmarkMachine machine)
    {
        var previous = new List<(BenchJobDraft Draft, BenchmarkRecord? Before)>();
        foreach (var draft in outcome.Drafts)
        {
            previous.Add((draft, LatestComparable(draft.Test, draft.ScoreVersion, machine)));
        }

        foreach (var draft in outcome.Drafts)
        {
            _history.Add(new BenchmarkRecord
            {
                Id = Guid.NewGuid(),
                At = _clock.GetUtcNow(),
                Test = draft.Test,
                ScoreVersion = draft.ScoreVersion,
                Score = draft.Score,
                Metrics = draft.Metrics,
                Machine = machine,
                Sustained = draft.Sustained,
            });
            AppLog.BenchmarkCompleted(_logger, draft.Test, draft.Score);
        }

        var section = new InfoSectionViewModel(Loc.Get("Bench.Result.Title"), job.IconKey);
        section.SetReady(outcome.Rows);
        section.SetNotes(outcome.Notes);
        section.StatusText = Loc.Format("Bench.Result.Done", ElapsedText);
        section.StatusTone = MetricState.Good;
        Result = section;

        var first = outcome.Drafts[0];
        ScoreHeadline = BenchNames.Value(first.Test, first.Score);
        ScoreCaption = BenchNames.Test(first.Test);
        if (outcome.Drafts.Count > 1)
        {
            var second = outcome.Drafts[1];
            SecondScoreHeadline = BenchNames.Value(second.Test, second.Score);
            SecondScoreCaption = BenchNames.Test(second.Test);
        }

        ShowVersus(previous);
        if (outcome.Drafts.Count == 0)
        {
            return;
        }

        SetMessage(Loc.Get("Bench.Saved"), MetricState.Good);
    }

    private void ShowVersus(List<(BenchJobDraft Draft, BenchmarkRecord? Before)> previous)
    {
        var parts = new List<string>();
        var tone = MetricState.Neutral;
        foreach (var (draft, before) in previous)
        {
            if (before is null)
            {
                continue;
            }

            var info = BenchmarkCatalog.Find(draft.Test);
            var change = BenchStatistics.ChangePercent(before.Score, draft.Score);
            var verdict = BenchmarkComparer.Judge(change, info?.HigherIsBetter ?? true);
            parts.Add(Loc.Format("Bench.Versus.Line", BenchNames.Test(draft.Test), BenchNames.Percent(change), BenchNames.VerdictText(verdict)));
            if (verdict != ComparisonVerdict.WithinNoise && (tone == MetricState.Neutral || verdict == ComparisonVerdict.Worse))
            {
                tone = BenchNames.Tone(verdict);
            }
        }

        VersusPrevious = parts.Count == 0 ? Loc.Get("Bench.Versus.First") : Loc.Format("Bench.Versus.Title", string.Join(" · ", parts));
        VersusTone = tone;
    }

    private BenchmarkRecord? LatestComparable(string test, int version, BenchmarkMachine machine) =>
        _history.For(test).FirstOrDefault(r => r.ScoreVersion == version && !r.Imported && r.Machine.Cpu == machine.Cpu && r.Machine.Gpu == machine.Gpu);

    private void RefreshHistory()
    {
        var tests = Selected.HistoryTests;
        var records = _history.Records.Where(r => tests.Contains(r.Test)).ToList();
        var bests = tests.ToDictionary(t => t, t => BestOf(records, t));
        HistoryRows.Clear();
        foreach (var record in records.Take(_historyLimit))
        {
            var info = BenchmarkCatalog.Find(record.Test);
            var best = bests[record.Test];
            var fraction = best <= 0 || record.Score <= 0
                ? 0
                : info?.HigherIsBetter == false ? Math.Min(1, best / record.Score) : Math.Min(1, record.Score / best);
            HistoryRows.Add(new BenchHistoryRowViewModel(record, fraction, _baseline?.Id == record.Id)
            {
                SelectCommand = SelectBaselineCommand,
                DeleteCommand = DeleteRecordCommand,
            });
        }

        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(CanShowMore));
    }

    private static double BestOf(List<BenchmarkRecord> records, string test)
    {
        var info = BenchmarkCatalog.Find(test);
        var scores = records.Where(r => r.Test == test && r.ScoreVersion == info?.ScoreVersion && r.Score > 0).Select(r => r.Score).ToList();
        if (scores.Count == 0)
        {
            return 0;
        }

        return info?.HigherIsBetter == false ? scores.Min() : scores.Max();
    }

    private void UpdateComparison()
    {
        if (_baseline is null)
        {
            Comparison = null;
            return;
        }

        var current = _history.For(_baseline.Test).FirstOrDefault(r => r.Id != _baseline.Id && r.At >= _baseline.At)
            ?? _history.For(_baseline.Test).FirstOrDefault(r => r.Id != _baseline.Id);
        Comparison = current is null ? null : BenchComparisonRows.Build(BenchmarkComparer.Compare(_baseline, current));
    }

    private string StopMessage() => _monitor.Trip switch
    {
        { Reason: TripReason.Temperature } trip => Loc.Format("Bench.Stop.Temperature", Loc.Get(trip.Component == ThermalComponent.Gpu ? "Bench.Component.Gpu" : "Bench.Component.Cpu"), trip.Value.ToString("0", Loc.Culture), trip.Limit.ToString("0", Loc.Culture)),
        { Reason: TripReason.SensorLost } => Loc.Get("Bench.Stop.SensorLost"),
        { Reason: TripReason.ComponentFailed } => Loc.Get("Bench.Stop.Failed"),
        _ => Loc.Get("Bench.Stop.User"),
    };

    private static string FailureMessage(Exception exception) => exception switch
    {
        InsufficientMemoryException => Loc.Get("Bench.Error.Memory"),
        UnauthorizedAccessException => Loc.Get("Bench.Error.Access"),
        InsufficientDiskSpaceException => Loc.Get("Bench.Error.Space"),
        _ => Loc.Get("Bench.Error.Generic"),
    };
}
