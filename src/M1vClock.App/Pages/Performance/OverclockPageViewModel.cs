using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Dialogs;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Hardware.Platform;
using M1vClock.Tuning;
using M1vClock.Tuning.Bios;
using M1vClock.Tuning.Gpu;
using M1vClock.Tuning.Stability;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Performance;

public sealed record TuningLine(string Label, string Value);

public sealed record GuideStepRow(string Number, string Title, string Menu, string Text)
{
    public bool HasMenu => Menu.Length > 0;
}

public sealed partial class GpuTuningRow : ObservableObject
{
    public GpuTuningRow(GpuTuningInfo info, int index)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
        Index = index;
        Lines = OverclockText.Lines(info);
        CanTune = info.Vendor == GpuVendor.Nvidia && info.PowerRange is not null;
        Note = Loc.Get(info.Vendor == GpuVendor.Amd ? "Oc.Gpu.Amd.Note" : "Oc.Gpu.Nvidia.Note");
        PowerText = info.PowerCurrent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        CapLabels = [Loc.Get("Oc.Gpu.Cap.None"), .. CapOffsets.Skip(1).Select(o => Loc.Format("Oc.Gpu.Cap.Offset", CapFor(o)?.ToString("N0", Loc.Culture) ?? "—"))];
    }

    public static readonly int[] CapOffsets = [0, 300, 500, 800];

    public GpuTuningInfo Info { get; }

    public int Index { get; }

    public string Name => Info.Name;

    public IReadOnlyList<TuningLine> Lines { get; }

    public bool CanTune { get; }

    public string Note { get; }

    public IReadOnlyList<string> CapLabels { get; }

    [ObservableProperty]
    public partial string PowerText { get; set; }

    [ObservableProperty]
    public partial int CapIndex { get; set; }

    public int? CapFor(int offset) =>
        offset == 0 || Info.ClockRange is not { } range ? null : Math.Max(NvidiaTuner.MinimumClockCap, range.Maximum - offset);

    public int? SelectedCap => CapFor(CapOffsets[Math.Clamp(CapIndex, 0, CapOffsets.Length - 1)]);

    public override string ToString() => Name;
}

public static class OverclockText
{
    public static IReadOnlyList<TuningLine> Lines(GpuTuningInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var lines = new List<TuningLine>();
        var culture = Loc.Culture;
        string Range(TuningRange range, string unit) => Loc.Format("Oc.Gpu.Range", range.Minimum.ToString("N0", culture), range.Maximum.ToString("N0", culture), unit);
        if (info.ClockRange is { } clock)
        {
            lines.Add(new TuningLine(Loc.Get("Oc.Gpu.Clock"), Range(clock, "MHz")));
        }

        if (info.VoltageRange is { } voltage)
        {
            lines.Add(new TuningLine(Loc.Get("Oc.Gpu.Voltage"), Range(voltage, "mV")));
        }

        if (info.PowerRange is { } power)
        {
            var unit = info.PowerUnit == PowerUnit.Watts ? "W" : "%";
            var current = info.PowerCurrent is { } now ? now.ToString("N0", culture) + " " + unit : "—";
            var standard = info.PowerDefault is { } value ? value.ToString("N0", culture) + " " + unit : "—";
            lines.Add(new TuningLine(Loc.Get("Oc.Gpu.Power"), Loc.Format("Oc.Gpu.PowerValue", current, standard, Range(power, unit))));
        }

        if (info.AtFactory is { } factory)
        {
            lines.Add(new TuningLine(Loc.Get("Oc.Gpu.State"), Loc.Get(factory ? "Oc.Gpu.State.Factory" : "Oc.Gpu.State.Changed")));
        }

        if (!info.ManualClockSupported && !info.ManualPowerSupported)
        {
            lines.Add(new TuningLine(Loc.Get("Oc.Gpu.State"), Loc.Get("Oc.Gpu.NoTuning")));
        }

        return lines;
    }

