using System.Collections.ObjectModel;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.Core.Results;
using M1vClock.Hardware.Platform;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed class SystemPageViewModel : HardwarePageViewModel
{
    private const int FixedSections = 3;

    private readonly IPlatformService _platform;
    private readonly InfoSectionViewModel _os;
    private readonly InfoSectionViewModel _board;
    private readonly InfoSectionViewModel _bios;
    private readonly InfoSectionViewModel _cpuPlaceholder;

    public SystemPageViewModel(IPlatformService platform, ILogger<SystemPageViewModel> logger)
        : base(PageCatalog.Find("system"), logger)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
        _os = new InfoSectionViewModel(Loc.Get("Hw.System.Os"), "Icon.System");
        _board = new InfoSectionViewModel(Loc.Get("Hw.System.Board"), "Icon.Board");
        _bios = new InfoSectionViewModel(Loc.Get("Hw.System.Bios"), "Icon.Bios");
        _cpuPlaceholder = new InfoSectionViewModel(Loc.Get("Hw.System.Cpu"), "Icon.Cpu");
        Sections = [_os, _board, _bios, _cpuPlaceholder];
        foreach (var section in Sections)
        {
            section.RetryCommand = RefreshCommand;
        }
    }

    public ObservableCollection<InfoSectionViewModel> Sections { get; }

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
        while (Sections.Count > FixedSections)
        {
            Sections.RemoveAt(FixedSections);
        }

        if (!cpus.TryGet(out var list))
        {
            var failed = new InfoSectionViewModel(Loc.Get("Hw.System.Cpu"), "Icon.Cpu") { RetryCommand = RefreshCommand };
            failed.SetFailed(cpus.Error ?? Loc.Get("Hw.Common.Unknown"));
            Sections.Add(failed);
            return;
        }

        foreach (var cpu in list)
        {
            var section = new InfoSectionViewModel(cpu.Name, "Icon.Cpu") { RetryCommand = RefreshCommand };
            section.SetReady(SystemRows.Cpu(cpu));
            Sections.Add(section);
        }
    }
}
