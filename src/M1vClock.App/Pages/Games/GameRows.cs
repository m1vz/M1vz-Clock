using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Games.Library;

namespace M1vClock.App.Pages.Games;

public sealed class GameRowViewModel
{
    public GameRowViewModel(GameEntry game, bool hasProfile = false)
    {
        ArgumentNullException.ThrowIfNull(game);
        Game = game;
        HasProfile = hasProfile;
        IsManual = game.Platform == GamePlatform.Manual;
        ProfileText = Loc.Get(hasProfile ? "Game.Profile.Edit" : "Game.Profile.Create");
        Title = game.Name;
        PlatformText = GameRows.PlatformText(game.Platform);
        StateText = Loc.Get($"Game.Library.State.{game.State}");
        Tone = game.State switch
        {
            GameState.Installed => MetricState.Good,
            GameState.UpdatePending or GameState.Updating => MetricState.Neutral,
            GameState.Incomplete => MetricState.Warning,
            _ => MetricState.Critical,
        };
        var size = game.SizeBytes is { } bytes ? Units.Bytes(bytes, Loc.Culture) : Loc.Get("Game.Library.Meta.SizeUnknown");
        Meta = game.InstallFolder is { Length: > 0 } folder ? $"{PlatformText} · {size} · {folder}" : $"{PlatformText} · {size}";
        CanPlay = game.Launch is not null;
        PlayToolTip = CanPlay ? string.Empty : Loc.Get("Game.Library.Tip.NoLaunch");
        CanOpenFolder = game.FolderExists;
        FolderToolTip = CanOpenFolder ? string.Empty : Loc.Get("Game.Library.Tip.NoFolder");
    }

    public GameEntry Game { get; }

    public bool HasProfile { get; }

    public bool IsManual { get; }

    public string ProfileText { get; }

    public string Title { get; }

    public string PlatformText { get; }

    public string StateText { get; }

    public MetricState Tone { get; }

    public string Meta { get; }

    public bool CanPlay { get; }

    public string PlayToolTip { get; }

    public bool CanOpenFolder { get; }

    public string FolderToolTip { get; }

    public string AccessibleName => HasProfile ? $"{Title}, {PlatformText}, {StateText}, {Loc.Get("Game.Profile.Badge")}" : $"{Title}, {PlatformText}, {StateText}";

    public string ProfileBadge { get; } = Loc.Get("Game.Profile.Badge");

    public override string ToString() => AccessibleName;
}

public static class GameRows
{
    public const int MaximumShown = 300;

    public static string PlatformText(GamePlatform platform) => Loc.Get($"Game.Library.Platform.{platform}");

    public static IReadOnlyList<GameRowViewModel> Filter(IReadOnlyList<GameEntry> games, GamePlatform? platform, GameSort sort, string search, out int matching) =>
        Filter(games, platform, sort, search, new HashSet<string>(StringComparer.Ordinal), out matching);

    public static IReadOnlyList<GameRowViewModel> Filter(IReadOnlyList<GameEntry> games, GamePlatform? platform, GameSort sort, string search, IReadOnlySet<string> profiles, out int matching)
    {
        ArgumentNullException.ThrowIfNull(games);
        var text = (search ?? string.Empty).Trim();
        var found = games
            .Where(g => platform is null || g.Platform == platform)
            .Where(g => text.Length == 0 || g.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        matching = found.Count;
        return GameLibraryService.Sort(found, sort).Take(MaximumShown).Select(g => new GameRowViewModel(g, profiles.Contains(M1vClock.Games.Profiles.GameProfile.KeyOf(g)))).ToList();
    }
}