    public static IReadOnlyList<GuideStepRow> Guide(BiosGuide guide)
    {
        ArgumentNullException.ThrowIfNull(guide);
        return guide.Steps.Select((step, i) => new GuideStepRow(
            (i + 1).ToString(CultureInfo.InvariantCulture),
            Loc.Format($"Oc.Bios.{step.Kind}.Title", guide.MemoryProfileName),
            step.Menu,
            Loc.Format($"Oc.Bios.{step.Kind}.Text", guide.MemoryProfileName, guide.Family == CpuFamily.AmdAm5 ? "-30" : "-25"))).ToList();
    }

    public static string Errors(HardwareErrorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!report.CouldRead)
        {
            return Loc.Format("Oc.Errors.Unreadable", report.ReadError ?? string.Empty);
        }

        return report.Total == 0
            ? Loc.Get("Oc.Errors.None")
            : Loc.Format("Oc.Errors.Some", report.Whea.ToString("N0", Loc.Culture), report.DriverResets.ToString("N0", Loc.Culture), report.UnexpectedShutdowns.ToString("N0", Loc.Culture), report.Last!.Time.ToLocalTime().ToString("g", Loc.Culture));
    }
}

public sealed record OverclockServices(
    IPlatformService Platform,
    IMachineProfile Machine,
    IGpuTuningReader Amd,
    NvidiaTuner Nvidia,
    SessionGuard Guard,
    IHardwareErrorSource Errors,
    IStabilityRunner Stability,
    DialogService Dialogs,
    AppInfo Info,
    TimeProvider Time);

public sealed partial class OverclockPageViewModel : HardwarePageViewModel
{
    private static readonly StabilityTarget[] Targets = [StabilityTarget.CpuAndMemory, StabilityTarget.Gpu, StabilityTarget.Everything];
    private static readonly int[] Minutes = [5, 15, 30];

    private readonly OverclockServices _services;
    private readonly ILogger<OverclockPageViewModel> _logger;
    private CancellationTokenSource? _check;

    public OverclockPageViewModel(OverclockServices services, ILogger<OverclockPageViewModel> logger)
        : base(PageCatalog.Find("overclock"), logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _logger = logger;
        TargetLabels = Targets.Select(t => Loc.Get($"Oc.Check.Target.{t}")).ToList();
        DurationLabels = Minutes.Select(m => Loc.Format("Oc.Check.Minutes", m.ToString(CultureInfo.InvariantCulture))).ToList();
        DurationIndex = 1;
    }

    public ObservableCollection<GpuTuningRow> Gpus { get; } = [];

    public ObservableCollection<GuideStepRow> Guide { get; } = [];

    public ObservableCollection<string> VerdictReasons { get; } = [];

    public IReadOnlyList<string> TargetLabels { get; }

