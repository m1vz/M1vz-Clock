namespace M1vClock.Benchmarks.Io;

public sealed class InsufficientDiskSpaceException : IOException
{
    public InsufficientDiskSpaceException()
    {
    }

    public InsufficientDiskSpaceException(string message)
        : base(message)
    {
    }

    public InsufficientDiskSpaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InsufficientDiskSpaceException(string drive, long neededBytes, long freeBytes)
        : base($"Free space on {drive} is {freeBytes >> 20} MB and the test needs {neededBytes >> 20} MB.")
    {
        Drive = drive;
        NeededBytes = neededBytes;
        FreeBytes = freeBytes;
    }

    public string Drive { get; } = string.Empty;

    public long NeededBytes { get; }

    public long FreeBytes { get; }
}

public static class DirectIo
{
    public const FileOptions NoBuffering = (FileOptions)0x20000000;

    public const long MinimumFreeMargin = 512L << 20;

    public static string DriveOf(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return (Path.GetPathRoot(Path.GetFullPath(directory)) ?? string.Empty).TrimEnd('\\');
    }

    public static void RequireFreeSpace(string directory, long neededBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory)) ?? throw new IOException("Unidade não identificada.");
        var free = new DriveInfo(root).AvailableFreeSpace;
        if (free < neededBytes)
        {
            throw new InsufficientDiskSpaceException(root.TrimEnd('\\'), neededBytes, free);
        }
    }
}
