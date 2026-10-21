using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Services;
using M1vClock.Core.Paths;
using M1vClock.Core.Settings;
using M1vClock.Core.Shell;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Search;

public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly NavigationService _navigation;
    private readonly IReadOnlyList<SearchItem> _items;
    private readonly Dictionary<string, Action> _actions;
    private readonly ILogger<CommandPaletteViewModel> _logger;

    public CommandPaletteViewModel(
        NavigationService navigation,
        ThemeService theme,
        IShellLauncher shell,
        AppPaths paths,
        ILogger<CommandPaletteViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(paths);
        _navigation = navigation;
        _logger = logger;
        _actions = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["action.theme.light"] = () => theme.SetPreference(ThemePreference.Light),
            ["action.theme.dark"] = () => theme.SetPreference(ThemePreference.Dark),
            ["action.theme.system"] = () => theme.SetPreference(ThemePreference.System),
            ["action.logs.open"] = () => shell.OpenFolder(paths.EnsureDirectory(paths.Logs)),
        };

        var pages = PageCatalog.All.Select(p => new SearchItem(
            p.Id,
            p.Title,
            PageCatalog.SectionTitle(p.Section),
            p.IconKey,
            p.Keywords,
            SearchItemKind.Page));
        var actions = new[]
        {
            Action("action.theme.light", "Sun"),
            Action("action.theme.dark", "Moon"),
            Action("action.theme.system", "SunMoon"),
            Action("action.logs.open", "Folder"),
        };
        _items = [.. pages, .. actions];
        Refresh();
    }

    public ObservableCollection<SearchItem> Results { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SearchItem? Selected { get; set; }

    public bool HasResults => Results.Count > 0;

    public void Open()
    {
        Query = string.Empty;
        Refresh();
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        index = Math.Clamp(index + delta, 0, Results.Count - 1);
        Selected = Results[index];
    }

    [RelayCommand]
    public void Execute(SearchItem? item)
    {
        var target = item ?? Selected;
        if (target is null)
        {
            return;
        }

        Close();
        try
        {
            if (target.Kind == SearchItemKind.Page)
            {
                _navigation.Navigate(target.Id);
            }
            else if (_actions.TryGetValue(target.Id, out var action))
            {
                action();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.ActionFailed(_logger, ex, target.Id);
        }
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        var found = SearchIndex.Query(_items, Query, 12);
        Results.Clear();
        foreach (var item in found)
        {
            Results.Add(item);
        }

        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(HasResults));
    }

    private static SearchItem Action(string id, string icon) => new(
        id,
        Loc.Get($"Action.{id["action.".Length..]}.Title"),
        Loc.Get("Search.Kind.Action"),
        $"Icon.{icon}",
        Loc.Get($"Action.{id["action.".Length..]}.Keywords").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        SearchItemKind.Action);
}
