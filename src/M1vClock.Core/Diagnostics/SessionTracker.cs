using System.Text.Json;
using M1vClock.Core.IO;

namespace M1vClock.Core.Diagnostics;

public sealed record CrashInfo(DateTimeOffset At, string ExceptionType, string Message, string StackTrace);

public sealed record PreviousSession(DateTimeOffset StartedAt, string Version, CrashInfo? Crash);

public sealed class SessionTracker
{
    private const int MaxStackLines = 40;

    private readonly string _sessionFile;
    private readonly string _crashFile;
    private readonly TimeProvider _time;

    public SessionTracker(string stateDirectory, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentNullException.ThrowIfNull(time);
        _sessionFile = Path.Combine(stateDirectory, "session.json");
        _crashFile = Path.Combine(stateDirectory, "last-crash.json");
        _time = time;
    }

    public PreviousSession? Start(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var previous = Read<SessionState>(_sessionFile);
        var crash = Read<CrashInfo>(_crashFile);
        PreviousSession? unexpected = null;
        if (previous is { Running: true })
        {
            unexpected = new PreviousSession(previous.StartedAt, previous.Version, crash);
        }

        if (File.Exists(_crashFile))
        {
            File.Delete(_crashFile);
        }

        AtomicFile.WriteAllText(_sessionFile, JsonSerializer.Serialize(new SessionState(true, _time.GetUtcNow(), version)));
        return unexpected;
    }

    public void RecordCrash(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var stack = string.Join(Environment.NewLine, (exception.ToString()).Split(Environment.NewLine).Take(MaxStackLines));
        var crash = new CrashInfo(_time.GetUtcNow(), exception.GetType().FullName ?? exception.GetType().Name, exception.Message, stack);
        AtomicFile.WriteAllText(_crashFile, JsonSerializer.Serialize(crash));
    }

    public void EndCleanly()
    {
        var current = Read<SessionState>(_sessionFile);
        if (current is null)
        {
            return;
        }

        AtomicFile.WriteAllText(_sessionFile, JsonSerializer.Serialize(current with { Running = false }));
    }

    private static T? Read<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            File.Delete(path);
            return null;
        }
    }

    private sealed record SessionState(bool Running, DateTimeOffset StartedAt, string Version);
}
