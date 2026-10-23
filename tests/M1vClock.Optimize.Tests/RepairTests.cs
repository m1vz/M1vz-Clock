using M1vClock.Core.Processes;
using M1vClock.Core.Security;
using M1vClock.Optimize.Repair;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Optimize.Tests;

public sealed class RepairServiceTests
{
    private sealed class StubDism(DismOutcome outcome) : IDismSource
    {
        public List<bool> Scans { get; } = [];

        public DismOutcome CheckHealth(bool scan, IProgress<int> progress, CancellationToken cancellationToken)
        {
            Scans.Add(scan);
            progress.Report(40);
            cancellationToken.ThrowIfCancellationRequested();
            return outcome;
        }
    }

    private sealed class StubSfc(SfcOutcome outcome) : ISfcSource
    {
        public int Runs { get; private set; }

        public Task<SfcOutcome> VerifyAsync(IProgress<int> progress, CancellationToken cancellationToken)
        {
            Runs++;
            progress.Report(70);
            return Task.FromResult(outcome);
        }
    }

    private sealed class Collector : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value) => Values.Add(value);
    }

    private static RepairService Create(DismOutcome? dism = null, SfcOutcome? sfc = null, IDismSource? dismStub = null, ISfcSource? sfcStub = null) => new(
        dismStub ?? new StubDism(dism ?? new DismOutcome(DismStatus.Done, DismHealth.Healthy, 0)),
        sfcStub ?? new StubSfc(sfc ?? new SfcOutcome(SfcStatus.Done, 0, false, false, null)),
        new FakeTimeProvider(),
        NullLogger<RepairService>.Instance);

    [Theory]
    [InlineData(DismHealth.Healthy, RepairVerdict.Healthy)]
    [InlineData(DismHealth.Repairable, RepairVerdict.Repairable)]
    [InlineData(DismHealth.NotRepairable, RepairVerdict.NotRepairable)]
    public async Task TheComponentStoreHealthBecomesTheVerdict(DismHealth health, RepairVerdict expected)
    {
        var service = Create(dism: new DismOutcome(DismStatus.Done, health, 0));

        var result = await service.RunAsync(RepairCheck.ComponentsQuick, new Collector(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Verdict);
        Assert.Equal(RepairCheck.ComponentsQuick, result.Check);
        Assert.Equal(expected == RepairVerdict.Healthy, result.IsClean);
        Assert.Equal(expected != RepairVerdict.Healthy, result.NeedsAction);
    }

    [Fact]
    public async Task TheQuickCheckDoesNotScanAndTheFullCheckDoes()
    {
        var dism = new StubDism(new DismOutcome(DismStatus.Done, DismHealth.Healthy, 0));
        var service = Create(dismStub: dism);

        await service.RunAsync(RepairCheck.ComponentsQuick, new Collector(), TestContext.Current.CancellationToken);
        await service.RunAsync(RepairCheck.ComponentsFull, new Collector(), TestContext.Current.CancellationToken);

        Assert.Equal([false, true], dism.Scans);
    }

    [Fact]
    public async Task ProgressFromTheSourceReachesTheCaller()
    {
        var collector = new Collector();

        await Create().RunAsync(RepairCheck.ComponentsFull, collector, TestContext.Current.CancellationToken);
        await Create().RunAsync(RepairCheck.SystemFiles, collector, TestContext.Current.CancellationToken);

        Assert.Equal([40, 70], collector.Values);
    }

    [Theory]
    [InlineData(DismStatus.NeedsAdministrator, RepairVerdict.NeedsAdministrator)]
    [InlineData(DismStatus.Cancelled, RepairVerdict.Cancelled)]
    [InlineData(DismStatus.Failed, RepairVerdict.Failed)]
    public async Task DismProblemsAreMappedAndAFailureKeepsTheCode(DismStatus status, RepairVerdict expected)
    {
        var service = Create(dism: new DismOutcome(status, DismHealth.Healthy, unchecked((int)0x80070005)));

        var result = await service.RunAsync(RepairCheck.ComponentsQuick, new Collector(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Verdict);
        Assert.Equal(expected == RepairVerdict.Failed ? "80070005" : null, result.Detail);
    }

    [Fact]
    public async Task SfcWithoutViolationsIsHealthyAndWithViolationsIsProblemsFound()
    {
        var clean = await Create(sfc: new SfcOutcome(SfcStatus.Done, 0, false, false, null)).RunAsync(RepairCheck.SystemFiles, new Collector(), TestContext.Current.CancellationToken);
        var dirty = await Create(sfc: new SfcOutcome(SfcStatus.Done, 1, true, true, "2 arquivos")).RunAsync(RepairCheck.SystemFiles, new Collector(), TestContext.Current.CancellationToken);

        Assert.Equal(RepairVerdict.Healthy, clean.Verdict);
        Assert.Equal(RepairVerdict.ProblemsFound, dirty.Verdict);
        Assert.Equal("2 arquivos", dirty.Detail);
    }

    [Theory]
    [InlineData(SfcStatus.NeedsAdministrator, RepairVerdict.NeedsAdministrator)]
    [InlineData(SfcStatus.Cancelled, RepairVerdict.Cancelled)]
    [InlineData(SfcStatus.Failed, RepairVerdict.Failed)]
    public async Task SfcProblemsAreMapped(SfcStatus status, RepairVerdict expected)
    {
        var service = Create(sfc: new SfcOutcome(status, -1, false, false, null));

        var result = await service.RunAsync(RepairCheck.SystemFiles, new Collector(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Verdict);
    }

    [Fact]
    public async Task CancellingBeforeTheCheckStartsReportsCancelledInsteadOfThrowing()
    {
        var dism = new StubDism(new DismOutcome(DismStatus.Done, DismHealth.Healthy, 0));
        var service = Create(dismStub: dism);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        var result = await service.RunAsync(RepairCheck.ComponentsFull, new Collector(), cancel.Token);

        Assert.Equal(RepairVerdict.Cancelled, result.Verdict);
        Assert.Empty(dism.Scans);
    }

    [Fact]
    public async Task CancellingInTheMiddleOfTheDismCheckReportsCancelled()
    {
        var service = Create(dismStub: new CancellingDism());
        using var cancel = new CancellationTokenSource();

        var result = await service.RunAsync(RepairCheck.ComponentsFull, new Collector(), cancel.Token);

        Assert.Equal(RepairVerdict.Cancelled, result.Verdict);
    }

    private sealed class CancellingDism : IDismSource
    {
        public DismOutcome CheckHealth(bool scan, IProgress<int> progress, CancellationToken cancellationToken) =>
            new(DismStatus.Cancelled, DismHealth.Healthy, unchecked((int)0x800704C7));
    }
}

public sealed class RealDismTests
{
    private sealed class Quiet : IProgress<int>
    {
        public void Report(int value)
        {
        }
    }

    private sealed class Recorder : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value)
        {
            lock (Values)
            {
                Values.Add(value);
            }
        }
    }

    [Fact]
    public void ElevatedTheQuickCheckReturnsAHealthVerdict()
    {
        Assert.SkipUnless(ElevationInfo.Current().IsElevated, "Precisa de administrador.");
        var source = new WindowsDismSource(NullLogger<WindowsDismSource>.Instance);

        var outcome = source.CheckHealth(scan: false, new Quiet(), TestContext.Current.CancellationToken);

        Console.WriteLine($"DISM API rapida: status {outcome.Status}, estado {outcome.Health}, codigo {outcome.ErrorCode}");
        Assert.Equal(DismStatus.Done, outcome.Status);
        Assert.Contains(outcome.Health, new[] { DismHealth.Healthy, DismHealth.Repairable, DismHealth.NotRepairable });
    }

    [Fact]
    public void ElevatedTheFullScanReportsProgressAndStopsWhenCancelled()
    {
        Assert.SkipUnless(ElevationInfo.Current().IsElevated, "Precisa de administrador.");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("M1VCLOCK_SLOW_TESTS") == "1", "Teste lento: defina M1VCLOCK_SLOW_TESTS=1.");
        var source = new WindowsDismSource(NullLogger<WindowsDismSource>.Instance);
        var recorder = new Recorder();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var progress = new Progress5(recorder, cancel);

        var outcome = source.CheckHealth(scan: true, progress, cancel.Token);

        Assert.Contains(outcome.Status, new[] { DismStatus.Cancelled, DismStatus.Done });
        Assert.NotEmpty(recorder.Values);
        Assert.All(recorder.Values, v => Assert.InRange(v, 0, 100));
        Assert.Equal(recorder.Values.Order().ToList(), recorder.Values);
    }

    private sealed class Progress5(Recorder recorder, CancellationTokenSource cancel) : IProgress<int>
    {
        public void Report(int value)
        {
            recorder.Report(value);
            if (value >= 15)
            {
                cancel.Cancel();
            }
        }
    }

    [Fact]
    public void ThisPcDismIsAskedForRealAndAnswersWithAVerdictOrAsksForAdministrator()
    {
        var source = new WindowsDismSource(NullLogger<WindowsDismSource>.Instance);

        var outcome = source.CheckHealth(scan: false, new Quiet(), TestContext.Current.CancellationToken);

        if (ElevationInfo.Current().IsElevated)
        {
            Assert.Equal(DismStatus.Done, outcome.Status);
        }
        else
        {
            Assert.Equal(DismStatus.NeedsAdministrator, outcome.Status);
        }
    }
}

