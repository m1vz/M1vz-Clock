using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.Hardware.Ram;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed class MemoryPageViewModel : InfoPageViewModel
{
    private const int FixedSections = 2;

    private readonly IRamService _ram;
    private readonly InfoSectionViewModel _summary;
    private readonly InfoSectionViewModel _usage;

    public MemoryPageViewModel(IRamService ram, ILogger<MemoryPageViewModel> logger)
        : base(PageCatalog.Find("memory"), logger)
    {
        ArgumentNullException.ThrowIfNull(ram);
        _ram = ram;
        _summary = new InfoSectionViewModel(Loc.Get("Hw.Ram.Summary"), "Icon.Memory");
        _usage = new InfoSectionViewModel(Loc.Get("Hw.Ram.Usage"), "Icon.Memory");
        Add(_summary);
        Add(_usage);
        FooterNote = Loc.Get("Hw.Common.SerialNote");
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        foreach (var section in Sections)
        {
            section.SetLoading();
        }

        var report = await _ram.ReadAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        _summary.Apply(report.Inventory, inventory => RamRows.Summary(inventory.Summary));
        _usage.Apply(report.Usage, RamRows.Usage);
        TrimSectionsTo(FixedSections);
        if (!report.Inventory.TryGet(out var modules))
        {
            return;
        }

        foreach (var module in modules.Modules)
        {
            var section = new InfoSectionViewModel(RamRows.ModuleTitle(module), "Icon.Memory");
            section.SetReady(RamRows.Module(module));
            Add(section);
        }
    }
}
