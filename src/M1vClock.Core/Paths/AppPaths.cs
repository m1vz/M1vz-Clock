using M1vClock.Core.Security;

namespace M1vClock.Core.Paths;

public sealed class AppPaths
{
    public const string ProductFolderName = "m1vClock";
    public const string DevelopmentFolderName = "m1vClock-dev";

    private readonly List<string> _quarantined = [];

    public AppPaths(string root, bool isHardened)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        IsHardened = isHardened;
    }

    public string Root { get; }

    public bool IsHardened { get; }

    public string Logs => Path.Combine(Root, "logs");

    public string Journal => Path.Combine(Root, "journal");

    public string Backups => Path.Combine(Root, "backups");

    public string Native => Path.Combine(Root, "native");

    public string Downloads => Path.Combine(Root, "downloads");

    public string Temp => Path.Combine(Root, "temp");

    public string State => Path.Combine(Root, "state");

    public string UserDirectory(string userSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        if (userSid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("SID inválido para nome de pasta.", nameof(userSid));
        }

        return Path.Combine(Root, "users", userSid);
    }

    public string EnsureDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var insideRoot = full.Equals(Root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!insideRoot)
        {
            throw new ArgumentException("Pasta fora da raiz do m1vClock.", nameof(path));
        }

        if (IsHardened)
        {
            Track(Root, HardenedDirectory.Ensure(Root));
            var relative = Path.GetRelativePath(Root, full);
            var current = Root;
            foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".")
                {
                    continue;
                }

                current = Path.Combine(current, part);
                Track(current, HardenedDirectory.EnsureChild(current));
            }
        }
        else
        {
            Directory.CreateDirectory(full);
        }

        return full;
    }

    public IReadOnlyList<string> QuarantinedDirectories
    {
        get
        {
            lock (_quarantined)
            {
                return [.. _quarantined];
            }
        }
    }

    private void Track(string path, HardenedDirectoryStatus status)
    {
        if (status != HardenedDirectoryStatus.Quarantined)
        {
            return;
        }

        lock (_quarantined)
        {
            _quarantined.Add(path);
        }
    }

    public static AppPaths ForProcess(ElevationInfo elevation)
    {
        ArgumentNullException.ThrowIfNull(elevation);
        if (elevation.IsElevated)
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return new AppPaths(Path.Combine(programData, ProductFolderName), isHardened: true);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(Path.Combine(localAppData, DevelopmentFolderName), isHardened: false);
    }
}
