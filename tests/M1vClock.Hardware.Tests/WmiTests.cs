using M1vClock.Hardware.Wmi;

namespace M1vClock.Hardware.Tests;

public sealed class WmiRowTests
{
    [Fact]
    public void NamesAreCaseInsensitive()
    {
        var row = WmiRow.Of(("Caption", "Windows"));

        Assert.Equal("Windows", row.Text("caption"));
        Assert.Equal("Windows", row.Text("CAPTION"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTextIsMissing(string? raw)
    {
        Assert.Null(WmiRow.Of(("Name", raw)).Text("Name"));
        Assert.Null(WmiRow.Of().Text("Name"));
    }

    [Fact]
    public void TextIsTrimmed()
    {
        Assert.Equal("AMD Ryzen 7 5700X 8-Core Processor", WmiRow.Of(("Name", "AMD Ryzen 7 5700X 8-Core Processor             ")).Text("Name"));
    }

    [Fact]
    public void WholeNumbersAcceptEveryBoxedIntegerWmiReturns()
    {
        var row = WmiRow.Of(
            ("a", (byte)7), ("b", (ushort)3401), ("c", 26200u), ("d", -5), ("e", 9_000_000_000L), ("f", 42UL), ("g", "4096"), ("h", "x"));

        Assert.Equal(7, row.Whole("a"));
        Assert.Equal(3401, row.Whole("b"));
        Assert.Equal(26200, row.Whole("c"));
        Assert.Equal(-5, row.Whole("d"));
        Assert.Equal(9_000_000_000L, row.Whole("e"));
        Assert.Equal(42, row.Whole("f"));
        Assert.Equal(4096, row.Whole("g"));
        Assert.Null(row.Whole("h"));
        Assert.Null(row.Whole("missing"));
    }

    [Fact]
    public void NumberFallsBackWhenMissingOrTooLarge()
    {
        var row = WmiRow.Of(("big", 9_000_000_000L), ("ok", 8));

        Assert.Equal(8, row.Number("ok"));
        Assert.Equal(-1, row.Number("big", -1));
        Assert.Equal(0, row.Number("missing"));
        Assert.Equal(5, row.Number("missing", 5));
    }

    [Fact]
    public void SizeReadsUnsignedValuesAndRejectsNegatives()
    {
        var row = WmiRow.Of(("cap", 17_179_869_184UL), ("text", "8589934592"), ("neg", -1), ("pos", 5));

        Assert.Equal(17_179_869_184UL, row.Size("cap"));
        Assert.Equal(8_589_934_592UL, row.Size("text"));
        Assert.Equal(0UL, row.Size("neg"));
        Assert.Equal(5UL, row.Size("pos"));
        Assert.Equal(0UL, row.Size("missing"));
    }

    [Fact]
    public void RealAndBoolHandleTheirBoxedTypes()
    {
        var row = WmiRow.Of(("d", 1.5), ("f", 2.5f), ("i", 3), ("s", "4.25"), ("t", true), ("u", "False"), ("v", "talvez"));

        Assert.Equal(1.5, row.Real("d"));
        Assert.Equal(2.5, row.Real("f"));
        Assert.Equal(3.0, row.Real("i"));
        Assert.Equal(4.25, row.Real("s"));
        Assert.Null(row.Real("missing"));
        Assert.True(row.Bool("t"));
        Assert.False(row.Bool("u"));
        Assert.Null(row.Bool("v"));
        Assert.Null(row.Bool("missing"));
    }

    [Fact]
    public void WmiDatesAreParsedFromTheirDmtfText()
    {
        var row = WmiRow.Of(("d", "20260103210000.000000+000"), ("already", new DateTime(2025, 6, 7, 8, 9, 10)), ("bad", "ontem"), ("short", "2026"));

        Assert.Equal(new DateTime(2026, 1, 3, 21, 0, 0), row.Date("d"));
        Assert.Equal(new DateTime(2025, 6, 7, 8, 9, 10), row.Date("already"));
        Assert.Null(row.Date("bad"));
        Assert.Null(row.Date("short"));
        Assert.Null(row.Date("missing"));
    }
}

public sealed class WmiQueryTests
{
    private static readonly string[] Properties = ["Name", "Manufacturer"];
    private static readonly string[] Evil = ["Name; DROP"];
    private static readonly string[] Empty = [];

    [Fact]
    public void BuildsASimpleSelect()
    {
        var query = new WmiQuery("Win32_Processor", Properties);

        Assert.Equal("SELECT Name, Manufacturer FROM Win32_Processor", query.ToWql());
        Assert.Equal(WmiQuery.DefaultScope, query.Scope);
    }

    [Theory]
    [InlineData("Win32_X WHERE 1=1")]
    [InlineData("Win32_X;")]
    [InlineData("1Win32")]
    [InlineData("Win32'")]
    public void RejectsClassNamesThatAreNotPlainIdentifiers(string className)
    {
        Assert.Throws<ArgumentException>(() => new WmiQuery(className, Properties));
    }

    [Fact]
    public void RejectsPropertyNamesThatAreNotPlainIdentifiers()
    {
        Assert.Throws<ArgumentException>(() => new WmiQuery("Win32_Processor", Evil));
    }

    [Fact]
    public void RequiresAtLeastOneProperty()
    {
        Assert.Throws<ArgumentException>(() => new WmiQuery("Win32_Processor", Empty));
    }
}
