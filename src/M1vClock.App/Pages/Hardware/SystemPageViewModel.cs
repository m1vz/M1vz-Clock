using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.Core.Results;
using M1vClock.Hardware.Platform;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed class SystemPageViewModel : InfoPageViewModel
{
    private const int FixedSections = 3;

    private readonly IPlatformService _platform;
    private readonly InfoSectionViewModel _os;
    private readonly InfoSectionViewModel _board;
    private readonly InfoSectionViewModel _bios;

    public SystemPageViewModel(IPlatformService platform, ILogger<SystemPageViewModel> logger)
        : base(PageCatalog.Find("system"), logger)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
        _os = new InfoSectionViewModel(Loc.Get("Hw.System.Os"), "Icon.System");
        _board = new InfoSectionViewModel(Loc.Get("Hw.System.Board"), "Icon.Board");
        _bios = new InfoSectionViewModel(Loc.Get("Hw.System.Bios"), "Icon.Bios");
        Add(_os);
        Add(_board);
        Add(_bios);
        Add(new InfoSectionViewModel(Loc.Get("Hw.System.Cpu"), "Icon.Cpu"));
        FooterNote = Loc.Get("Hw.Common.SerialNote");
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        foreach (var section in Sections)
        {
            section.SetLoading();
        }

        var report = await _platform.ReadAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        _os.Apply(report.Os, SystemRows.Os);
        _board.Apply(report.Board, SystemRows.Board);
        _bios.Apply(report.Bios, SystemRows.Bios);
        ShowProcessors(report.Cpus);
    }

    private void ShowProcessors(ReadResult<IReadOnlyList<CpuInfo>> cpus)
    {
        TrimSectionsTo(FixedSections);

        if (!cpus.TryGet(out var list))
        {
            var failed = new InfoSectionViewModel(Loc.Get("Hw.System.Cpu"), "Icon.Cpu");
            failed.SetFailed(cpus.Error ?? Loc.Get("Hw.Common.Unknown"));
            Add(failed);
            return;
        }

        foreach (var cpu in list)
        {
            var section = new InfoSectionViewModel(cpu.Name, "Icon.Cpu");
            section.SetReady(SystemRows.Cpu(cpu));
            Add(section);
        }
    }
}
