using System.Diagnostics;
using M1vClock.Benchmarks.Io;
using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks.Tests;

internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);

internal sealed class CollectingLogger<T> : ILogger<T>
{
    private readonly Lock _gate = new();
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var entry = new LogEntry(logLevel, eventId, formatter(state, exception), exception);
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }
}

internal sealed class ScratchDirectory : IDisposable
{
    public ScratchDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "m1vclock-bench-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public IReadOnlyList<string> BenchFolders() =>
        [.. Directory.EnumerateDirectories(Path, BenchTempArea.Prefix + "*")];

    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        foreach (var entry in new DirectoryInfo(Path).EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if (entry.Exists && entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                entry.Delete();
            }
        }

        Directory.Delete(Path, recursive: true);
    }
}

internal static class Junction
{
    public static void Create(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        }) ?? throw new InvalidOperationException("cmd.exe não iniciou.");
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Não foi possível criar a junção de teste.");
        }
    }
}
