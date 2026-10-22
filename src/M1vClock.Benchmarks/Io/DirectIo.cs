namespace M1vClock.Benchmarks.Io;

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
            throw new IOException($"Pouco espaço livre em {root.TrimEnd('\\')}: o teste precisa de {neededBytes >> 20} MB e há {free >> 20} MB.");
        }
    }
}
