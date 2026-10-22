using System.Security.Cryptography;
using M1vClock.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace M1vClock.Benchmarks.Io;

public sealed class BenchTempArea : IDisposable
{
    public const string Prefix = "m1vClock-bench-";

    private static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromMinutes(1);

    private readonly ILogger _logger;
    private bool _disposed;

    private BenchTempArea(string path, ILogger logger)
    {
        Path = path;
        _logger = logger;
    }

    public string Path { get; }

    public static BenchTempArea Create(string parentDirectory, bool hardened, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        var parent = System.IO.Path.GetFullPath(parentDirectory);
        var parentInfo = new DirectoryInfo(parent);
        if (!parentInfo.Exists)
        {
            throw new DirectoryNotFoundException($"A pasta {parent} não existe.");
        }

        if (parentInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"A pasta {parent} é um link ou junção e não pode receber arquivos de teste.");
        }

        var name = Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var full = System.IO.Path.Combine(parent, name);
        if (Directory.Exists(full) || File.Exists(full))
        {
            throw new IOException($"{full} já existe.");
        }

        if (hardened)
        {
            HardenedDirectory.EnsureChild(full);
        }
        else
        {
            Directory.CreateDirectory(full);
        }

        BenchmarkLog.TempAreaCreated(logger, full);
        return new BenchTempArea(full, logger);
    }

    public string PathOf(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || fileName is "." or "..")
        {
            throw new ArgumentException("O nome do arquivo de teste não pode ter caminho.", nameof(fileName));
        }

        return System.IO.Path.Combine(Path, fileName);
    }

    public SafeFileHandle OpenNew(string fileName, FileAccess access, FileShare share, FileOptions options, long preallocationSize = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = PathOf(fileName);
        return File.OpenHandle(path, FileMode.CreateNew, access, share, options | FileOptions.DeleteOnClose, preallocationSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Remove(Path, _logger);
    }

    public static int SweepLeftovers(string parentDirectory, ILogger logger, TimeSpan? minimumAge = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        var age = minimumAge ?? DefaultMinimumAge;
        var removed = 0;
        try
        {
            if (!Directory.Exists(parentDirectory))
            {
                return 0;
            }

            foreach (var directory in Directory.EnumerateDirectories(parentDirectory, Prefix + "*", SearchOption.TopDirectoryOnly))
            {
                var info = new DirectoryInfo(directory);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    BenchmarkLog.LeftoverSkipped(logger, directory);
                    continue;
                }

                if (DateTime.UtcNow - info.CreationTimeUtc < age)
                {
                    continue;
                }

                if (Remove(directory, logger))
                {
                    removed++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BenchmarkLog.SweepFailed(logger, ex, parentDirectory);
        }

        if (removed > 0)
        {
            BenchmarkLog.LeftoversRemoved(logger, removed, parentDirectory);
        }

        return removed;
    }

    private static bool Remove(string directory, ILogger logger)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return true;
            }

            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    BenchmarkLog.LeftoverSkipped(logger, entry.FullName);
                    return false;
                }

                if (entry is DirectoryInfo)
                {
                    BenchmarkLog.LeftoverSkipped(logger, entry.FullName);
                    return false;
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                File.Delete(file);
            }

            Directory.Delete(directory, recursive: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BenchmarkLog.TempDeleteFailed(logger, ex, directory);
            return false;
        }
    }
}
