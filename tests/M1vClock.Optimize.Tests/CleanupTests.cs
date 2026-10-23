using System.Diagnostics;
using M1vClock.Optimize.Cleanup;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Optimize.Tests;

internal sealed class CleanupArena : IDisposable
{
    public CleanupArena()
    {
        Base = Path.Combine(Path.GetTempPath(), "m1vclock-cleanup-tests", Guid.NewGuid().ToString("N"));
        Root = Path.Combine(Base, "work", "alvo");
        Directory.CreateDirectory(Root);
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
    }

    public string Base { get; }

    public string Root { get; }

    public FakeTimeProvider Clock { get; }

    public CleanupCategory Category(TimeSpan? age = null, params string[] extraRoots) =>
        new("teste", [Root, .. extraRoots], false, age ?? TimeSpan.FromDays(1), false);

    public CleanupService Service(CleanupCategory category) => new([category], Clock, NullLogger<CleanupService>.Instance);

    public string Write(string relative, int bytes, TimeSpan age)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, Clock.GetUtcNow().UtcDateTime - age);
        return path;
    }

    public void Dispose()
    {
        foreach (var entry in new DirectoryInfo(Base).EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if (entry.Exists && entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                entry.Delete();
            }
        }

        Directory.Delete(Base, recursive: true);
    }
}

internal static class TestJunction
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

public sealed class CleanupPolicyTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Users\joaop")]
    [InlineData(@"C:\Users\joaop\Documents")]
    [InlineData(@"C:\Users\joaop\Downloads")]
    [InlineData(@"C:\Users\joaop\Desktop")]
    [InlineData(@"C:\Users\joaop\OneDrive")]
    [InlineData(@"C:\Users\joaop\Pictures")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Windows\WinSxS")]
    [InlineData("")]
    public void AFolderThatIsTooWideOrPersonalOrPartOfTheSystemIsNeverARoot(string root)
    {
        Assert.False(CleanupPolicy.IsAcceptableRoot(root, out var reason));
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData(@"C:\Users\joaop\AppData\Local\Temp")]
    [InlineData(@"C:\Windows\Temp")]
    [InlineData(@"C:\Windows\SoftwareDistribution\Download")]
    [InlineData(@"C:\Users\joaop\AppData\Local\CrashDumps")]
    [InlineData(@"C:\ProgramData\Microsoft\Windows\WER\ReportArchive")]
    public void TheFoldersTheCleanupUsesAreAcceptable(string root)
    {
        Assert.True(CleanupPolicy.IsAcceptableRoot(root, out var reason), reason);
    }

    [Fact]
    public void ASiblingWithTheSamePrefixAndADotDotPathAreNotInsideTheRoot()
    {
        Assert.True(CleanupPolicy.IsInside(@"C:\a\b\alvo", @"C:\a\b\alvo\x\y.tmp"));
        Assert.False(CleanupPolicy.IsInside(@"C:\a\b\alvo", @"C:\a\b\alvo2\x.tmp"));
        Assert.False(CleanupPolicy.IsInside(@"C:\a\b\alvo", @"C:\a\b\alvo\..\outro\x.tmp"));
        Assert.False(CleanupPolicy.IsInside(@"C:\a\b\alvo", @"C:\a\b\alvo"));
        Assert.True(CleanupPolicy.IsInside(@"C:\A\B\ALVO", @"c:\a\b\alvo\X.TMP"));
    }

    [Fact]
    public void EveryRootOfTheRealCatalogIsAcceptableAndLivesUnderTheFourKnownPlaces()
    {
        var paths = CleanupPaths.Current;
        var catalog = CleanupCatalog.Create(paths);

        Assert.Equal(CleanupCatalog.Ids, catalog.Select(c => c.Id));
        Assert.Equal(catalog.Count, catalog.Select(c => c.Id).Distinct().Count());
        foreach (var root in catalog.SelectMany(c => c.Roots))
        {
            Assert.True(CleanupPolicy.IsAcceptableRoot(root, out var reason), $"{root}: {reason}");
            Assert.True(
                CleanupPolicy.IsInside(paths.Temp, root) || root.Equals(paths.Temp, StringComparison.OrdinalIgnoreCase)
                || CleanupPolicy.IsInside(paths.WindowsDirectory, root)
                || CleanupPolicy.IsInside(paths.LocalAppData, root)
                || CleanupPolicy.IsInside(paths.ProgramData, root),
                root);
        }
    }

    [Fact]
    public void OnlyTheWindowsFoldersAndTheErrorReportsNeedAdministrator()
    {
        var catalog = CleanupCatalog.Create(CleanupPaths.Current).ToDictionary(c => c.Id);

        Assert.False(catalog[CleanupCatalog.UserTemp].NeedsAdministrator);
        Assert.False(catalog[CleanupCatalog.CrashDumps].NeedsAdministrator);
        Assert.False(catalog[CleanupCatalog.ShaderCache].NeedsAdministrator);
        Assert.True(catalog[CleanupCatalog.WindowsTemp].NeedsAdministrator);
        Assert.True(catalog[CleanupCatalog.UpdateDownloads].NeedsAdministrator);
        Assert.True(catalog[CleanupCatalog.ErrorReports].NeedsAdministrator);
        Assert.True(catalog[CleanupCatalog.ShaderCache].RebuildsByItself);
        Assert.True(catalog[CleanupCatalog.UpdateDownloads].RebuildsByItself);
        Assert.False(catalog[CleanupCatalog.UserTemp].RebuildsByItself);
        Assert.Equal(TimeSpan.FromDays(1), catalog[CleanupCatalog.UserTemp].MinimumAge);
    }
}

