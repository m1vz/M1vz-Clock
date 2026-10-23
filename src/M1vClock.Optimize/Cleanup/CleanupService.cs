using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Cleanup;

public sealed class CleanupService : ICleanupService
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        ReturnSpecialDirectories = false,
    };

    private readonly TimeProvider _clock;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(IReadOnlyList<CleanupCategory> categories, TimeProvider clock, ILogger<CleanupService> logger)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        Categories = categories;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<CleanupCategory> Categories { get; }

    public Task<CleanupSummary> ScanAsync(CancellationToken cancellationToken) =>
        Task.Run(() => new CleanupSummary(Categories.Select(c => Scan(c, cancellationToken)).ToList()), cancellationToken);

    public Task<CleanupOutcome> CleanAsync(string categoryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        var category = Categories.FirstOrDefault(c => string.Equals(c.Id, categoryId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Categoria de limpeza desconhecida: {categoryId}.", nameof(categoryId));
        return Task.Run(() => Clean(category, cancellationToken), cancellationToken);
    }

    private CleanupScan Scan(CleanupCategory category, CancellationToken cancellationToken)
    {
        var files = 0;
        var bytes = 0L;
        var unreadable = 0;
        foreach (var root in category.Roots)
        {
            foreach (var file in Candidates(category, root, cancellationToken))
            {
                try
                {
                    bytes += file.Length;
                    files++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CleanupLog.FileUnreadable(_logger, ex, file.FullName);
                    unreadable++;
                }
            }
        }

        return new CleanupScan(category, files, bytes, unreadable);
    }

    private CleanupOutcome Clean(CleanupCategory category, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var freed = 0L;
        var failed = 0;
        foreach (var root in category.Roots)
        {
            foreach (var file in Candidates(category, root, cancellationToken).ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!CleanupPolicy.IsInside(root, file.FullName) || File.GetAttributes(file.FullName).HasFlag(FileAttributes.ReparsePoint))
                    {
                        failed++;
                        continue;
                    }

                    var length = file.Length;
                    file.Delete();
                    deleted++;
                    freed += length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CleanupLog.FileKept(_logger, ex, file.FullName);
                    failed++;
                }
            }

            RemoveEmptyFolders(category, root, cancellationToken);
        }

        CleanupLog.Cleaned(_logger, category.Id, deleted, freed, failed);
        return new CleanupOutcome(category, deleted, freed, failed);
    }

    private IEnumerable<FileInfo> Candidates(CleanupCategory category, string root, CancellationToken cancellationToken)
    {
        if (!CleanupPolicy.IsAcceptableRoot(root, out var reason))
        {
            CleanupLog.RootRefused(_logger, root, reason);
            yield break;
        }

        var info = new DirectoryInfo(CleanupPolicy.Normalize(root));
        if (!info.Exists)
        {
            yield break;
        }

        var limit = _clock.GetUtcNow().UtcDateTime - category.MinimumAge;
        foreach (var file in info.EnumerateFiles("*", Options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.LastWriteTimeUtc <= limit && CleanupPolicy.IsInside(info.FullName, file.FullName))
            {
                yield return file;
            }
        }
    }

    private void RemoveEmptyFolders(CleanupCategory category, string root, CancellationToken cancellationToken)
    {
        if (!CleanupPolicy.IsAcceptableRoot(root, out _) || !Directory.Exists(root))
        {
            return;
        }

        var limit = _clock.GetUtcNow().UtcDateTime - category.MinimumAge;
        var folders = new DirectoryInfo(CleanupPolicy.Normalize(root)).EnumerateDirectories("*", Options).OrderByDescending(d => d.FullName.Length).ToList();
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (folder.LastWriteTimeUtc <= limit && CleanupPolicy.IsInside(root, folder.FullName) && !folder.Attributes.HasFlag(FileAttributes.ReparsePoint) && !folder.EnumerateFileSystemInfos().Any())
                {
                    folder.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CleanupLog.FileKept(_logger, ex, folder.FullName);
            }
        }
    }
}
