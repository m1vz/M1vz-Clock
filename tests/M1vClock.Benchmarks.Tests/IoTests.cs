using M1vClock.Benchmarks.Io;
using M1vClock.Core.Security;

namespace M1vClock.Benchmarks.Tests;

public sealed class AlignedBufferTests
{
    [Fact]
    public void TheBufferStartsOnASectorBoundaryAndIsZeroed()
    {
        using var buffer = new AlignedBuffer(8192);

        Assert.Equal(0, buffer.Address % AlignedBuffer.SectorAlignment);
        Assert.Equal(8192, buffer.GetSpan().Length);
        Assert.True(buffer.GetSpan().IndexOfAnyExcept((byte)0) < 0);
    }

    [Fact]
    public void ACustomAlignmentIsHonored()
    {
        using var buffer = new AlignedBuffer(1024, 64);

        Assert.Equal(0, buffer.Address % 64);
        Assert.Equal(64, buffer.Alignment);
    }

    [Fact]
    public void TheMemoryViewSharesTheSameBytes()
    {
        using var buffer = new AlignedBuffer(16);

        buffer.GetSpan()[3] = 42;

        Assert.Equal(42, buffer.Memory.Span[3]);
    }

    [Fact]
    public void ReadingAfterDisposeIsRefusedInsteadOfTouchingFreedMemory()
    {
        var buffer = new AlignedBuffer(16);
        buffer.Dispose();
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.GetSpan().Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnEmptyBufferIsRejected(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlignedBuffer(length));
    }
}

public sealed class BenchTempAreaTests : IDisposable
{
    private readonly ScratchDirectory _parent = new();
    private readonly CollectingLogger<BenchTempArea> _logger = new();

    public void Dispose() => _parent.Dispose();

    [Fact]
    public void EveryAreaGetsItsOwnUnpredictableName()
    {
        using var first = BenchTempArea.Create(_parent.Path, hardened: false, _logger);
        using var second = BenchTempArea.Create(_parent.Path, hardened: false, _logger);

        Assert.NotEqual(first.Path, second.Path);
        Assert.StartsWith(BenchTempArea.Prefix, Path.GetFileName(first.Path), StringComparison.Ordinal);
        Assert.Equal(BenchTempArea.Prefix.Length + 16, Path.GetFileName(first.Path).Length);
        Assert.Equal(_parent.Path, Path.GetDirectoryName(first.Path));
        Assert.True(Directory.Exists(first.Path));
    }

    [Fact]
    public void ADeletedFileIsGoneTheMomentItsHandleCloses()
    {
        using var area = BenchTempArea.Create(_parent.Path, hardened: false, _logger);
        var path = area.PathOf("data.bin");

        using (var handle = area.OpenNew("data.bin", FileAccess.ReadWrite, FileShare.None, FileOptions.None))
        {
            RandomAccess.Write(handle, new byte[] { 1, 2, 3 }, 0);
            Assert.True(File.Exists(path));
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DisposingTheAreaRemovesTheFolder()
    {
        var area = BenchTempArea.Create(_parent.Path, hardened: false, _logger);
        var path = area.Path;
        File.WriteAllText(area.PathOf("left.dat"), "x");

        area.Dispose();
        area.Dispose();

        Assert.False(Directory.Exists(path));
        Assert.Empty(_parent.BenchFolders());
    }

    [Fact]
    public async Task AFileThatAnotherProgramHoldsForAMomentDoesNotLeaveTheFolderBehind()
    {
        var area = BenchTempArea.Create(_parent.Path, hardened: false, _logger);
        var path = area.Path;
        var held = new FileStream(area.PathOf("held.dat"), FileMode.Create, FileAccess.Write, FileShare.None);
        var release = Task.Run(
            async () =>
            {
                await Task.Delay(120, TestContext.Current.CancellationToken);
                await held.DisposeAsync();
            },
            TestContext.Current.CancellationToken);

        area.Dispose();
        await release;

        Assert.False(Directory.Exists(path));
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 4006);
        Assert.DoesNotContain(_logger.Entries, e => e.EventId.Id == 4002);
    }

    [Fact]
    public async Task AFileThatStaysHeldMakesTheAreaGiveUpWithAWarningAndLeaveItForTheNextSweep()
    {
        var area = BenchTempArea.Create(_parent.Path, hardened: false, _logger);
        var path = area.Path;
        await using var held = new FileStream(area.PathOf("held.dat"), FileMode.Create, FileAccess.Write, FileShare.None);

        area.Dispose();

        Assert.True(Directory.Exists(path));
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 4002);
        Assert.Equal(5, _logger.Entries.Count(e => e.EventId.Id == 4006));
    }