public sealed class CleanupServiceTests
{
    [Fact]
    public async Task TheScanCountsOnlyTheOldFilesAndTheirSize()
    {
        using var arena = new CleanupArena();
        arena.Write("velho1.tmp", 1000, TimeSpan.FromDays(3));
        arena.Write(@"sub\velho2.tmp", 500, TimeSpan.FromDays(2));
        arena.Write("novo.tmp", 9999, TimeSpan.FromHours(2));

        var summary = await arena.Service(arena.Category()).ScanAsync(TestContext.Current.CancellationToken);

        var scan = Assert.Single(summary.Scans);
        Assert.Equal(2, scan.Files);
        Assert.Equal(1500, scan.Bytes);
        Assert.Equal(1500, summary.Bytes);
        Assert.Equal(2, summary.Files);
    }

    [Fact]
    public async Task ScanningDeletesNothing()
    {
        using var arena = new CleanupArena();
        var old = arena.Write("velho.tmp", 100, TimeSpan.FromDays(5));

        await arena.Service(arena.Category()).ScanAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(old));
    }

    [Fact]
    public async Task CleaningDeletesTheOldFilesKeepsTheNewOnesAndRemovesTheFoldersLeftEmpty()
    {
        using var arena = new CleanupArena();
        var old = arena.Write("velho.tmp", 1000, TimeSpan.FromDays(3));
        var oldInEmptyFolder = arena.Write(@"vazia\a.tmp", 200, TimeSpan.FromDays(4));
        var fresh = arena.Write("novo.tmp", 50, TimeSpan.FromMinutes(5));
        var freshInFolder = arena.Write(@"cheia\novo.tmp", 60, TimeSpan.FromMinutes(5));
        var oldInFullFolder = arena.Write(@"cheia\velho.tmp", 70, TimeSpan.FromDays(9));
        Directory.SetLastWriteTimeUtc(Path.Combine(arena.Root, "vazia"), arena.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(4));

        var outcome = await arena.Service(arena.Category()).CleanAsync("teste", TestContext.Current.CancellationToken);

        Assert.Equal(3, outcome.Deleted);
        Assert.Equal(1270, outcome.FreedBytes);
        Assert.Equal(0, outcome.Failed);
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(oldInEmptyFolder));
        Assert.False(File.Exists(oldInFullFolder));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(freshInFolder));
        Assert.False(Directory.Exists(Path.Combine(arena.Root, "vazia")));
        Assert.True(Directory.Exists(Path.Combine(arena.Root, "cheia")));
        Assert.True(Directory.Exists(arena.Root));
    }

    [Fact]
    public async Task ACategoryWithNoMinimumAgeTakesTheNewFilesToo()
    {
        using var arena = new CleanupArena();
        var fresh = arena.Write("dump.dmp", 10, TimeSpan.Zero);

        var outcome = await arena.Service(arena.Category(TimeSpan.Zero)).CleanAsync("teste", TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Deleted);
        Assert.False(File.Exists(fresh));
    }

    [Fact]
    public async Task AFileThatAnotherProgramHoldsStaysAndIsCountedWhileTheRestGoes()
    {
        using var arena = new CleanupArena();
        var held = arena.Write("preso.tmp", 100, TimeSpan.FromDays(3));
        var free = arena.Write("livre.tmp", 100, TimeSpan.FromDays(3));
        await using var hold = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);

        var outcome = await arena.Service(arena.Category()).CleanAsync("teste", TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(1, outcome.Failed);
        Assert.True(File.Exists(held));
        Assert.False(File.Exists(free));
    }

    [Fact]
    public async Task ARemoteJunctionInsideTheRootIsNeverFollowedAndWhatItPointsToIsUntouched()
    {
        using var arena = new CleanupArena();
        var outside = Path.Combine(arena.Base, "fora-do-alvo");
        Directory.CreateDirectory(outside);
        var precious = Path.Combine(outside, "precioso.docx");
        File.WriteAllBytes(precious, new byte[4000]);
        File.SetLastWriteTimeUtc(precious, arena.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(400));
        var link = Path.Combine(arena.Root, "atalho");
        TestJunction.Create(link, outside);
        var ordinary = arena.Write("velho.tmp", 10, TimeSpan.FromDays(3));
        var service = arena.Service(arena.Category());

        var summary = await service.ScanAsync(TestContext.Current.CancellationToken);
        var outcome = await service.CleanAsync("teste", TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.Files);
        Assert.Equal(1, outcome.Deleted);
        Assert.True(File.Exists(precious));
        Assert.True(Directory.Exists(link));
        Assert.False(File.Exists(ordinary));
    }

    [Fact]
    public async Task ARootThatIsItselfAJunctionIsRefusedAndNothingBehindItIsTouched()
    {
        using var arena = new CleanupArena();
        var real = Path.Combine(arena.Base, "real");
        Directory.CreateDirectory(real);
        var inside = Path.Combine(real, "velho.tmp");
        File.WriteAllBytes(inside, new byte[10]);
        File.SetLastWriteTimeUtc(inside, arena.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(30));
        var link = Path.Combine(arena.Base, "work", "ligacao");
        TestJunction.Create(link, real);
        var service = arena.Service(new CleanupCategory("teste", [link], false, TimeSpan.FromDays(1), false));

        var summary = await service.ScanAsync(TestContext.Current.CancellationToken);
        var outcome = await service.CleanAsync("teste", TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.Files);
        Assert.Equal(0, outcome.Deleted);
        Assert.True(File.Exists(inside));
    }

    [Fact]
    public async Task ATooWideRootInsideACategoryIsSkippedNotCleaned()
    {
        using var arena = new CleanupArena();
        var personal = Path.Combine(arena.Base, "Documents");
        Directory.CreateDirectory(personal);
        var file = Path.Combine(personal, "tese.docx");
        File.WriteAllBytes(file, new byte[10]);
        File.SetLastWriteTimeUtc(file, arena.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(100));
        var service = arena.Service(arena.Category(null, personal));

        var outcome = await service.CleanAsync("teste", TestContext.Current.CancellationToken);

        Assert.Equal(0, outcome.Deleted);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task ARootThatDoesNotExistYetIsSimplyEmpty()
    {
        using var arena = new CleanupArena();
        var service = arena.Service(new CleanupCategory("teste", [Path.Combine(arena.Base, "work", "nao-existe")], false, TimeSpan.FromDays(1), false));

        var summary = await service.ScanAsync(TestContext.Current.CancellationToken);
        var outcome = await service.CleanAsync("teste", TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.Files);
        Assert.Equal((0, 0L, 0), (outcome.Deleted, outcome.FreedBytes, outcome.Failed));
    }

    [Fact]
    public async Task AnUnknownCategoryIsRefused()
    {
        using var arena = new CleanupArena();

        await Assert.ThrowsAsync<ArgumentException>(() => arena.Service(arena.Category()).CleanAsync("inexistente", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACancelledCleanStopsInsteadOfFinishing()
    {
        using var arena = new CleanupArena();
        var old = arena.Write("velho.tmp", 10, TimeSpan.FromDays(3));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => arena.Service(arena.Category()).CleanAsync("teste", cancelled.Token));

        Assert.True(File.Exists(old));
    }
}

public sealed class RealMachineCleanupTests
{
    [Fact]
    public async Task OnlyTheScanRunsAgainstThisPcAndItNeverDeletesAnything()
    {
        var service = new CleanupService(CleanupCatalog.Create(CleanupPaths.Current), TimeProvider.System, NullLogger<CleanupService>.Instance);

        var summary = await service.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CleanupCatalog.Ids, summary.Scans.Select(s => s.Category.Id));
        Assert.All(summary.Scans, s =>
        {
            Assert.True(s.Files >= 0 && s.Bytes >= 0 && s.Unreadable >= 0);
            Assert.Equal(s.Files == 0, s.Bytes == 0 || s.Files == 0);
        });
        Assert.Equal(summary.Scans.Sum(s => s.Bytes), summary.Bytes);
    }
}