    public IReadOnlyList<string> DurationLabels { get; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string GpuProblem { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GpuResult { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GuideTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int TargetIndex { get; set; }

    [ObservableProperty]
    public partial int DurationIndex { get; set; }

    [ObservableProperty]
    public partial bool IsChecking { get; set; }

    [ObservableProperty]
    public partial double CheckFraction { get; set; }

    [ObservableProperty]
    public partial string CheckStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string VerdictTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState VerdictTone { get; set; } = MetricState.Neutral;

    [ObservableProperty]
    public partial string VerdictDetail { get; set; } = string.Empty;

    public bool HasGpuProblem => GpuProblem.Length > 0;

    public bool HasGpuResult => GpuResult.Length > 0;

    public bool HasVerdict => VerdictTitle.Length > 0;

    public string CheckButtonText => Loc.Get(IsChecking ? "Oc.Check.Stop" : "Oc.Check.Start");

    public override void OnNavigatedFrom()
    {
        _check?.Cancel();
        base.OnNavigatedFrom();
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var platformTask = _services.Platform.ReadAsync(cancellationToken);
            var machineTask = _services.Machine.GetAsync(cancellationToken);
            var readings = await Task.Run(() => new[] { _services.Amd.Read(), _services.Nvidia.Read() }, cancellationToken).ConfigureAwait(true);
            var errors = await Task.Run(() => _services.Errors.ErrorsSince(_services.Time.GetUtcNow().AddDays(-7)), cancellationToken).ConfigureAwait(true);
            var platform = await platformTask.ConfigureAwait(true);
            var machine = await machineTask.ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            Gpus.Clear();
            foreach (var reading in readings.Where(r => r.Status == GpuReadStatus.Read))
            {
                for (var i = 0; i < reading.Gpus.Count; i++)
                {
                    Gpus.Add(new GpuTuningRow(reading.Gpus[i], i));
                }
            }

            GpuProblem = Gpus.Count == 0 ? Loc.Get("Oc.Gpu.None") : string.Empty;
            ErrorsText = OverclockText.Errors(errors);
            var board = platform.Board.TryGet(out var info) ? info : null;
            var guide = BiosGuides.Build(board?.Manufacturer, board?.Model, machine.Cpu);
            GuideTitle = Loc.Format("Oc.Bios.Heading", string.IsNullOrWhiteSpace(guide.Board) ? "—" : guide.Board, machine.Cpu.Trim());
            Guide.Clear();
            foreach (var step in OverclockText.Guide(guide))
            {
                Guide.Add(step);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ApplyAsync(GpuTuningRow? row)
    {
        if (row is not { CanTune: true })
        {
            return;
        }

        if (!_services.Info.IsElevated)
        {
            GpuResult = Loc.Get("Oc.Gpu.NeedsAdmin");
            return;
        }

        if (!int.TryParse(row.PowerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var watts) || row.Info.PowerRange is not { } range || !range.Contains(watts))
        {
            GpuResult = Loc.Format("Oc.Gpu.PowerOutOfRange", row.Info.PowerRange?.Minimum ?? 0, row.Info.PowerRange?.Maximum ?? 0);
            return;
        }

        var cap = row.SelectedCap;
        var changes = new List<string> { Loc.Format("Oc.Gpu.Confirm.Power", watts) };
        changes.Add(cap is { } limit ? Loc.Format("Oc.Gpu.Confirm.Cap", limit) : Loc.Get("Oc.Gpu.Confirm.NoCap"));
        var confirmed = await _services.Dialogs.ShowAsync(new DialogRequest(
            Loc.Format("Oc.Gpu.Confirm.Title", row.Name),
            Loc.Get("Oc.Gpu.Confirm.Message"),
            changes,
            Loc.Get("Oc.Gpu.Confirm.Undo"),
            Loc.Get("Oc.Gpu.Apply"),
            Loc.Get("Common.Cancel"),
            DialogTone.Neutral)).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = _services.Nvidia.Apply(row.Index, new NvidiaRequest(watts, cap));
        if (result.Status is NvidiaApplyStatus.Applied or NvidiaApplyStatus.Partial)
        {
            _services.Guard.Record(new TuningMarker(GpuVendor.Nvidia, row.Index, row.Name, _services.Time.GetUtcNow(), watts, cap));
        }

        GpuResult = Loc.Get($"Oc.Gpu.Result.{result.Status}");
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RestoreAsync(GpuTuningRow? row)
    {
        if (row is not { CanTune: true })
        {
            return;
        }

        if (!_services.Info.IsElevated)
        {
            GpuResult = Loc.Get("Oc.Gpu.NeedsAdmin");
            return;
        }

        var result = _services.Nvidia.RestoreDefaults(row.Index);
        if (result.Status == NvidiaApplyStatus.Applied)
        {
            _services.Guard.Clear();
        }

        GpuResult = Loc.Get(result.Status == NvidiaApplyStatus.Applied ? "Oc.Gpu.Restored" : $"Oc.Gpu.Result.{result.Status}");
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ToggleCheckAsync()
    {
        if (IsChecking)
        {
            _check?.Cancel();
            return;
        }

        var target = Targets[Math.Clamp(TargetIndex, 0, Targets.Length - 1)];
        var duration = TimeSpan.FromMinutes(Minutes[Math.Clamp(DurationIndex, 0, Minutes.Length - 1)]);
        using var cancel = new CancellationTokenSource();
        _check = cancel;
        IsChecking = true;
        VerdictTitle = string.Empty;
        VerdictReasons.Clear();
        CheckStatus = Loc.Get("Oc.Check.Starting");
        CheckFraction = 0;
        var progress = new Progress<StabilityProgress>(ShowProgress);
        try
        {
            var evidence = await _services.Stability.RunAsync(target, duration, progress, cancel.Token).ConfigureAwait(true);
            ShowVerdict(evidence);
        }
        catch (StabilityStartException ex)
        {
            AppLog.StabilityRefused(_logger, ex, ex.Status);
            CheckStatus = Loc.Get($"Oc.Check.Refused.{ex.Status}");
        }
        catch (OperationCanceledException)
        {
            CheckStatus = Loc.Get("Oc.Check.Cancelled");
        }
        finally
        {
            IsChecking = false;
            CheckFraction = 0;
            _check = null;
        }
    }

    partial void OnGpuProblemChanged(string value) => OnPropertyChanged(nameof(HasGpuProblem));

    partial void OnGpuResultChanged(string value) => OnPropertyChanged(nameof(HasGpuResult));

    partial void OnVerdictTitleChanged(string value) => OnPropertyChanged(nameof(HasVerdict));

    public bool IsIdle => !IsChecking;

    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckButtonText));
        OnPropertyChanged(nameof(IsIdle));
    }

    private static string Temperature(float? value) => value is > 0 ? value.Value.ToString("0", Loc.Culture) + " °C" : "—";

    private void ShowProgress(StabilityProgress update)
    {
        CheckFraction = update.Planned > TimeSpan.Zero ? Math.Clamp(update.Elapsed / update.Planned, 0, 1) : 0;
        CheckStatus = Loc.Format("Oc.Check.Running", update.Elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture), update.Planned.ToString(@"mm\:ss", CultureInfo.InvariantCulture), Temperature(update.CpuMaximum), Temperature(update.GpuMaximum));
    }

    private void ShowVerdict(StabilityEvidence evidence)
    {
        var verdict = StabilityJudge.Decide(evidence);
        VerdictTitle = Loc.Get($"Oc.Verdict.{verdict.Result}");
        VerdictTone = verdict.Result switch
        {
            StabilityResult.Stable => MetricState.Good,
            StabilityResult.Unstable => MetricState.Critical,
            _ => MetricState.Warning,
        };
        VerdictDetail = Loc.Format(
            "Oc.Verdict.Detail",
            evidence.Elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture),
            Temperature(evidence.CpuMaximum),
            Temperature(evidence.GpuMaximum),
            evidence.Errors.Whea.ToString(CultureInfo.InvariantCulture),
            evidence.Errors.DriverResets.ToString(CultureInfo.InvariantCulture),
            evidence.MemoryErrors.ToString("N0", Loc.Culture));
        VerdictReasons.Clear();
        foreach (var reason in verdict.Reasons)
        {
            VerdictReasons.Add(Loc.Get($"Oc.Verdict.Reason.{reason}"));
        }

        if (verdict.Result == StabilityResult.Stable)
        {
            VerdictReasons.Add(Loc.Get("Oc.Verdict.StableHint"));
        }

        CheckStatus = string.Empty;
        ErrorsText = OverclockText.Errors(_services.Errors.ErrorsSince(_services.Time.GetUtcNow().AddDays(-7)));
    }
}
