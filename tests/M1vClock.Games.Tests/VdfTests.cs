using M1vClock.Games.Library;

namespace M1vClock.Games.Tests;

public sealed class VdfTests
{
    [Fact]
    public void TheRealLibraryListOfThisPcIsParsedIntoTwoLibrariesWithTheirApps()
    {
        var root = Vdf.Parse(Real.LibraryFolders);

        Assert.Equal("libraryfolders", root.Name);
        Assert.Equal(["0", "1"], root.Children.Select(c => c.Name));
        Assert.Equal(@"C:\Program Files (x86)\Steam", root.Child("0")!.Text("path"));
        Assert.Equal(@"D:\SteamLibrary", root.Child("1")!.Text("path"));
        Assert.Equal(6, root.Child("0")!.Child("apps")!.Children.Count);
        Assert.Equal(9, root.Child("1")!.Child("apps")!.Children.Count);
        Assert.Equal("73922650178", root.Child("0")!.Child("apps")!.Text("730"));
    }

    [Fact]
    public void ARealManifestKeepsItsNestedObjects()
    {
        var state = Vdf.Parse(Real.Cs2Manifest);

        Assert.Equal("AppState", state.Name);
        Assert.Equal("730", state.Text("appid"));
        Assert.Equal("Counter-Strike 2", state.Text("name"));
        Assert.Equal(2, state.Child("InstalledDepots")!.Children.Count);
        Assert.Equal("65027793761", state.Child("InstalledDepots")!.Child("2347770")!.Text("size"));
        Assert.Equal("brazilian", state.Child("UserConfig")!.Text("language"));
        Assert.True(state.Child("UserConfig")!.IsObject);
        Assert.False(state.Child("appid")!.IsObject);
    }

    [Fact]
    public void LookupIgnoresCaseAndAMissingNameGivesNull()
    {
        var state = Vdf.Parse(Real.Cs2Manifest);

        Assert.Equal("730", state.Text("APPID"));
        Assert.Null(state.Text("nao-existe"));
        Assert.Null(state.Child("nao-existe"));
    }

    [Fact]
    public void CommentsBlankLinesAndAByteOrderMarkAreIgnored()
    {
        var root = Vdf.Parse("\uFEFF// cabeçalho\n\"a\"\n{\n  // outro\n  \"b\"  \"c\"\n}\n");

        Assert.Equal("a", root.Name);
        Assert.Equal("c", root.Text("b"));
    }

    [Fact]
    public void EscapesAreDecodedAndBareWordsAreAccepted()
    {
        var root = Vdf.Parse("\"a\" { \"p\" \"C:\\\\x\\\\y\" \"q\" \"linha\\noutra\" \"r\" \"diz \\\"oi\\\"\" bare value }");

        Assert.Equal(@"C:\x\y", root.Text("p"));
        Assert.Equal("linha\noutra", root.Text("q"));
        Assert.Equal("diz \"oi\"", root.Text("r"));
        Assert.Equal("value", root.Text("bare"));
    }

    [Fact]
    public void SeveralTopLevelEntriesComeBackUnderAnUnnamedRoot()
    {
        var root = Vdf.Parse("\"a\" \"1\" \"b\" \"2\"");

        Assert.Equal(string.Empty, root.Name);
        Assert.Equal(["a", "b"], root.Children.Select(c => c.Name));
    }

    [Fact]
    public void EmptyTextGivesAnEmptyRoot()
    {
        var root = Vdf.Parse("   \n  ");

        Assert.Empty(root.Children);
    }

    [Theory]
    [InlineData("\"a\" { \"b\" \"c\"")]
    [InlineData("\"a\" { \"b\" \"c")]
    [InlineData("\"a\" { \"b\" }")]
    [InlineData("\"a\" }")]
    [InlineData("{ \"a\" \"b\" }")]
    [InlineData("\"a\"")]
    public void BrokenFilesAreRejectedWithAFormatError(string text)
    {
        Assert.Throws<FormatException>(() => Vdf.Parse(text));
    }

    [Fact]
    public void AbsurdlyDeepNestingIsRejectedInsteadOfOverflowingTheStack()
    {
        var text = string.Concat(Enumerable.Repeat("\"a\" {", 200)) + string.Concat(Enumerable.Repeat("}", 200));

        Assert.Throws<FormatException>(() => Vdf.Parse(text));
    }

    [Fact]
    public void ATruncatedRealManifestIsRejected()
    {
        Assert.Throws<FormatException>(() => Vdf.Parse(Real.Cs2Manifest[..(Real.Cs2Manifest.Length / 2)]));
    }
}
