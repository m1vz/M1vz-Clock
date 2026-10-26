using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Dialogs;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Core.Shell;
using M1vClock.Games.Library;
using M1vClock.Games.Profiles;
using M1vClock.Games.Settings;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Games;

public sealed record LibraryServices(
    IGameLibraryService Library,
    IShellLauncher Shell,
    GameSessionHost Sessions,
    ProfileEditorServices Editor,
    DialogService Dialogs,
    IUiThread Ui);

public sealed partial class LibraryPageViewModel : HardwarePageViewModel
{
    private static readonly GamePlatform?[] Platforms = [null, GamePlatform.Steam, GamePlatform.Epic, GamePlatform.Riot, GamePlatform.Gog, GamePlatform.Ubisoft, GamePlatform.Manual];
    private static readonly GameSort[] Sorts = [GameSort.Name, GameSort.Size];

    private readonly LibraryServices _services;
    private readonly ILogger<LibraryPageViewModel> _logger;
    private GameLibrary? _loaded;

    public LibraryPageViewModel(LibraryServices services, ILogger<LibraryPageViewModel> logger)
        : base(PageCatalog.Find("library"), logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _logger = logger;
        PlatformLabels = Platforms.Select(p => p is { } platform ? GameRows.PlatformText(platform) : Loc.Get("Game.Library.Filter.All")).ToList();
        ShowSession();
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

    [ObservableProperty]
    public partial string SessionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SessionActive { get; set; }

    [ObservableProperty]
    public partial ProfileEditorViewModel? Editor { get; set; }

    public bool IsEditing => Editor is not null;

    public bool IsBrowsing => Editor is null;

    public bool HasShowing => ShowingText.Length > 0;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool HasSession => SessionText.Length > 0;

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    public override void OnNavigatedTo()
    {
        _services.Sessions.Changed += OnSessionChanged;
        ShowSession();
        base.OnNavigatedTo();
    }

    public override void OnNavigatedFrom()
    {
        _services.Sessions.Changed -= OnSessionChanged;
        base.OnNavigatedFrom();
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var library = await _services.Library.ReadAsync(cancellationToken).ConfigureAwait(true);
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
        var profile = _services.Editor.Settings.Read(s => s.Profiles.TryGetValue(GameProfile.KeyOf(row.Game), out var p) ? p.Copy() : null);
        if (profile is not null)
        {
            if (!_services.Sessions.TryStart(row.Game, profile))
            {
                Problem = Loc.Get("Game.Session.Busy");
                return;
            }

            Problem = string.Empty;
            ShowSession();
            return;
        }

        try
        {
            var directory = launch.IsExecutable ? Path.GetDirectoryName(launch.Target) : null;
            _services.Shell.Start(launch.Target, launch.Arguments, directory);
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
    public void StopSession() => _services.Sessions.Stop();

    [RelayCommand]
    public void OpenFolder(GameRowViewModel? row)
    {
        if (row?.Game.InstallFolder is not { Length: > 0 } folder || !row.CanOpenFolder)
        {
            return;
        }

        try
        {
            _services.Shell.OpenFolder(folder);
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            AppLog.GameLaunchFailed(_logger, ex, row.Title);
            Problem = Loc.Format("Game.Library.FolderFailed", row.Title, ex.Message);
        }
    }

    [RelayCommand]
    public void EditProfile(GameRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Notice = string.Empty;
        Editor = new ProfileEditorViewModel(row.Game, _services.Editor, saved =>
        {
            Editor = null;
            if (saved)
            {
                Notice = Loc.Format("Game.Profile.Saved", row.Title);
            }

            ShowItems();
        });
    }

    [RelayCommand]
    public async Task AddGameAsync()
    {
        var picked = _services.Editor.Files.PickOpen(Loc.Get("Game.Library.Add.Pick"), Loc.Get("Game.Profile.Exe.Filter"));
        if (picked is null)
        {
            return;
        }

        var existing = _services.Editor.Settings.Read(s => s.ManualGames.ToList());
        var problem = ManualGames.Check(existing, picked, null, _services.Editor.FileSystem);
        if (problem != ManualGameProblem.None)
        {
            Problem = Loc.Get($"Game.Library.Add.Problem.{problem}");
            return;
        }

        var name = ManualGames.NameFor(null, picked);
        _services.Editor.Settings.Update(s => s.ManualGames.Add(new ManualGame { Name = name, ExePath = Path.GetFullPath(picked) }));
        Problem = string.Empty;
        Notice = Loc.Format("Game.Library.Add.Done", name);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RemoveGameAsync(GameRowViewModel? row)
    {
        if (row is null || ManualGames.IdOf(row.Game) is not { } id)
        {
            return;
        }

        var confirmed = await _services.Dialogs.ShowAsync(new DialogRequest(
            Loc.Format("Game.Library.Remove.Title", row.Title),
            Loc.Get("Game.Library.Remove.Message"),
            [],
            null,
            Loc.Get("Game.Library.Remove.Confirm"),
            Loc.Get("Common.Cancel"),
            DialogTone.Destructive)).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var key = GameProfile.KeyOf(row.Game);
        _services.Editor.Settings.Update(s =>
        {
            s.ManualGames.RemoveAll(g => g.Id == id);
            s.Profiles.Remove(key);
        });
        Notice = Loc.Format("Game.Library.Remove.Done", row.Title);
        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnPlatformIndexChanged(int value) => ShowItems();

    partial void OnSortIndexChanged(int value) => ShowItems();

    partial void OnSearchTextChanged(string value) => ShowItems();

    partial void OnShowingTextChanged(string value) => OnPropertyChanged(nameof(HasShowing));

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    partial void OnSessionTextChanged(string value) => OnPropertyChanged(nameof(HasSession));

    partial void OnEditorChanged(ProfileEditorViewModel? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsBrowsing));
    }

    private static bool IsLaunchFailure(Exception ex) =>
        ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or COMException or ArgumentException;

    private void OnSessionChanged(object? sender, EventArgs e) => _services.Ui.Post(ShowSession);

    private void ShowSession()
    {
        var status = _services.Sessions.Status;
        SessionText = status is null ? string.Empty : GameSessionText.Describe(status);
        SessionActive = status is { IsOver: false };
    }

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
        var profiles = _services.Editor.Settings.Read(s => s.Profiles.Keys.ToHashSet(StringComparer.Ordinal));
        var rows = GameRows.Filter(library.Games, platform, sort, SearchText, profiles, out var matching);
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        ShowingText = matching > rows.Count ? Loc.Format("Game.Library.Showing", rows.Count.ToString("N0", Loc.Culture), matching.ToString("N0", Loc.Culture)) : string.Empty;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
