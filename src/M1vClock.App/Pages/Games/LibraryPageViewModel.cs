using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Shell;
using M1vClock.Games.Library;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Games;

public sealed partial class LibraryPageViewModel : HardwarePageViewModel
{
    private static readonly GamePlatform?[] Platforms = [null, GamePlatform.Steam, GamePlatform.Epic, GamePlatform.Riot];
    private static readonly GameSort[] Sorts = [GameSort.Name, GameSort.Size];

    private readonly IGameLibraryService _library;
    private readonly IShellLauncher _shell;
    private readonly ILogger<LibraryPageViewModel> _logger;
    private GameLibrary? _loaded;

    public LibraryPageViewModel(IGameLibraryService library, IShellLauncher shell, ILogger<LibraryPageViewModel> logger)
        : base(PageCatalog.Find("library"), logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(shell);
        _library = library;
        _shell = shell;
        _logger = logger;
        PlatformLabels = [Loc.Get("Game.Library.Filter.All"), GameRows.PlatformText(GamePlatform.Steam), GameRows.PlatformText(GamePlatform.Epic), GameRows.PlatformText(GamePlatform.Riot)];
    }

    public ObservableCollection<GameRowViewModel> Items { get; } = [];

    public IReadOnlyList<string> PlatformLabels { get; }

    public IReadOnlyList<string> SortLabels { get; } = [Loc.Get("Game.Library.Sort.Name"), Loc.Get("Game.Library.Sort.Size")];

    [ObservableProperty]
    public partial int PlatformIndex { get; set; }

    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string TotalText { get; set; } = "—";

    [ObservableProperty]
    public partial string SizeText { get; set; } = "—";

    [ObservableProperty]
    public partial string PlatformsText { get; set; } = "—";

    [ObservableProperty]
    public partial string AttentionText { get; set; } = "—";

    [ObservableProperty]
    public partial string ShowingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    public bool HasShowing => ShowingText.Length > 0;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var library = await _library.ReadAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            _loaded = library;
            TotalText = library.Games.Count.ToString("N0", Loc.Culture);
            SizeText = Units.Bytes(library.KnownBytes, Loc.Culture);
            PlatformsText = library.Platforms.ToString("N0", Loc.Culture);
            AttentionText = library.NeedAttention.ToString("N0", Loc.Culture);
            Problem = string.Join(" ", library.Failed.Select(p => Loc.Format("Game.Library.ReadFailed", GameRows.PlatformText(p))));
            ShowItems();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    public void Play(GameRowViewModel? row)
    {
        if (row?.Game.Launch is not { } launch)
        {
            return;
        }

        Notice = string.Empty;
        try
        {
            var directory = Path.IsPathFullyQualified(launch.Target) ? Path.GetDirectoryName(launch.Target) : null;
            _shell.Start(launch.Target, launch.Arguments, directory);
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            AppLog.GameLaunchFailed(_logger, ex, row.Title);
            Problem = Loc.Format("Game.Library.PlayFailed", row.Title, ex.Message);
            return;
        }

        Problem = string.Empty;
        Notice = Loc.Format("Game.Library.Opening", row.Title, row.PlatformText);
    }

    [RelayCommand]
    public void OpenFolder(GameRowViewModel? row)
    {
        if (row?.Game.InstallFolder is not { Length: > 0 } folder || !row.CanOpenFolder)
        {
            return;
        }

        try
        {
            _shell.OpenFolder(folder);
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            AppLog.GameLaunchFailed(_logger, ex, row.Title);
            Problem = Loc.Format("Game.Library.FolderFailed", row.Title, ex.Message);
        }
    }

    partial void OnPlatformIndexChanged(int value) => ShowItems();

    partial void OnSortIndexChanged(int value) => ShowItems();

    partial void OnSearchTextChanged(string value) => ShowItems();

    partial void OnShowingTextChanged(string value) => OnPropertyChanged(nameof(HasShowing));

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    private static bool IsLaunchFailure(Exception ex) =>
        ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or COMException or ArgumentException;

    private void ShowItems()
    {
        Items.Clear();
        if (_loaded is not { } library)
        {
            ShowingText = string.Empty;
            return;
        }

        var platform = Platforms[Math.Clamp(PlatformIndex, 0, Platforms.Length - 1)];
        var sort = Sorts[Math.Clamp(SortIndex, 0, Sorts.Length - 1)];
        var rows = GameRows.Filter(library.Games, platform, sort, SearchText, out var matching);
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        ShowingText = matching > rows.Count ? Loc.Format("Game.Library.Showing", rows.Count.ToString("N0", Loc.Culture), matching.ToString("N0", Loc.Culture)) : string.Empty;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
