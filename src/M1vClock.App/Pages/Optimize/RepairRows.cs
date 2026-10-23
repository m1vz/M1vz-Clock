using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.Optimize.Repair;

namespace M1vClock.App.Pages.Optimize;

public static class RepairAdvice
{
    public const string ComponentsCommand = "DISM /Online /Cleanup-Image /RestoreHealth";

    public const string FilesCommand = "sfc /scannow";

    public static string CommandFor(RepairCheck check) => check == RepairCheck.SystemFiles ? FilesCommand : ComponentsCommand;

    public static string KeyOf(RepairCheck check) => check switch
    {
        RepairCheck.ComponentsQuick => "Quick",
        RepairCheck.ComponentsFull => "Full",
        _ => "Files",
    };
}

public sealed partial class RepairCardViewModel : ObservableObject
{
    public RepairCardViewModel(RepairCheck check)
    {
        Check = check;
        var key = RepairAdvice.KeyOf(check);
        Title = Loc.Get($"Opt.Repair.{key}.Title");
        Description = Loc.Get($"Opt.Repair.{key}.Description");
        VerdictText = Loc.Get("Opt.Repair.Verdict.None");
    }

    public RepairCheck Check { get; }

    public string Title { get; }

    public string Description { get; }

    public bool HasAdvice => Advice.Length > 0;

    public bool HasCommand => Command.Length > 0;

    public bool HasTook => TookText.Length > 0;

    public string AccessibleName => $"{Title}, {VerdictText}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial string VerdictText { get; set; }

    [ObservableProperty]
    public partial MetricState Tone { get; set; } = MetricState.Neutral;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTook))]
    public partial string TookText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdvice))]
    public partial string Advice { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommand))]
    public partial string Command { get; set; } = string.Empty;

    public void Show(RepairResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        VerdictText = Loc.Get($"Opt.Repair.Verdict.{result.Verdict}");
        Tone = result.Verdict switch
        {
            RepairVerdict.Healthy => MetricState.Good,
            RepairVerdict.Repairable => MetricState.Warning,
            RepairVerdict.NotRepairable or RepairVerdict.ProblemsFound or RepairVerdict.Failed => MetricState.Critical,
            _ => MetricState.Neutral,
        };
        var total = (int)result.Elapsed.TotalSeconds;
        TookText = result.Verdict is RepairVerdict.NeedsAdministrator
            ? string.Empty
            : Loc.Format("Opt.Repair.Took", (total / 60).ToString(Loc.Culture), (total % 60).ToString(Loc.Culture));
        Command = result.NeedsAction ? RepairAdvice.CommandFor(result.Check) : string.Empty;
        Advice = result.Verdict switch
        {
            RepairVerdict.Repairable or RepairVerdict.NotRepairable => $"{Loc.Get("Opt.Repair.Advice.Components")} {Loc.Get("Opt.Repair.Advice.Trimmed")}",
            RepairVerdict.ProblemsFound => FilesAdvice(result.Detail),
            RepairVerdict.Failed => string.IsNullOrWhiteSpace(result.Detail) ? Loc.Get("Opt.Repair.Advice.Failed") : Loc.Format("Opt.Repair.Advice.FailedCode", result.Detail),
            _ => string.Empty,
        };
    }

    private static string FilesAdvice(string? files)
    {
        var found = Loc.Get("Opt.Repair.Advice.FilesFound");
        var named = string.IsNullOrWhiteSpace(files) ? string.Empty : " " + Loc.Format("Opt.Repair.Advice.FilesNamed", files);
        return $"{found}{named} {Loc.Get("Opt.Repair.Advice.Files")} {Loc.Get("Opt.Repair.Advice.Trimmed")}";
    }

    public override string ToString() => AccessibleName;
}