public sealed class SfcOutputTests
{
    private static readonly string[] RealRun =
    [
        "2026-10-22 23:30:43, Info                  CSI    0000000a [SR] Verifying 100 components",
        "2026-10-22 23:30:43, Info                  CSI    0000000b [SR] Beginning Verify and Repair transaction",
        "2026-10-22 23:30:43, Info                  CSI    0000000c [SR] Verify complete",
        "2026-10-22 23:30:43, Info                  CSI    0000000d [SR] Verifying 100 components",
        "2026-10-22 23:30:43, Info                  CSI    0000000e [SR] Beginning Verify and Repair transaction",
        "2026-10-22 23:30:43, Info                  CSI    0000000f [SR] Verify complete",
        @"2026-10-22 23:36:49, Info                  DEPLOY [Pnp] Corrupt file: C:\WINDOWS\System32\drivers\bthmodem.sys",
    ];

    private static readonly string[] CleanRun = RealRun[..6];

    [Theory]
    [InlineData("Verificação 35% concluída.", 35)]
    [InlineData("Verification 7% complete.", 7)]
    [InlineData("Verificação 100% concluída.\r\r\n", 100)]
    [InlineData("Verificação 1% concluída.\r\r\nVerificação 2% concl", 2)]
    public void ThePercentComesFromTheNumberAloneSoItWorksInAnyLanguage(string text, int expected)
    {
        Assert.Equal(expected, SfcOutput.LastPercent(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Iniciando verificação de arquivos.")]
    [InlineData("Verifying 100 components")]
    [InlineData("Progresso 250% estranho")]
    public void WithoutAUsablePercentNothingIsReported(string text)
    {
        Assert.Null(SfcOutput.LastPercent(text));
    }

    [Fact]
    public void ARunWithOnlyTheRoutineMarkersAndExitCodeZeroIsClean()
    {
        var outcome = SfcOutput.Analyze(0, CleanRun);

        Assert.Equal(SfcStatus.Done, outcome.Status);
        Assert.False(outcome.ViolationsFound);
        Assert.False(outcome.CouldNotRepair);
        Assert.Null(outcome.Detail);
    }

    [Fact]
    public void TheRealRunOfThisPcFlagsTheCorruptDriverEvenThoughSfcExitedWithZero()
    {
        var outcome = SfcOutput.Analyze(0, RealRun);

        Assert.Equal(SfcStatus.Done, outcome.Status);
        Assert.True(outcome.ViolationsFound);
        Assert.Equal("bthmodem.sys", outcome.Detail);
        Assert.False(outcome.CouldNotRepair);
    }

    [Fact]
    public void RepeatedFilesAreListedOnceAndOnlyTheFirstFiveAreShown()
    {
        var lines = new List<string> { @"DEPLOY [Pnp] Corrupt file: C:\WINDOWS\System32\drivers\a.sys", @"DEPLOY [Pnp] Corrupt file: C:\WINDOWS\SysWOW64\A.SYS" };
        lines.AddRange(Enumerable.Range(1, 6).Select(i => $@"DEPLOY [Pnp] Corrupt file: C:\WINDOWS\System32\f{i}.dll"));

        var outcome = SfcOutput.Analyze(0, lines);

        Assert.Equal("a.sys, f1.dll, f2.dll, f3.dll, f4.dll, …", outcome.Detail);
    }

    [Fact]
    public void AnUnusualMarkerLineCountsAsAProblemAndCannotRepairIsRemembered()
    {
        var lines = new List<string>(CleanRun) { "2026-10-22 23:40:00, Info CSI 00000100 [SR] Cannot repair member file [l:10]\"x.dll\" of Microsoft-Windows-Foo" };

        var outcome = SfcOutput.Analyze(0, lines);

        Assert.True(outcome.ViolationsFound);
        Assert.True(outcome.CouldNotRepair);
        Assert.Null(outcome.Detail);
    }

    [Fact]
    public void ARunThatVerifiedNothingOrExitedWithAnErrorIsAFailureNotAHealthyResult()
    {
        var nothing = SfcOutput.Analyze(0, []);
        var badExit = SfcOutput.Analyze(1, CleanRun);

        Assert.Equal(SfcStatus.Failed, nothing.Status);
        Assert.Equal("0", nothing.Detail);
        Assert.Equal(SfcStatus.Failed, badExit.Status);
        Assert.Equal("1", badExit.Detail);
    }

    [Fact]
    public void OnlyMarkerLinesAreKept()
    {
        Assert.True(SfcOutput.IsRelevant(RealRun[0]));
        Assert.True(SfcOutput.IsRelevant(RealRun[6]));
        Assert.False(SfcOutput.IsRelevant("2026-10-22 23:30:40, Info                  CBS    TI: --- Initializing Trusted Installer ---"));
    }
}

public sealed class WindowsSfcSourceTests
{
    private const string Routine = "2026-10-22 23:30:43, Info                  CSI    0000000a [SR] Verifying 100 components\n" +
        "2026-10-22 23:30:43, Info                  CSI    0000000b [SR] Beginning Verify and Repair transaction\n" +
        "2026-10-22 23:30:43, Info                  CSI    0000000c [SR] Verify complete\n";

    private const string Corrupt = "2026-10-22 23:36:49, Info                  DEPLOY [Pnp] Corrupt file: C:\\WINDOWS\\System32\\drivers\\bthmodem.sys\n";

    private sealed class FakeRunner(Func<ProcessRequest, CancellationToken, Task<ProcessResult>> handler) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return handler(request, cancellationToken);
        }
    }

    private static ElevationInfo Admin(bool elevated) => new(elevated, "S-1-5-21-1", @"PC\joao");

    private static WindowsSfcSource Create(FakeRunner runner, string logPath, bool elevated = true) =>
        new(Admin(elevated), runner, NullLogger<WindowsSfcSource>.Instance, logPath);

    private static ProcessResult Exit(int code, bool timedOut = false) => new(code, string.Empty, string.Empty, TimeSpan.FromMinutes(6), timedOut);

    [Fact]
    public async Task WithoutAdministratorTheSfcIsNotLaunchedAtAll()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult(Exit(0)));
        using var temp = new TempFile();

