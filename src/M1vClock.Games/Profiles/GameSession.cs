using System.ComponentModel;
using System.Runtime.InteropServices;
using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.Shell;
using M1vClock.Games.Library;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Profiles;

public enum GameSessionStage
{
    Applying,
    OpeningHelpers,
    Launching,
    WaitingForGame,
    Running,
    Finished,
    NotDetected,
    Failed,
    Cancelled,
}

public enum GameSessionWarning
{
    PreferenceFailed,
    PriorityDenied,
    HelperFailed,
    HelperNotClosed,
    ArgumentsNotSupported,
}

public sealed record GameSessionStatus(GameSessionStage Stage, string Game, int? ProcessId, IReadOnlyList<GameSessionWarning> Warnings, string? Error = null)
{
    public bool IsOver => Stage is GameSessionStage.Finished or GameSessionStage.NotDetected or GameSessionStage.Failed or GameSessionStage.Cancelled;
}

public sealed record GameSessionTitles(string GpuPreference, string FullscreenOptimizations);

public sealed record GameSessionTiming(TimeSpan Poll, TimeSpan GiveUpAfter, TimeSpan RelaunchWindow, TimeSpan HelperHeadStart)
{
    public static GameSessionTiming Default { get; } = new(TimeSpan.FromSeconds(1.5), TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1.5));
}

public static class SessionArguments
{
    public static GameLaunch? Resolve(GameEntry game, GameProfile profile, out bool argumentsIgnored)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(profile);
        argumentsIgnored = false;
        if (game.Launch is not { } launch)
        {
            return null;
        }

        var built = LaunchArguments.Build(profile);
        if (!launch.IsExecutable)
        {
            argumentsIgnored = built.Length > 0 && game.Platform != GamePlatform.Steam;
            return launch;
        }

        var combined = string.Join(' ', new[] { launch.Arguments, built }.Where(a => !string.IsNullOrWhiteSpace(a)));
        return launch with { Arguments = combined.Length == 0 ? null : combined };
    }
}

