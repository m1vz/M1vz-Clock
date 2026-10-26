using M1vClock.Tuning.Gpu;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Tuning.Tests;

public sealed class RealAdlxTests
{
    [Fact]
    public void TheRadeonOfThisPcIsReadThroughAdlxWithoutChangingAnything()
    {
        Assert.SkipUnless(File.Exists(Path.Combine(Environment.SystemDirectory, AdlxReader.LibraryName)), "sem driver AMD");

        var reading = new AdlxReader(NullLogger<AdlxReader>.Instance).Read();

        Assert.Equal(GpuReadStatus.Read, reading.Status);
        var gpu = Assert.Single(reading.Gpus);
        Assert.Equal("AMD Radeon RX 5500 XT", gpu.Name);
        Assert.Equal(GpuVendor.Amd, gpu.Vendor);
        TestContext.Current.SendDiagnosticMessage($"ADLX: {gpu}");
        if (gpu.ClockRange is { } clocks)
        {
            Assert.InRange(clocks.Minimum, 100, 3000);
            Assert.InRange(clocks.Maximum, clocks.Minimum, 4000);
        }

        if (gpu.PowerRange is { } power)
        {
            Assert.InRange(power.Minimum, -100, 0);
            Assert.InRange(power.Maximum, 0, 100);
            Assert.True(gpu.PowerCurrent is null || power.Contains(gpu.PowerCurrent.Value));
        }
    }

    [Fact]
    public void ReadingTwiceGivesTheSameAnswer()
    {
        Assert.SkipUnless(File.Exists(Path.Combine(Environment.SystemDirectory, AdlxReader.LibraryName)), "sem driver AMD");
        var reader = new AdlxReader(NullLogger<AdlxReader>.Instance);

        Assert.Equal(reader.Read().Gpus, reader.Read().Gpus);
    }
}
