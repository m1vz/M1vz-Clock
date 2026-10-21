using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.Hardware.Power;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed class PowerPageViewModel : InfoPageViewModel
{
    private readonly IPowerService _power;
    private readonly InfoSectionViewModel _energy;

    public PowerPageViewModel(IPowerService power, ILogger<PowerPageViewModel> logger)
        : base(PageCatalog.Find("power"), logger)
    {
        ArgumentNullException.ThrowIfNull(power);
        _power = power;
        _energy = new InfoSectionViewModel(Loc.Get("Hw.Power.Energy"), "Icon.Power");
        Add(_energy);
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        foreach (var section in Sections)
        {
            section.SetLoading();
        }

        var report = await _power.ReadAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        TrimSectionsTo(0);
        AddBatteries(report);
        Add(_energy);
        var plan = report.Plan.TryGet(out var read) ? read : null;
        _energy.SetReady(PowerRows.Energy(report.Source, plan));
        _energy.SetNotes(PowerRows.EnergyNotes(report.Source, plan));
    }

    private void AddBatteries(PowerReport report)
    {
        if (!report.Batteries.TryGet(out var batteries))
        {
            var failed = new InfoSectionViewModel(Loc.Get("Hw.Power.Battery"), "Icon.Power");
            failed.SetFailed(report.Batteries.Error ?? Loc.Get("Hw.Common.Unknown"));
            Add(failed);
            return;
        }

        var onMains = report.Source != PowerSource.Battery;
        foreach (var entry in batteries)
        {
            var battery = entry.Battery;
            var section = new InfoSectionViewModel(PowerRows.Title(battery, batteries.Count), "Icon.Power")
            {
                Subtitle = PowerRows.Subtitle(battery),
                StatusText = PowerRows.Verdict(entry.Assessment.Verdict),
                StatusTone = PowerRows.Tone(entry.Assessment.Verdict),
            };
            section.SetReady(PowerRows.Metrics(battery, onMains));
            section.SetNotes(PowerRows.Notes(entry.Assessment));
            Add(section);
        }
    }
}
