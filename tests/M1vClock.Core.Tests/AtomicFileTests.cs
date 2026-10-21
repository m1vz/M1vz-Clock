using M1vClock.Core.IO;

namespace M1vClock.Core.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void WritesNewFileWithoutLeavingTemporaryFiles()
    {
        using var temp = new TempDirectory();
        var path = temp.File("dados.json");

        AtomicFile.WriteAllText(path, "{\"a\":1}");

        Assert.Equal("{\"a\":1}", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void ReplacesExistingFileAndKeepsPreviousVersionAsBackup()
    {
        using var temp = new TempDirectory();
        var path = temp.File("dados.json");
        var backup = temp.File("dados.json.bak");
        AtomicFile.WriteAllText(path, "primeira");

        AtomicFile.WriteAllText(path, "segunda", backup);

        Assert.Equal("segunda", File.ReadAllText(path));
        Assert.Equal("primeira", File.ReadAllText(backup));
        Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
    }

    [Fact]
    public void WritesUtf8WithoutByteOrderMark()
    {
        using var temp = new TempDirectory();
        var path = temp.File("acentos.txt");

        AtomicFile.WriteAllText(path, "ação");

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("ação", File.ReadAllText(path));
    }
}
