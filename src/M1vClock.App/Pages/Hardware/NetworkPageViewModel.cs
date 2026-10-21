using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.Hardware.Network;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed class NetworkPageViewModel : InfoPageViewModel
{
    private readonly INetworkService _network;
    private readonly InfoSectionViewModel _active;
    private readonly InfoSectionViewModel _others;

    public NetworkPageViewModel(INetworkService network, NetworkDiagnosticViewModel diagnostic, ILogger<NetworkPageViewModel> logger)
        : base(PageCatalog.Find("network"), logger)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(diagnostic);
        _network = network;
        Diagnostic = diagnostic;
        _active = new InfoSectionViewModel(Loc.Get("Hw.Net.Active"), "Icon.Network");
        _others = new InfoSectionViewModel(Loc.Get("Hw.Net.Others"), "Icon.Network");
        Add(_active);
    }

    public NetworkDiagnosticViewModel Diagnostic { get; }

    public override void OnNavigatedFrom()
    {
        Diagnostic.Cancel();
        base.OnNavigatedFrom();
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        _active.SetLoading();
        var result = await _network.ReadAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        TrimSectionsTo(0);
        Add(_active);
        if (!result.TryGet(out var overview))
        {
            _active.SetFailed(result.Error ?? Loc.Get("Hw.Common.Unknown"));
            return;
        }

        ShowActive(overview.Active);
        if (overview.Others.Count > 0)
        {
            _others.SetReady(NetworkRows.Others(overview.Others));
            Add(_others);
        }
    }

    private void ShowActive(NetworkAdapter? adapter)
    {
        if (adapter is null)
        {
            _active.Subtitle = string.Empty;
            _active.StatusText = Loc.Get("Hw.Net.NoConnection.Status");
            _active.StatusTone = MetricState.Critical;
            _active.SetReady([]);
            _active.SetNotes([Loc.Get("Hw.Net.NoConnection.Note")]);
            return;
        }

        _active.Subtitle = adapter.Description;
        _active.StatusText = string.Empty;
        _active.StatusTone = MetricState.Neutral;
        _active.SetReady(NetworkRows.Adapter(adapter));
        _active.SetNotes([]);
    }
}
