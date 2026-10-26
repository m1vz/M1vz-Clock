using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Performance;
using M1vClock.Games.Library;
using M1vClock.Games.Profiles;
using M1vClock.Games.Settings;

namespace M1vClock.App.Pages.Games;

public sealed partial class HelperRowViewModel : ObservableObject
{
    public HelperRowViewModel(HelperProgram helper)
    {
        ArgumentNullException.ThrowIfNull(helper);
        Path = helper.Path;
        Arguments = helper.Arguments;
        CloseWithGame = helper.CloseWithGame;
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    [ObservableProperty]
    public partial string Arguments { get; set; }

    [ObservableProperty]
    public partial bool CloseWithGame { get; set; }

    public HelperProgram ToModel() => new() { Path = Path, Arguments = Arguments.Trim(), CloseWithGame = CloseWithGame };

    public override string ToString() => Name;
}

public sealed record ProfileEditorServices(IGameSettings Settings, ISteamLaunchOptions Steam, IFileDialogs Files, IGameFileSystem FileSystem);

public sealed partial class ProfileEditorViewModel : ObservableObject
{
    private static readonly GameEngine[] Engines = [GameEngine.Unknown, GameEngine.Source, GameEngine.Unity, GameEngine.Unreal];
    private static readonly WindowMode[] Modes = [WindowMode.Default, WindowMode.Fullscreen, WindowMode.Borderless, WindowMode.Windowed];
    private static readonly GamePriority[] Priorities = [GamePriority.Normal, GamePriority.AboveNormal, GamePriority.High];

    private readonly ProfileEditorServices _services;
    private readonly Action<bool> _close;

    public ProfileEditorViewModel(GameEntry game, ProfileEditorServices services, Action<bool> close)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(close);
        Game = game;
        _services = services;
        _close = close;
        Key = GameProfile.KeyOf(game);
        var saved = services.Settings.Read(s => s.Profiles.TryGetValue(Key, out var p) ? p.Copy() : null);
        Detected = EngineDetector.Detect(services.FileSystem, game.InstallFolder);
        var profile = saved ?? new GameProfile { Engine = Detected };
        HasSaved = saved is not null;
        EngineIndex = Math.Max(0, Array.IndexOf(Engines, profile.Engine));
        WindowModeIndex = Math.Max(0, Array.IndexOf(Modes, profile.WindowMode));
        PriorityIndex = Math.Max(0, Array.IndexOf(Priorities, profile.Priority));
        WidthText = Number(profile.Width);
        HeightText = Number(profile.Height);
        RefreshText = Number(profile.RefreshRate);
        ExtraText = profile.Extra;
        HighPerformanceGpu = profile.HighPerformanceGpu;
        DisableFullscreenOptimizations = profile.DisableFullscreenOptimizations;
        ExePath = profile.ExePath ?? string.Empty;
        foreach (var helper in profile.Helpers)
        {
            Helpers.Add(new HelperRowViewModel(helper));
        }

        EngineLabels = Engines.Select(e => Loc.Get($"Game.Profile.Engine.{e}")).ToList();
        WindowModeLabels = Modes.Select(m => Loc.Get($"Game.Profile.Window.{m}")).ToList();
        PriorityLabels = Priorities.Select(p => Loc.Get($"Game.Profile.Priority.{p}")).ToList();
        DetectedText = Detected == GameEngine.Unknown
            ? Loc.Get("Game.Profile.Engine.NotDetected")
            : Loc.Format("Game.Profile.Engine.Detected", Loc.Get($"Game.Profile.Engine.{Detected}"));
        ArgumentsNote = Loc.Get(game.Launch?.IsExecutable == true ? "Game.Profile.Args.Direct"
            : game.Platform == GamePlatform.Steam ? "Game.Profile.Args.Steam" : "Game.Profile.Args.Unsupported");
        IsSteam = game.Platform == GamePlatform.Steam && GameLaunchIds.IsSteamAppId(game.Id);
        SteamCurrent = IsSteam ? services.Steam.Read(game.Id) ?? string.Empty : string.Empty;
        Helpers.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    public GameEntry Game { get; }

    public string Key { get; }

    public GameEngine Detected { get; }

    public bool HasSaved { get; private set; }

    public string Heading => Loc.Format("Game.Profile.Heading", Game.Name);

    public IReadOnlyList<string> EngineLabels { get; }

    public IReadOnlyList<string> WindowModeLabels { get; }

    public IReadOnlyList<string> PriorityLabels { get; }

    public string DetectedText { get; }

    public string ArgumentsNote { get; }

    public bool IsSteam { get; }

    public string SteamCurrent { get; private set; }

    public string SteamCurrentText => SteamCurrent.Length == 0 ? Loc.Get("Game.Profile.Steam.None") : Loc.Format("Game.Profile.Steam.Current", SteamCurrent);

    public ObservableCollection<HelperRowViewModel> Helpers { get; } = [];

    [ObservableProperty]
    public partial int EngineIndex { get; set; }

    [ObservableProperty]
    public partial int WindowModeIndex { get; set; }

    [ObservableProperty]
    public partial int PriorityIndex { get; set; }

    [ObservableProperty]
    public partial string WidthText { get; set; }

    [ObservableProperty]
    public partial string HeightText { get; set; }

    [ObservableProperty]
    public partial string RefreshText { get; set; }

    [ObservableProperty]
    public partial string ExtraText { get; set; }

    [ObservableProperty]
    public partial bool HighPerformanceGpu { get; set; }

    [ObservableProperty]
    public partial bool DisableFullscreenOptimizations { get; set; }

    [ObservableProperty]
    public partial string ExePath { get; set; }

