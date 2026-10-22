using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.Hardware.Gpu;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed class GpuPageViewModel : InfoPageViewModel
{
    private readonly IGpuService _gpu;
    private readonly GpuSpecDatabase _database;

    public GpuPageViewModel(IGpuService gpu, GpuSpecDatabase database, ILogger<GpuPageViewModel> logger)
        : base(PageCatalog.Find("gpu"), logger)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(database);
        _gpu = gpu;
        _database = database;
        Add(new InfoSectionViewModel(Loc.Get("Hw.Gpu.Section.Id"), "Icon.Gpu"));
        FooterNote = Loc.Get("Hw.Gpu.Note.Reference");
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        foreach (var section in Sections)
        {
            section.SetLoading();
        }

        var report = await _gpu.ReadAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        TrimSectionsTo(0);
        if (!report.Gpus.TryGet(out var gpus))
        {
            var failed = new InfoSectionViewModel(Loc.Get("Hw.Gpu.Section.Id"), "Icon.Gpu");
            failed.SetFailed(report.Gpus.Error ?? Loc.Get("Hw.Common.Unknown"));
            Add(failed);
            return;
        }

        foreach (var gpu in gpus)
        {
            AddCards(gpu, gpus.Count > 1);
        }
    }

    private void AddCards(GpuInfo gpu, bool several)
    {
        var subtitle = several ? gpu.Name : GpuRows.Subtitle(gpu, _database);
        var identification = Card("Hw.Gpu.Section.Id", GpuRows.Identification(gpu, _database), subtitle);
        if (gpu.Spec is null)
        {
            identification.SetNotes([Loc.Get("Hw.Gpu.NoData.Detail")]);
        }

        Add(identification);
        Add(Card("Hw.Gpu.Section.Units", GpuRows.ProcessingUnits(gpu), several ? gpu.Name : string.Empty));
        Add(Card("Hw.Gpu.Section.Memory", GpuRows.Memory(gpu), several ? gpu.Name : string.Empty));
        Add(Card("Hw.Gpu.Section.Clocks", GpuRows.Clocks(gpu), several ? gpu.Name : string.Empty));
        Add(Card("Hw.Gpu.Section.Driver", GpuRows.Driver(gpu), several ? gpu.Name : string.Empty));
        Add(Card("Hw.Gpu.Section.Tech", GpuRows.Technologies(gpu), several ? gpu.Name : string.Empty));
    }

    private static InfoSectionViewModel Card(string titleKey, IReadOnlyList<InfoRowViewModel> rows, string subtitle)
    {
        var section = new InfoSectionViewModel(Loc.Get(titleKey), "Icon.Gpu") { Subtitle = subtitle };
        section.SetReady(rows);
        return section;
    }
}
