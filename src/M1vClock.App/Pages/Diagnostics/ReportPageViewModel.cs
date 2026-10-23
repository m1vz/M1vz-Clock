using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Pages.Performance;
using M1vClock.App.Services;
using M1vClock.App.Services.Reports;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Diagnostics;

public sealed partial class ReportPartViewModel : ObservableObject
{
    public ReportPartViewModel(ReportSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        Section = section;
        Include = !section.IsEmpty;
    }

    public ReportSection Section { get; }

    public string Id => Section.Id;

    public string Title => Section.Title;

    public bool HasData => !Section.IsEmpty;

    public string RowsText => Loc.Format("Diag.Report.Rows", Section.Rows.Count.ToString(Loc.Culture));

    [ObservableProperty]
    public partial bool Include { get; set; }

    public string AccessibleName => $"{Title}: {RowsText}";

    public override string ToString() => AccessibleName;
}

public sealed partial class ReportPageViewModel : HardwarePageViewModel
{
    private readonly IReportCollector _collector;
    private readonly IFileDialogs _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly ILogger<ReportPageViewModel> _logger;
    private readonly Func<string> _computerName;
    private ReportDocument? _document;

    public ReportPageViewModel(
        IReportCollector collector,
        IFileDialogs dialogs,
        IClipboardService clipboard,
        ILogger<ReportPageViewModel> logger)
        : this(collector, dialogs, clipboard, logger, () => Environment.MachineName)
    {
    }

    internal ReportPageViewModel(
        IReportCollector collector,
        IFileDialogs dialogs,
        IClipboardService clipboard,
        ILogger<ReportPageViewModel> logger,
        Func<string> computerName)
        : base(PageCatalog.Find("report"), logger)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(computerName);
        _collector = collector;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _logger = logger;
        _computerName = computerName;
    }

    public ObservableCollection<ReportPartViewModel> Parts { get; } = [];

    [ObservableProperty]
    public partial bool IncludeName { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string Preview { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool HasPreview => Preview.Length > 0;

    public bool CanExport => !IsLoading && _document is not null && Parts.Any(p => p.Include);

    public bool ShowNothingChosen => !IsLoading && _document is not null && !Parts.Any(p => p.Include);

    public ReportDocument? Build()
    {
        if (_document is not { } document)
        {
            return null;
        }

        return document with { ComputerName = IncludeName ? _computerName() : null };
    }

    public IReadOnlySet<string> Included() => Parts.Where(p => p.Include).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        Notice = string.Empty;
        Problem = string.Empty;
        try
        {
            var document = await _collector.CollectAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            _document = document;
            foreach (var part in Parts)
            {
                part.PropertyChanged -= OnPartChanged;
            }

            Parts.Clear();
            foreach (var section in document.Sections)
            {
                var part = new ReportPartViewModel(section);
                part.PropertyChanged += OnPartChanged;
                Parts.Add(part);
            }

            Refresh();
        }
        finally
        {
            IsLoading = false;
            NotifyFlags();
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (Build() is not { } document || !CanExport)
        {
            return;
        }

        var path = _dialogs.PickSave(Loc.Get("Diag.Report.Save"), Loc.Get("Diag.Report.File.Filter"), ReportRenderer.FileName(document.At));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Notice = string.Empty;
        Problem = string.Empty;
        var included = Included();
        var asText = string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);
        var content = asText ? ReportRenderer.Text(document, included) : ReportRenderer.Html(document, included);
        try
        {
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)).ConfigureAwait(true);
            Notice = Loc.Format("Diag.Report.Saved", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppLog.ReportSaveFailed(_logger, ex);
            Problem = Loc.Format("Diag.Report.SaveFailed", ex.Message);
        }
    }

    [RelayCommand]
    public void Copy()
    {
        if (Build() is not { } document || !CanExport)
        {
            return;
        }

        Notice = string.Empty;
        Problem = string.Empty;
        if (_clipboard.TrySetText(ReportRenderer.Text(document, Included())))
        {
            Notice = Loc.Get("Diag.Logs.Copied");
        }
        else
        {
            Problem = Loc.Get("Diag.Logs.CopyFailed");
        }
    }

    partial void OnIncludeNameChanged(bool value) => Refresh();

    partial void OnPreviewChanged(string value) => OnPropertyChanged(nameof(HasPreview));

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    private void OnPartChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportPartViewModel.Include))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        Preview = Build() is { } document && Parts.Any(p => p.Include) ? ReportRenderer.Text(document, Included()) : string.Empty;
        NotifyFlags();
    }

    private void NotifyFlags()
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ShowNothingChosen));
    }
}
