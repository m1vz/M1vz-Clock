using System.ComponentModel;
using M1vClock.App.Dialogs;
using M1vClock.App.Pages.Optimize;
using M1vClock.Core.Results;
using M1vClock.Core.Shell;
using M1vClock.Optimize.Apps;

namespace M1vClock.App.Tests;

public sealed class AppsPageViewModelTests : IDisposable
{
    private readonly DialogService _dialogs = new();
    private readonly List<DialogRequest> _shown = [];
    private bool _answer = true;

    public AppsPageViewModelTests()
    {
        _dialogs.Opened += (_, _) =>
        {
            _shown.Add(_dialogs.Current!);
            if (_answer)
            {
                _dialogs.ConfirmCommand.Execute(null);
            }
            else
            {
                _dialogs.CancelCommand.Execute(null);
            }
        };
    }

    public void Dispose() => GC.SuppressFinalize(this);

    private sealed class StubApps(ReadResult<AppReport> result) : IInstalledAppService
    {
        public ReadResult<AppReport> Result { get; set; } = result;

        public Task<ReadResult<AppReport>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Result);
    }

    private sealed class RecordingShell : IShellLauncher
    {
        public List<(string FileName, string? Arguments, string? WorkingDirectory)> Started { get; } = [];

        public Exception? Failure { get; set; }

        public void OpenUrl(Uri url) => throw new NotSupportedException();

        public void OpenFolder(string path) => throw new NotSupportedException();

        public void Start(string fileName, string? arguments, string? workingDirectory)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Started.Add((fileName, arguments, workingDirectory));
        }
    }

    private static InstalledApp App(string name, long? sizeBytes, bool withUninstaller = true, string? publisher = "Empresa", DateOnly? date = null, AppScope scope = AppScope.Machine) =>
        new(name, name, "1.0", publisher, date, sizeBytes, scope, withUninstaller ? @"""C:\Programas\" + name + @"\unins000.exe"" /X" : null)
        {
            Launch = withUninstaller ? new UninstallLaunch(@"C:\Programas\" + name + @"\unins000.exe", "/X") : null,
        };

    private static ReadResult<AppReport> Report(params InstalledApp[] apps) => ReadResult.Success(new AppReport(apps));

    private static ReadResult<AppReport> ThisPc() => Report(
        App("Médio", 512L * 1024 * 1024),
        App("Grande", 2L * 1024 * 1024 * 1024, date: new DateOnly(2026, 1, 10)),
        App("Sem desinstalador", null, withUninstaller: false),
        App("Alfa", 1024L * 1024, scope: AppScope.User));

    private AppsPageViewModel Create(StubApps apps, RecordingShell? shell = null) =>
        new(apps, shell ?? new RecordingShell(), _dialogs, new CollectingLogger<AppsPageViewModel>());

    [Fact]
    public async Task TheSummaryCountsProgramsDeclaredSpaceTheLargestAndTheOnesWithoutUninstaller()
    {
        var page = Create(new StubApps(ThisPc()));

        await page.RefreshAsync();

        Assert.Equal("4", page.TotalText);
        Assert.Equal("2,5 GB", page.SizeText);
        Assert.Equal("2 GB", page.LargestText);
        Assert.Equal("1", page.NoUninstallerText);
        Assert.False(page.IsLoading);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task TheRowsStartSortedBySizeAndChangeWithTheSortAndTheSearch()
    {
        var page = Create(new StubApps(ThisPc()));
        await page.RefreshAsync();

        Assert.Equal(["Grande", "Médio", "Alfa", "Sem desinstalador"], page.Items.Select(r => r.Title));

        page.SortIndex = 1;
        Assert.Equal(["Alfa", "Grande", "Médio", "Sem desinstalador"], page.Items.Select(r => r.Title));

        page.SearchText = "méd";
        Assert.Equal(["Médio"], page.Items.Select(r => r.Title));

        page.SearchText = "empresa";
        Assert.Equal(4, page.Items.Count);

        page.SearchText = "nada disso";
        Assert.Empty(page.Items);
        Assert.True(page.IsEmpty);
    }

    [Fact]
    public async Task ARowShowsPublisherVersionSizeDateAndTheUserScope()
    {
        var page = Create(new StubApps(ThisPc()));
        await page.RefreshAsync();

        var big = page.Items.Single(r => r.Title == "Grande");
        var user = page.Items.Single(r => r.Title == "Alfa");
        var unknown = page.Items.Single(r => r.Title == "Sem desinstalador");

        Assert.Equal("Empresa · 1.0", big.Detail);
        Assert.Equal("2 GB", big.SizeText);
        Assert.Equal("instalado em 10/01/2026", big.Meta);
        Assert.Equal("só para este usuário", user.Meta);
        Assert.Equal("Tamanho não informado", unknown.SizeText);
        Assert.False(unknown.HasMeta);
        Assert.Equal("Grande, 2 GB", big.AccessibleName);
    }

    [Fact]
    public async Task UninstallingAsksFirstSayingTheProgramOwnUninstallerOpensAndThenStartsIt()
    {
        var shell = new RecordingShell();
        var page = Create(new StubApps(ThisPc()), shell);
        await page.RefreshAsync();

        await page.UninstallAsync(page.Items.Single(r => r.Title == "Grande"));

        var request = Assert.Single(_shown);
        Assert.Equal("Desinstalar Grande?", request.Title);
        Assert.Contains("desinstalador do próprio programa", request.Message, StringComparison.Ordinal);
        Assert.Contains("o m1vClock não apaga nada", request.Message, StringComparison.Ordinal);
        Assert.Equal(["O desinstalador de Grande será aberto."], request.Changes);
        Assert.Contains("não consegue desfazer", request.Undo, StringComparison.Ordinal);
        Assert.Equal(DialogTone.Destructive, request.Tone);
        var started = Assert.Single(shell.Started);
        Assert.Equal(@"C:\Programas\Grande\unins000.exe", started.FileName);
        Assert.Equal("/X", started.Arguments);
        Assert.Equal(@"C:\Programas\Grande", started.WorkingDirectory);
        Assert.StartsWith("O desinstalador de Grande foi aberto.", page.Notice, StringComparison.Ordinal);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task RefusingTheQuestionOpensNothing()
    {
        _answer = false;
        var shell = new RecordingShell();
        var page = Create(new StubApps(ThisPc()), shell);
        await page.RefreshAsync();

        await page.UninstallAsync(page.Items[0]);

        Assert.Single(_shown);
        Assert.Empty(shell.Started);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task AProgramWithoutAnUninstallerHasTheButtonOffAndNothingIsAsked()
    {
        var shell = new RecordingShell();
        var page = Create(new StubApps(ThisPc()), shell);
        await page.RefreshAsync();
        var row = page.Items.Single(r => r.Title == "Sem desinstalador");

        await page.UninstallAsync(row);
        await page.UninstallAsync(null);

        Assert.False(row.CanUninstall);
        Assert.Contains("Configurações do Windows", row.UninstallToolTip, StringComparison.Ordinal);
        Assert.Empty(_shown);
        Assert.Empty(shell.Started);
    }

    [Fact]
    public async Task WindowsRefusingToStartTheUninstallerShowsTheReasonAndNoSuccess()
    {
        var shell = new RecordingShell { Failure = new Win32Exception("O sistema não pode encontrar o arquivo") };
        var page = Create(new StubApps(ThisPc()), shell);
        await page.RefreshAsync();

        await page.UninstallAsync(page.Items.Single(r => r.Title == "Grande"));

        Assert.Equal("Não foi possível abrir o desinstalador de Grande: O sistema não pode encontrar o arquivo", page.Problem);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task MoreThanTwoHundredProgramsShowTheFirstOnesAndSayHowManyThereAre()
    {
        var many = Enumerable.Range(0, 250).Select(i => App($"Programa {i:000}", (i + 1) * 1024L)).ToArray();
        var page = Create(new StubApps(Report(many)));

        await page.RefreshAsync();

        Assert.Equal(200, page.Items.Count);
        Assert.Equal("Mostrando 200 de 250 programas. Use a busca para achar os outros.", page.ShowingText);
        Assert.Equal("Programa 249", page.Items[0].Title);
    }

    [Fact]
    public async Task AReadThatFailedShowsTheProblemAndDashesAndClearsItWhenItWorksAgain()
    {
        var apps = new StubApps(ReadResult.Failure<AppReport>("sem acesso ao registro"));
        var page = Create(apps);

        await page.RefreshAsync();

        Assert.Equal("Não foi possível ler os programas instalados: sem acesso ao registro", page.Problem);
        Assert.Equal("—", page.TotalText);
        Assert.Empty(page.Items);

        apps.Result = ThisPc();
        await page.RefreshAsync();

        Assert.False(page.HasProblem);
        Assert.Equal("4", page.TotalText);
        Assert.Equal(4, page.Items.Count);
    }
}