public sealed class GameSession(
    IShellLauncher shell,
    IGameFileSystem files,
    IGameProcesses processes,
    IChangeJournal journal,
    IRegistryAccess registry,
    TimeProvider time,
    ILogger<GameSession> logger)
{
    public GameSessionTiming Timing { get; init; } = GameSessionTiming.Default;

    public async Task<GameSessionStatus> RunAsync(GameEntry game, GameProfile profile, GameSessionTitles titles, IProgress<GameSessionStatus> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(progress);
        var warnings = new List<GameSessionWarning>();
        var helpers = new List<RunningProcess>();
        GameSessionStatus Report(GameSessionStage stage, int? processId = null, string? error = null)
        {
            var status = new GameSessionStatus(stage, game.Name, processId, warnings.Distinct().ToList(), error);
            progress.Report(status);
            return status;
        }

        GameSessionStatus final;
        try
        {
            if (LaunchArguments.Validate(profile) != ProfileProblem.None)
            {
                throw new ArgumentException("Perfil inválido.", nameof(profile));
            }

            var launch = SessionArguments.Resolve(game, profile, out var ignored) ?? throw new InvalidOperationException("Este jogo não tem como ser aberto.");
            if (ignored)
            {
                warnings.Add(GameSessionWarning.ArgumentsNotSupported);
            }

            Report(GameSessionStage.Applying);
            var exe = profile.ExePath ?? (launch.IsExecutable ? launch.Target : null);
            if (exe is not null)
            {
                await ApplyPreferencesAsync(exe, profile, titles, warnings, cancellationToken).ConfigureAwait(false);
            }

            var before = processes.List().Select(p => p.Id).ToHashSet();
            if (profile.Helpers.Count > 0)
            {
                Report(GameSessionStage.OpeningHelpers);
                OpenHelpers(profile, warnings);
                await Task.Delay(Timing.HelperHeadStart, time, cancellationToken).ConfigureAwait(false);
                var helperPaths = profile.Helpers.Where(h => h.CloseWithGame).Select(h => Path.GetFullPath(h.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                helpers.AddRange(processes.List().Where(p => !before.Contains(p.Id) && helperPaths.Contains(p.Path)));
            }

            Report(GameSessionStage.Launching);
            var directory = launch.IsExecutable ? Path.GetDirectoryName(launch.Target) : null;
            shell.Start(launch.Target, launch.Arguments, directory);
            Report(GameSessionStage.WaitingForGame);

            var ignoredIds = before.Concat(helpers.Select(h => h.Id)).ToHashSet();
            var found = await WaitForGameAsync(game, profile, ignoredIds, Timing.GiveUpAfter, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                final = Report(GameSessionStage.NotDetected);
            }
            else
            {
                while (found is not null)
                {
                    ProfileLog.GameDetected(logger, game.Name, found.Id);
                    if (profile.Priority != GamePriority.Normal && !processes.SetPriority(found.Id, profile.Priority))
                    {
                        warnings.Add(GameSessionWarning.PriorityDenied);
                    }

                    Report(GameSessionStage.Running, found.Id);
                    while (processes.IsRunning(found.Id))
                    {
                        await Task.Delay(Timing.Poll, time, cancellationToken).ConfigureAwait(false);
                    }

                    ignoredIds.Add(found.Id);
                    found = await WaitForGameAsync(game, profile, ignoredIds, Timing.RelaunchWindow, cancellationToken).ConfigureAwait(false);
                }

                final = Report(GameSessionStage.Finished);
            }
        }
        catch (OperationCanceledException)
        {
            final = new GameSessionStatus(GameSessionStage.Cancelled, game.Name, null, warnings.Distinct().ToList());
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            ProfileLog.LaunchFailed(logger, ex, game.Name);
            final = new GameSessionStatus(GameSessionStage.Failed, game.Name, null, warnings.Distinct().ToList(), ex.Message);
        }

        foreach (var helper in helpers)
        {
            if (!processes.Close(helper.Id))
            {
                warnings.Add(GameSessionWarning.HelperNotClosed);
            }
        }

        final = final with { Warnings = warnings.Distinct().ToList() };
        progress.Report(final);
        ProfileLog.SessionEnded(logger, game.Name, final.Stage);
        return final;
    }

    public static bool BelongsToGame(RunningProcess process, GameEntry game, GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ExePath is { } exe && string.Equals(Path.GetFullPath(exe), process.Path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (game.InstallFolder is not { Length: > 3 } folder)
        {
            return false;
        }

        var prefix = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        return process.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !LauncherNames.Contains(Path.GetFileNameWithoutExtension(process.Path));
    }

    private static readonly HashSet<string> LauncherNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnityCrashHandler64", "UnityCrashHandler32", "CrashReportClient", "CrashReport", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEService_x64",
    };

    private async Task<RunningProcess?> WaitForGameAsync(GameEntry game, GameProfile profile, HashSet<int> ignored, TimeSpan limit, CancellationToken cancellationToken)
    {
        var start = time.GetTimestamp();
        while (true)
        {
            var match = processes.List().FirstOrDefault(p => !ignored.Contains(p.Id) && BelongsToGame(p, game, profile));
            if (match is not null)
            {
                return match;
            }

            if (time.GetElapsedTime(start) >= limit)
            {
                return null;
            }

            await Task.Delay(Timing.Poll, time, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyPreferencesAsync(string exe, GameProfile profile, GameSessionTitles titles, List<GameSessionWarning> warnings, CancellationToken cancellationToken)
    {
        IReadOnlyList<PreferenceChange> changes;
        try
        {
            changes = GamePreferences.ChangesFor(registry, Path.GetFullPath(exe), profile, titles.GpuPreference, titles.FullscreenOptimizations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ProfileLog.PreferenceFailed(logger, exe, ex.Message);
            warnings.Add(GameSessionWarning.PreferenceFailed);
            return;
        }

        foreach (var change in changes)
        {
            var outcome = await journal.ApplyAsync(change.Change, cancellationToken).ConfigureAwait(false);
            if (!outcome.Succeeded)
            {
                ProfileLog.PreferenceFailed(logger, exe, outcome.Entry.Error);
                warnings.Add(GameSessionWarning.PreferenceFailed);
            }
        }
    }

    private void OpenHelpers(GameProfile profile, List<GameSessionWarning> warnings)
    {
        foreach (var helper in profile.Helpers)
        {
            try
            {
                if (!files.FileExists(helper.Path))
                {
                    throw new FileNotFoundException("Programa auxiliar não encontrado.", helper.Path);
                }

                shell.Start(Path.GetFullPath(helper.Path), string.IsNullOrWhiteSpace(helper.Arguments) ? null : helper.Arguments.Trim(), Path.GetDirectoryName(helper.Path));
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or COMException or ArgumentException)
            {
                ProfileLog.HelperFailed(logger, ex, helper.Path);
                warnings.Add(GameSessionWarning.HelperFailed);
            }
        }
    }
}
