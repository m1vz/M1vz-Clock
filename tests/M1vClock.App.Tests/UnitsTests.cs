using System.Globalization;
using M1vClock.App.Formatting;

namespace M1vClock.App.Tests;

public sealed class UnitsTests
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(17_179_869_184, "16 GB")]
    [InlineData(1_293_942.0 * 1024, "1,23 GB")]
    [InlineData(1_099_511_627_776, "1 TB")]
    public void BytesUseBinaryUnitsAndTrimTrailingZeros(double bytes, string expected)
    {
        Assert.Equal(expected, Units.Bytes(bytes, Portuguese));
    }

    [Fact]
    public void BytesFollowTheChosenCulture()
    {
        Assert.Equal("1.5 KB", Units.Bytes(1536, English));
        Assert.Equal("1,5 KB", Units.Bytes(1536, Portuguese));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSizesShowADash(double bytes)
    {
        Assert.Equal(Units.Missing, Units.Bytes(bytes, Portuguese));
    }

    [Fact]
    public void KilobytesScaleToTheRightUnit()
    {
        Assert.Equal("4 MB", Units.Kilobytes(4096, Portuguese));
        Assert.Equal("32 MB", Units.Kilobytes(32768, Portuguese));
    }

    [Theory]
    [InlineData(3401, "3,40 GHz")]
    [InlineData(1000, "1,00 GHz")]
    [InlineData(800, "800 MHz")]
    public void FrequencySwitchesToGigahertzFromAThousand(double megahertz, string expected)
    {
        Assert.Equal(expected, Units.Frequency(megahertz, Portuguese));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void MissingFrequencyShowsADash(double megahertz)
    {
        Assert.Equal(Units.Missing, Units.Frequency(megahertz, Portuguese));
    }

    [Fact]
    public void DatesUseTheCultureShortFormat()
    {
        Assert.Equal("03/01/2026", Units.Date(new DateOnly(2026, 1, 3), Portuguese));
        Assert.Equal(Units.Missing, Units.Date(null, Portuguese));
    }

    [Fact]
    public void YesNoUsesTheLocalizedWords()
    {
        Assert.Equal("Sim", Units.YesNo(true));
        Assert.Equal("Não", Units.YesNo(false));
        Assert.Equal(Units.Missing, Units.YesNo(null));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    [InlineData("   ", "—")]
    [InlineData("  AM4 ", "AM4")]
    public void OrMissingTrimsAndFillsBlanks(string? text, string expected)
    {
        Assert.Equal(expected, Units.OrMissing(text));
    }
}
