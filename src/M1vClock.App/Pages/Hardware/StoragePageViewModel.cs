using System.Collections.ObjectModel;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.Hardware.Storage;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed class StoragePageViewModel : InfoPageViewModel
{
    private readonly IStorageService _storage;

    public StoragePageViewModel(IStorageService storage, ILogger<StoragePageViewModel> logger)
        : base(PageCatalog.Find("storage"), logger)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
        VolumesStatus = new InfoSectionViewModel(Loc.Get("Hw.Storage.Volumes"), "Icon.Storage") { RetryCommand = RefreshCommand };
        Add(new InfoSectionViewModel(Loc.Get("Hw.Storage.Disks"), "Icon.Storage"));
        FooterNote = Loc.Get("Hw.Common.SerialNote");
    }

    public ObservableCollection<VolumeCardViewModel> Volumes { get; } = [];

    public InfoSectionViewModel VolumesStatus { get; }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        VolumesStatus.SetLoading();
        foreach (var section in Sections)
        {
            section.SetLoading();
        }

        var report = await _storage.ReadAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        ShowVolumes(report);
        ShowDisks(report);
        Notice = NoticeFor(report);
    }

    public static string NoticeFor(StorageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.CounterAccess == CounterAccess.NeedsAdministrator || report.PredictionAccess == CounterAccess.NeedsAdministrator)
        {
            return Loc.Get("Hw.Storage.Notice.Admin");
        }

        return report.Disks.IsSuccess && report.CounterAccess == CounterAccess.Unavailable && report.PredictionAccess == CounterAccess.Unavailable
            ? Loc.Get("Hw.Storage.Notice.Unavailable")
            : string.Empty;
    }

    private void ShowVolumes(StorageReport report)
    {
        Volumes.Clear();
        if (!report.Volumes.TryGet(out var volumes))
        {
            VolumesStatus.SetFailed(report.Volumes.Error ?? Loc.Get("Hw.Common.Unknown"));
            return;
        }

        foreach (var volume in volumes)
        {
            Volumes.Add(new VolumeCardViewModel(volume));
        }

        VolumesStatus.SetReady([]);
    }

    private void ShowDisks(StorageReport report)
    {
        TrimSectionsTo(0);
        if (!report.Disks.TryGet(out var disks))
        {
            var failed = new InfoSectionViewModel(Loc.Get("Hw.Storage.Disks"), "Icon.Storage");
            failed.SetFailed(report.Disks.Error ?? Loc.Get("Hw.Common.Unknown"));
            Add(failed);
            return;
        }

        foreach (var entry in disks)
        {
            var section = new InfoSectionViewModel(entry.Disk.Model, "Icon.Storage")
            {
                Subtitle = DiskRows.Subtitle(entry.Disk),
                StatusText = DiskRows.Verdict(entry.Assessment.Verdict),
                StatusTone = DiskRows.Tone(entry.Assessment.Verdict),
            };
            section.SetReady(DiskRows.Metrics(entry.Disk));
            section.SetNotes(DiskRows.Notes(entry.Assessment));
            Add(section);
        }
    }
}