    [ObservableProperty]
    public partial string Preview { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public bool HasNotice => Notice.Length > 0;

    public bool HasExe => ExePath.Length > 0;

    public string ExeText => ExePath.Length > 0 ? ExePath : Loc.Get("Game.Profile.Exe.None");

    public GameProfile? Build(out ProfileProblem problem)
    {
        problem = ProfileProblem.None;
        if (!TryNumber(WidthText, out var width) || !TryNumber(HeightText, out var height))
        {
            problem = ProfileProblem.ResolutionOutOfRange;
            return null;
        }

        if (!TryNumber(RefreshText, out var refresh))
        {
            problem = ProfileProblem.RefreshOutOfRange;
            return null;
        }

        var profile = new GameProfile
        {
            Engine = Engines[Math.Clamp(EngineIndex, 0, Engines.Length - 1)],
            WindowMode = Modes[Math.Clamp(WindowModeIndex, 0, Modes.Length - 1)],
            Priority = Priorities[Math.Clamp(PriorityIndex, 0, Priorities.Length - 1)],
            Width = width,
            Height = height,
            RefreshRate = refresh,
            Extra = (ExtraText ?? string.Empty).Trim(),
            HighPerformanceGpu = HighPerformanceGpu,
            DisableFullscreenOptimizations = DisableFullscreenOptimizations,
            ExePath = ExePath.Length > 0 ? ExePath : null,
            Helpers = Helpers.Select(h => h.ToModel()).ToList(),
        };
        problem = LaunchArguments.Validate(profile);
        return problem == ProfileProblem.None ? profile : null;
    }

    [RelayCommand]
    public void Save()
    {
        if (Build(out var problem) is not { } profile)
        {
            Problem = Loc.Get($"Game.Profile.Problem.{problem}");
            return;
        }

        _services.Settings.Update(s =>
        {
            if (profile.IsEmpty || (profile.Engine == Detected && profile.WithoutEngine().IsEmpty))
            {
                s.Profiles.Remove(Key);
            }
            else
            {
                s.Profiles[Key] = profile;
            }
        });
        HasSaved = true;
        _close(true);
    }

    [RelayCommand]
    public void Cancel() => _close(false);

    [RelayCommand]
    public void PickExe()
    {
        var picked = _services.Files.PickOpen(Loc.Get("Game.Profile.Exe.Pick"), Loc.Get("Game.Profile.Exe.Filter"));
        if (picked is null)
        {
            return;
        }

        if (!GameLaunch.IsExecutablePath(picked))
        {
            Problem = Loc.Get("Game.Profile.Problem.BadExecutable");
            return;
        }

        ExePath = Path.GetFullPath(picked);
    }

    [RelayCommand]
    public void ClearExe() => ExePath = string.Empty;

    [RelayCommand]
    public void AddHelper()
    {
        var picked = _services.Files.PickOpen(Loc.Get("Game.Profile.Helper.Pick"), Loc.Get("Game.Profile.Exe.Filter"));
        if (picked is null)
        {
            return;
        }

        if (!GameLaunch.IsExecutablePath(picked))
        {
            Problem = Loc.Get("Game.Profile.Problem.BadHelper");
            return;
        }

        Helpers.Add(new HelperRowViewModel(new HelperProgram { Path = Path.GetFullPath(picked) }));
    }

    [RelayCommand]
    public void RemoveHelper(HelperRowViewModel? helper)
    {
        if (helper is not null)
        {
            Helpers.Remove(helper);
        }
    }

    [RelayCommand]
    public void WriteSteam()
    {
        if (!IsSteam)
        {
            return;
        }

        if (Build(out var problem) is not { } profile)
        {
            Problem = Loc.Get($"Game.Profile.Problem.{problem}");
            return;
        }

        var options = LaunchArguments.Build(profile);
        var result = _services.Steam.Write(Game.Id, options);
        if (result is SteamOptionsResult.Written or SteamOptionsResult.Unchanged)
        {
            SteamCurrent = options;
            OnPropertyChanged(nameof(SteamCurrentText));
            Problem = string.Empty;
            Notice = Loc.Get($"Game.Profile.Steam.Result.{result}");
            return;
        }

        Notice = string.Empty;
        Problem = Loc.Get($"Game.Profile.Steam.Result.{result}");
    }

    partial void OnEngineIndexChanged(int value) => Refresh();

    partial void OnWindowModeIndexChanged(int value) => Refresh();

    partial void OnWidthTextChanged(string value) => Refresh();

    partial void OnHeightTextChanged(string value) => Refresh();

    partial void OnRefreshTextChanged(string value) => Refresh();

    partial void OnExtraTextChanged(string value) => Refresh();

    partial void OnExePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasExe));
        OnPropertyChanged(nameof(ExeText));
    }

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    private void Refresh()
    {
        if (EngineLabels is null)
        {
            return;
        }

        if (Build(out var problem) is { } profile)
        {
            var built = LaunchArguments.Build(profile);
            Preview = built.Length == 0 ? Loc.Get("Game.Profile.Preview.None") : built;
            Problem = string.Empty;
        }
        else
        {
            Preview = Loc.Get("Game.Profile.Preview.None");
            Problem = Loc.Get($"Game.Profile.Problem.{problem}");
        }
    }

    private static string Number(int value) => value == 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);

    private static bool TryNumber(string? text, out int value)
    {
        value = 0;
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length == 0 || int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}

public static class GameProfileExtensions
{
    public static GameProfile WithoutEngine(this GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var copy = profile.Copy();
        copy.Engine = GameEngine.Unknown;
        return copy;
    }
}
