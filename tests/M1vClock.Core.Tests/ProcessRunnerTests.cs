using M1vClock.Core.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Core.Tests;

public sealed class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    [Fact]
    public async Task ArgumentsArePassedWithoutShellInterpretation()
    {
        var request = new ProcessRequest(SystemTools.System32("cmd.exe"), ["/d", "/c", "echo", "um & dois"]);

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Contains("um & dois", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExitCodeIsReported()
    {
        var request = new ProcessRequest(SystemTools.System32("cmd.exe"), ["/d", "/c", "exit 7"]);

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ProcessIsKilledWhenTimeoutExpires()
    {
        var request = new ProcessRequest(SystemTools.System32("ping.exe"), ["-n", "30", "127.0.0.1"])
        {
            Timeout = TimeSpan.FromMilliseconds(500),
        };

        var result = await _runner.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.TimedOut);
        Assert.True(result.Duration < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RelativeExecutableIsRejected()
    {
        var request = new ProcessRequest("cmd.exe", ["/c", "echo"]);

        await Assert.ThrowsAsync<ArgumentException>(() => _runner.RunAsync(request, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(@"..\cmd.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData("sub/cmd.exe")]
    public void System32RejectsAnythingButAPlainFileName(string name)
    {
        Assert.Throws<ArgumentException>(() => SystemTools.System32(name));
    }

    [Fact]
    public void DescribeQuotesArgumentsWithSpaces()
    {
        var request = new ProcessRequest(@"C:\Windows\System32\pnputil.exe", ["/export-driver", "*", @"C:\Backup de drivers"]);

        Assert.Equal(@"C:\Windows\System32\pnputil.exe /export-driver * ""C:\Backup de drivers""", ProcessRunner.Describe(request));
    }
}