        var outcome = await Create(runner, temp.At("CBS.log"), elevated: false).VerifyAsync(new Progress<int>(), TestContext.Current.CancellationToken);

        Assert.Equal(SfcStatus.NeedsAdministrator, outcome.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task TheSfcIsStartedByTheApprovedRunnerWithFullPathVerifyOnlyAndUnicodeOutput()
    {
        using var temp = new TempFile();
        var log = temp.At("CBS.log");
        await File.WriteAllTextAsync(log, "linha antiga\n", TestContext.Current.CancellationToken);
        var runner = new FakeRunner(async (_, token) =>
        {
            await File.AppendAllTextAsync(log, Routine, token);
            return Exit(0);
        });

        await Create(runner, log).VerifyAsync(new Progress<int>(), TestContext.Current.CancellationToken);

        var request = Assert.Single(runner.Requests);
        Assert.True(Path.IsPathFullyQualified(request.FileName));
        Assert.EndsWith("sfc.exe", request.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["/verifyonly"], request.Arguments);
        Assert.Equal(System.Text.Encoding.Unicode, request.OutputEncoding);
        Assert.True(request.Timeout >= TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task OnlyLinesWrittenDuringTheRunCountAndARunWithOnlyRoutineMarkersIsClean()
    {
        using var temp = new TempFile();
        var log = temp.At("CBS.log");
        await File.WriteAllTextAsync(log, Corrupt, TestContext.Current.CancellationToken);
        var runner = new FakeRunner(async (_, token) =>
        {
            await File.AppendAllTextAsync(log, Routine, token);
            return Exit(0);
        });

        var outcome = await Create(runner, log).VerifyAsync(new Progress<int>(), TestContext.Current.CancellationToken);

        Assert.Equal(SfcStatus.Done, outcome.Status);
        Assert.False(outcome.ViolationsFound);
    }

    [Fact]
    public async Task ACorruptFileLoggedDuringTheRunIsReportedEvenWithExitCodeZero()
    {
        using var temp = new TempFile();
        var log = temp.At("CBS.log");
        await File.WriteAllTextAsync(log, "linha antiga\n", TestContext.Current.CancellationToken);
        var runner = new FakeRunner(async (_, token) =>
        {
            await File.AppendAllTextAsync(log, Routine + Corrupt, token);
            return Exit(0);
        });

        var outcome = await Create(runner, log).VerifyAsync(new Progress<int>(), TestContext.Current.CancellationToken);

        Assert.True(outcome.ViolationsFound);
        Assert.Equal("bthmodem.sys", outcome.Detail);
    }

    [Fact]
    public async Task ALogThatShrankDuringTheRunIsReadFromTheStart()
    {
        using var temp = new TempFile();
        var log = temp.At("CBS.log");
        await File.WriteAllTextAsync(log, new string('x', 5000) + "\n", TestContext.Current.CancellationToken);
        var runner = new FakeRunner(async (_, token) =>
        {
            await File.WriteAllTextAsync(log, Routine + Corrupt, token);
            return Exit(0);
        });

        var outcome = await Create(runner, log).VerifyAsync(new Progress<int>(), TestContext.Current.CancellationToken);

        Assert.True(outcome.ViolationsFound);
    }

    [Fact]
    public async Task AMissingLogMeansNothingWasVerifiedSoItIsAFailure()
    {
        using var temp = new TempFile();
        var runner = new FakeRunner((_, _) => Task.FromResult(Exit(0)));

        var outcome = await Create(runner, temp.At("nao-existe.log")).VerifyAsync(new Progress<int>(), TestContext.Current.CancellationToken);

        Assert.Equal(SfcStatus.Failed, outcome.Status);
    }

    [Fact]
    public async Task ATimeoutIsAFailureAndCancellingIsCancelled()
    {
        using var temp = new TempFile();
        var timedOut = Create(new FakeRunner((_, _) => Task.FromResult(Exit(-1, timedOut: true))), temp.At("CBS.log"));
        var cancelled = Create(new FakeRunner((_, _) => throw new OperationCanceledException()), temp.At("CBS.log"));

        var first = await timedOut.VerifyAsync(new Progress<int>(), TestContext.Current.CancellationToken);
        var second = await cancelled.VerifyAsync(new Progress<int>(), TestContext.Current.CancellationToken);

        Assert.Equal(SfcStatus.Failed, first.Status);
        Assert.Equal(SfcStatus.Cancelled, second.Status);
    }

    [Fact]
    public async Task AToolThatCannotBeStartedIsAFailure()
    {
        using var temp = new TempFile();
        var runner = new FakeRunner((_, _) => throw new FileNotFoundException("sfc"));

        var outcome = await Create(runner, temp.At("CBS.log")).VerifyAsync(new Progress<int>(), TestContext.Current.CancellationToken);

        Assert.Equal(SfcStatus.Failed, outcome.Status);
    }
}
