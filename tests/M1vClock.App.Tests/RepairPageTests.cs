using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Pages.Optimize;
using M1vClock.App.Services;
using M1vClock.Core.Security;
using M1vClock.Optimize.Repair;

namespace M1vClock.App.Tests;

public sealed class RepairPageViewModelTests
{
    private sealed class StubRepair(Func<RepairCheck, IProgress<int>, CancellationToken, Task<RepairResult>> handler) : IRepairService
    {
        public List<RepairCheck> Runs { get; } = [];

        public Task<RepairResult> RunAsync(RepairCheck check, IProgress<int> progress, CancellationToken cancellationToken)
        {
            Runs.Add(check);
            return handler(check, progress, cancellationToken);
        }
    }

    private sealed class StubClipboard(bool works = true) : IClipboardService
    {
        public List<string> Copied { get; } = [];

        public bool TrySetText(string text)
        {
            Copied.Add(text);
            return works;
        }
    }

    private static ElevationInfo Elevation(bool elevated) => new(elevated, "S-1-5-21-1-2-3-1001", @"PC-DE-TESTE\joao");

    private static Task<RepairResult> Done(RepairCheck check, RepairVerdict verdict, int seconds = 12, string? detail = null) =>
        Task.FromResult(new RepairResult(check, verdict, TimeSpan.FromSeconds(seconds), detail));

    private static RepairPageViewModel Create(StubRepair repair, bool elevated = true, StubClipboard? clipboard = null) =>
        new(repair, clipboard ?? new StubClipboard(), Elevation(elevated));