    [Fact]
    public void ANestedFolderInsideTheAreaIsNeverDeletedBlindly()
    {
        var area = BenchTempArea.Create(_parent.Path, hardened: false, _logger);
        var nested = Path.Combine(area.Path, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "keep.txt"), "x");

        area.Dispose();

        Assert.True(File.Exists(Path.Combine(nested, "keep.txt")));
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 4004);
    }

    [Theory]
    [InlineData("..\\evil.dat")]
    [InlineData("sub\\file.dat")]
    [InlineData("C:\\file.dat")]
    [InlineData("..")]
    [InlineData(".")]
    public void FileNamesWithAPathAreRefused(string name)
    {
        using var area = BenchTempArea.Create(_parent.Path, hardened: false, _logger);

        Assert.Throws<ArgumentException>(() => area.PathOf(name));
    }

    [Fact]
    public void OpeningAFileTwiceWithTheSameNameFailsInsteadOfReusingIt()
    {
        using var area = BenchTempArea.Create(_parent.Path, hardened: false, _logger);
        using var first = area.OpenNew("a.dat", FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, FileOptions.None);

        Assert.Throws<IOException>(() => area.OpenNew("a.dat", FileAccess.ReadWrite, FileShare.None, FileOptions.None));
    }

    [Fact]
    public void ACreateOnAJunctionIsRefused()
    {
        var target = Path.Combine(_parent.Path, "real");
        var link = Path.Combine(_parent.Path, "link");
        Directory.CreateDirectory(target);
        Junction.Create(link, target);

        Assert.Throws<IOException>(() => BenchTempArea.Create(link, hardened: false, _logger));
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Fact]
    public void AMissingParentIsReportedClearly()
    {
        Assert.Throws<DirectoryNotFoundException>(() => BenchTempArea.Create(Path.Combine(_parent.Path, "nao-existe"), hardened: false, _logger));
    }

    [Fact]
    public void LeftoversFromAnEarlierRunAreRemovedAndOtherFoldersAreLeftAlone()
    {
        var leftover = Path.Combine(_parent.Path, BenchTempArea.Prefix + "deadbeef00000000");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "bench.dat"), "x");
        Directory.SetCreationTimeUtc(leftover, DateTime.UtcNow.AddHours(-2));
        var unrelated = Path.Combine(_parent.Path, "meus-jogos");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "save.dat"), "x");

        var removed = BenchTempArea.SweepLeftovers(_parent.Path, _logger);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(leftover));
        Assert.True(File.Exists(Path.Combine(unrelated, "save.dat")));
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 4003);
    }

    [Fact]
    public void AFolderCreatedJustNowIsNotTreatedAsALeftover()
    {
        using var area = BenchTempArea.Create(_parent.Path, hardened: false, _logger);

        Assert.Equal(0, BenchTempArea.SweepLeftovers(_parent.Path, _logger));
        Assert.True(Directory.Exists(area.Path));
    }

    [Fact]
    public void ALeftoverThatIsAJunctionIsNeverFollowed()
    {
        var target = Path.Combine(_parent.Path, "precious");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "important.txt"), "x");
        var link = Path.Combine(_parent.Path, BenchTempArea.Prefix + "aaaaaaaaaaaaaaaa");
        Junction.Create(link, target);

        var removed = BenchTempArea.SweepLeftovers(_parent.Path, _logger, TimeSpan.Zero);

        Assert.Equal(0, removed);
        Assert.True(File.Exists(Path.Combine(target, "important.txt")));
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 4004);
        Directory.Delete(link);
    }

    [Fact]
    public void ALeftoverHoldingAJunctionInsideIsLeftAloneToo()
    {
        var target = Path.Combine(_parent.Path, "precious");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "important.txt"), "x");
        var leftover = Path.Combine(_parent.Path, BenchTempArea.Prefix + "bbbbbbbbbbbbbbbb");
        Directory.CreateDirectory(leftover);
        Junction.Create(Path.Combine(leftover, "trap"), target);

        var removed = BenchTempArea.SweepLeftovers(_parent.Path, _logger, TimeSpan.Zero);

        Assert.Equal(0, removed);
        Assert.True(File.Exists(Path.Combine(target, "important.txt")));
        Directory.Delete(Path.Combine(leftover, "trap"));
    }

    [Fact]
    public void SweepingAMissingFolderIsHarmless()
    {
        Assert.Equal(0, BenchTempArea.SweepLeftovers(Path.Combine(_parent.Path, "nao-existe"), _logger));
    }

    [Fact]
    public void AHardenedAreaIsOwnedByAdministratorsAndNotWritableByUsers()
    {
        Assert.SkipUnless(ElevationInfo.Current().IsElevated, "Precisa de administrador.");

        using var area = BenchTempArea.Create(_parent.Path, hardened: true, _logger);

        Assert.True(HardenedDirectory.IsTrusted(new DirectoryInfo(area.Path)));
    }
}
