using System.IO;
using M1vClock.Core.Paths;
using M1vClock.Core.Security;

namespace M1vClock.App.Services;

public sealed record BenchWorkPlace(string Parent, bool Hardened, string Drive);

public sealed class BenchmarkWorkLocation
{
    private readonly AppPaths _paths;
    private readonly ElevationInfo _elevation;
    private readonly string _systemRoot;

    public BenchmarkWorkLocation(AppPaths paths, ElevationInfo elevation)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(elevation);
        _paths = paths;
        _elevation = elevation;
        _systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
    }

    public BenchWorkPlace For(string driveRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driveRoot);
        var root = Path.GetPathRoot(Path.GetFullPath(driveRoot)) ?? throw new ArgumentException("Unidade inválida.", nameof(driveRoot));
        var drive = root.TrimEnd('\\');
        if (_elevation.IsElevated)
        {
            return new BenchWorkPlace(root, Hardened: true, drive);
        }

        if (string.Equals(root, _systemRoot, StringComparison.OrdinalIgnoreCase))
        {
            return new BenchWorkPlace(_paths.EnsureDirectory(_paths.Temp), Hardened: false, drive);
        }

        return new BenchWorkPlace(root, Hardened: false, drive);
    }

    public IReadOnlyList<string> SweepParents()
    {
        var parents = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
            {
                parents.Add(drive.RootDirectory.FullName);
            }
        }

        parents.Add(_paths.Temp);
        return parents;
    }
}