    [Fact]
    public void ThePageStartsWithThreeChecksNothingRunAndNothingAdvised()
    {
        var page = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.Healthy)));

        Assert.Equal(3, page.Cards.Count);
        Assert.Equal([RepairCheck.ComponentsQuick, RepairCheck.ComponentsFull, RepairCheck.SystemFiles], page.Cards.Select(c => c.Check));
        Assert.All(page.Cards, c => Assert.Equal("Ainda não verificado", c.VerdictText));
        Assert.All(page.Cards, c => Assert.False(c.HasAdvice || c.HasCommand || c.HasTook));
        Assert.True(page.CanRun);
        Assert.False(page.NeedsAdministrator);
        Assert.False(page.IsRunning);
    }

    [Fact]
    public async Task WithoutAdministratorNothingRunsAndThePageSaysWhy()
    {
        var repair = new StubRepair((c, _, _) => Done(c, RepairVerdict.Healthy));
        var page = Create(repair, elevated: false);

        await page.RunAsync(page.Cards[0]);

        Assert.True(page.NeedsAdministrator);
        Assert.False(page.CanRun);
        Assert.Empty(repair.Runs);
    }

    [Fact]
    public async Task AHealthyResultShowsGoodWithTheTimeItTookAndNoAdvice()
    {
        var page = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.Healthy, seconds: 125)));

        await page.RunAsync(page.Cards[2]);

        var card = page.Cards[2];
        Assert.Equal("Sem problemas", card.VerdictText);
        Assert.Equal(MetricState.Good, card.Tone);
        Assert.Equal("Levou 2 min 5 s.", card.TookText);
        Assert.False(card.HasAdvice);
        Assert.False(card.HasCommand);
        Assert.False(page.IsRunning);
        Assert.True(page.CanRun);
        Assert.Equal("Ainda não verificado", page.Cards[0].VerdictText);
    }

    [Fact]
    public async Task WhileRunningTheProgressIsShownTheOtherButtonsAreOffAndCancelWorks()
    {
        var release = new TaskCompletionSource<RepairResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = Create(new StubRepair((c, progress, token) =>
        {
            progress.Report(30);
            progress.Report(60);
            started.SetResult();
            token.Register(() => release.TrySetResult(new RepairResult(c, RepairVerdict.Cancelled, TimeSpan.FromSeconds(3), null)));
            return release.Task;
        }));

        var run = page.RunAsync(page.Cards[1]);
        await started.Task;

        Assert.True(page.IsRunning);
        Assert.False(page.CanRun);
        Assert.True(page.HasFraction);
        Assert.False(page.ShowHint);
        Assert.Equal(0.6, page.Fraction, 3);
        Assert.Equal("Verificando: Componentes do Windows: verificação completa (DISM) · 60%", page.ProgressText);

        page.CancelCommand.Execute(null);
        await run;

        Assert.False(page.IsRunning);
        Assert.Equal("Cancelada", page.Cards[1].VerdictText);
        Assert.Equal(MetricState.Neutral, page.Cards[1].Tone);
        Assert.True(page.CanRun);
    }

    [Fact]
    public async Task ACheckThatReportsNoPercentShowsAHintInsteadOfAFakeBar()
    {
        var release = new TaskCompletionSource<RepairResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = Create(new StubRepair((_, _, _) =>
        {
            started.SetResult();
            return release.Task;
        }));

        var run = page.RunAsync(page.Cards[2]);
        await started.Task;

        Assert.True(page.IsRunning);
        Assert.False(page.HasFraction);
        Assert.True(page.ShowHint);
        Assert.Equal("Esta verificação não mostra o andamento por aqui. Costuma levar alguns minutos.", page.ProgressHint);

        release.SetResult(new RepairResult(RepairCheck.SystemFiles, RepairVerdict.Healthy, TimeSpan.FromSeconds(1), null));
        await run;

        Assert.False(page.ShowHint);
    }

    [Fact]
    public async Task LeavingThePageCancelsTheCheckInProgress()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = Create(new StubRepair(async (c, _, token) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                return new RepairResult(c, RepairVerdict.Cancelled, TimeSpan.Zero, null);
            }

            return new RepairResult(c, RepairVerdict.Healthy, TimeSpan.Zero, null);
        }));

        var run = page.RunAsync(page.Cards[0]);
        await started.Task;
        page.OnNavigatedFrom();
        await run;

        Assert.Equal("Cancelada", page.Cards[0].VerdictText);
        Assert.False(page.IsRunning);
    }

    [Fact]
    public async Task ARepairableStoreWarnsGivesTheDismCommandAndMentionsTrimmedWindows()
    {
        var page = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.Repairable)));

        await page.RunAsync(page.Cards[0]);

        var card = page.Cards[0];
        Assert.Equal("Reparável", card.VerdictText);
        Assert.Equal(MetricState.Warning, card.Tone);
        Assert.Equal("DISM /Online /Cleanup-Image /RestoreHealth", card.Command);
        Assert.True(card.HasCommand);
        Assert.Contains("ainda não faz esse reparo", card.Advice, StringComparison.Ordinal);
        Assert.Contains("Atlas", card.Advice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SystemFilesWithProblemsAreCriticalAndGiveTheSfcCommand()
    {
        var page = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.ProblemsFound, detail: "bthmodem.sys")));

        await page.RunAsync(page.Cards[2]);

        var card = page.Cards[2];
        Assert.Equal("Problemas encontrados", card.VerdictText);
        Assert.Equal(MetricState.Critical, card.Tone);
        Assert.Equal("sfc /scannow", card.Command);
        Assert.StartsWith("O SFC achou arquivos do sistema diferentes das cópias guardadas. Arquivos: bthmodem.sys. Para reparar,", card.Advice, StringComparison.Ordinal);
        Assert.Contains("Atlas", card.Advice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SystemFilesWithProblemsButNoFileNameStillAdvise()
    {
        var page = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.ProblemsFound)));

        await page.RunAsync(page.Cards[2]);

        Assert.StartsWith("O SFC achou arquivos do sistema diferentes das cópias guardadas. Para reparar,", page.Cards[2].Advice, StringComparison.Ordinal);
        Assert.DoesNotContain("Arquivos:", page.Cards[2].Advice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureShowsTheCodeWhenThereIsOneAndNoCommand()
    {
        var withCode = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.Failed, detail: "80070005")));
        var without = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.Failed)));

        await withCode.RunAsync(withCode.Cards[0]);
        await without.RunAsync(without.Cards[0]);

        Assert.Equal("A verificação não terminou (código 80070005). O motivo está no log do app.", withCode.Cards[0].Advice);
        Assert.Equal("A verificação não terminou. O motivo está no log do app.", without.Cards[0].Advice);
        Assert.False(withCode.Cards[0].HasCommand);
        Assert.Equal(MetricState.Critical, withCode.Cards[0].Tone);
    }

    [Fact]
    public async Task NeedingAdministratorShowsNoTimeAndNoCommand()
    {
        var page = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.NeedsAdministrator)));

        await page.RunAsync(page.Cards[0]);

        Assert.Equal("Precisa de administrador", page.Cards[0].VerdictText);
        Assert.False(page.Cards[0].HasTook);
        Assert.False(page.Cards[0].HasCommand);
    }

    [Fact]
    public async Task CopyingTheCommandPutsItOnTheClipboardAndSaysSo()
    {
        var clipboard = new StubClipboard();
        var page = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.NotRepairable)), clipboard: clipboard);
        await page.RunAsync(page.Cards[1]);

        page.Copy(page.Cards[1]);

        Assert.Equal(["DISM /Online /Cleanup-Image /RestoreHealth"], clipboard.Copied);
        Assert.Equal("Comando copiado.", page.Notice);
        Assert.True(page.HasNotice);
    }

    [Fact]
    public async Task ABusyClipboardIsReportedAndACardWithoutCommandCopiesNothing()
    {
        var clipboard = new StubClipboard(works: false);
        var page = Create(new StubRepair((c, _, _) => Done(c, RepairVerdict.Repairable)), clipboard: clipboard);
        await page.RunAsync(page.Cards[0]);

        page.Copy(page.Cards[2]);
        Assert.Empty(clipboard.Copied);

        page.Copy(page.Cards[0]);
        Assert.Equal("Não foi possível copiar. Selecione o comando e copie à mão.", page.Notice);
    }
}
