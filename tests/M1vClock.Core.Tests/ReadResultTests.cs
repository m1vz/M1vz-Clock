using M1vClock.Core.Privacy;
using M1vClock.Core.Results;

namespace M1vClock.Core.Tests;

public sealed class ReadResultTests
{
    [Fact]
    public void SuccessCarriesTheValue()
    {
        var result = ReadResult.Success("ok");

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
        Assert.Null(result.Error);
        Assert.True(result.TryGet(out var value));
        Assert.Equal("ok", value);
    }

    [Fact]
    public void FailureCarriesTheMessageAndNoValue()
    {
        var result = ReadResult.Failure<string>("WMI indisponível.");

        Assert.False(result.IsSuccess);
        Assert.Equal("WMI indisponível.", result.Error);
        Assert.False(result.TryGet(out var value));
        Assert.Null(value);
    }

    [Fact]
    public void MapTransformsOnlySuccesses()
    {
        Assert.Equal(4, ReadResult.Success("abcd").Map(s => s.Length).Value);

        var failed = ReadResult.Failure<string>("erro").Map(s => s.Length);

        Assert.False(failed.IsSuccess);
        Assert.Equal("erro", failed.Error);
    }

    [Fact]
    public void FailureNeedsAMessageAndSuccessNeedsAValue()
    {
        Assert.Throws<ArgumentException>(() => ReadResult.Failure<string>(" "));
        Assert.Throws<ArgumentNullException>(() => ReadResult.Success<string>(null!));
    }
}

public sealed class SensitiveTextTests
{
    [Fact]
    public void HiddenByDefaultAndRevealedOnRequest()
    {
        var serial = new SensitiveText("  ABC123  ");

        Assert.Equal(SensitiveText.Mask, serial.Display(reveal: false));
        Assert.Equal("ABC123", serial.Display(reveal: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingValueShowsADash(string? value)
    {
        var serial = new SensitiveText(value);

        Assert.False(serial.HasValue);
        Assert.Equal("—", serial.Display(reveal: true));
        Assert.Equal("—", serial.Display(reveal: false));
    }

    [Fact]
    public void ToStringNeverLeaksTheValue()
    {
        var serial = new SensitiveText("ABC123");

        Assert.DoesNotContain("ABC123", serial.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ABC123", $"{serial}", StringComparison.Ordinal);
        Assert.Equal(string.Empty, new SensitiveText(null).ToString());
    }
}
