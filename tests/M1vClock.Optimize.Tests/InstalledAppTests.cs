using M1vClock.Core.Results;
using M1vClock.Optimize.Apps;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Optimize.Tests;

public sealed class InstalledAppTests
{
    private static InstalledAppRaw Raw(
        string key,
        string? name,
        AppScope scope = AppScope.Machine,
        string? version = "1.0",
        string? publisher = "Empresa",
        string? date = "20260110",
        long? size = 1024,
        string? uninstall = @"""C:\Programas\Teste\unins000.exe""",
        int? system = null,
        string? parent = null) =>
        new(key, scope, name, version, publisher, date, size, uninstall, system, parent);

    [Fact]
    public void SystemComponentsEntriesWithoutNameAndUpdatesOfOtherProgramsAreLeftOut()
    {
        var apps = InstalledAppService.Describe(
        [
            Raw("a", "Brave"),
            Raw("b", "Componente do sistema", system: 1),
            Raw("c", null),
            Raw("d", "   "),
            Raw("e", "Atualização do Brave", parent: "Brave"),
        ]);

        Assert.Equal(["Brave"], apps.Select(a => a.Name));
    }

    [Fact]
    public void TheSizeIsKilobytesTimes1024AndZeroOrMissingMeansUnknown()
    {
        var apps = InstalledAppService.Describe(
        [
            Raw("a", "Grande", size: 36_222_339),
            Raw("b", "Zero", size: 0),
            Raw("c", "Sem tamanho", size: null),
        ]);

        Assert.Equal(36_222_339L * 1024, apps.Single(a => a.Name == "Grande").SizeBytes);
        Assert.Null(apps.Single(a => a.Name == "Zero").SizeBytes);
        Assert.Null(apps.Single(a => a.Name == "Sem tamanho").SizeBytes);
    }

    [Theory]
    [InlineData("20260110", 2026, 1, 10)]
    [InlineData(" 20251231 ", 2025, 12, 31)]
    public void TheInstallDateIsReadFromYyyyMmDd(string text, int year, int month, int day)
    {
        var app = Assert.Single(InstalledAppService.Describe([Raw("a", "App", date: text)]));

        Assert.Equal(new DateOnly(year, month, day), app.InstalledOn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-01-10")]
    [InlineData("20261350")]
    public void ADateThatCannotBeReadIsNotInvented(string? text)
    {
        var app = Assert.Single(InstalledAppService.Describe([Raw("a", "App", date: text)]));

        Assert.Null(app.InstalledOn);
    }

    [Fact]
    public void TheSameProgramInTheUserAndMachineHivesAppearsOnceKeepingTheUserOne()
    {
        var apps = InstalledAppService.Describe(
        [
            Raw("m", "Discord", AppScope.Machine32, "1.2"),
            Raw("u", "discord", AppScope.User, "1.2"),
            Raw("n", "Discord", AppScope.Machine, "1.3"),
        ]);

        Assert.Equal(2, apps.Count);
        Assert.Equal(AppScope.User, apps.Single(a => a.Version == "1.2").Scope);
    }

    [Fact]
    public void BlankFieldsBecomeNullAndTextIsTrimmed()
    {
        var app = Assert.Single(InstalledAppService.Describe([Raw("a", "  Nome  ", version: " ", publisher: "", uninstall: "  ")]));

        Assert.Equal("Nome", app.Name);
        Assert.Null(app.Version);
        Assert.Null(app.Publisher);
        Assert.Null(app.UninstallCommand);
    }

    [Fact]
    public void SortingBySizePutsTheBiggestFirstAndUnknownSizesLast()
    {
        var apps = InstalledAppService.Describe(
        [
            Raw("a", "Pequeno", size: 10),
            Raw("b", "Sem tamanho", size: null),
            Raw("c", "Enorme", size: 5_000_000),
            Raw("d", "Médio", size: 5000),
        ]);

        var sorted = InstalledAppService.Sort(apps, AppSort.Size);

        Assert.Equal(["Enorme", "Médio", "Pequeno", "Sem tamanho"], sorted.Select(a => a.Name));
    }

    [Fact]
    public void SortingByNameIgnoresCaseAndByDateShowsTheNewestFirst()
    {
        var apps = InstalledAppService.Describe(
        [
            Raw("a", "beta", date: "20240101"),
            Raw("b", "Alfa", date: "20260301"),
            Raw("c", "Gama", date: null),
        ]);

        Assert.Equal(["Alfa", "beta", "Gama"], InstalledAppService.Sort(apps, AppSort.Name).Select(a => a.Name));
        Assert.Equal(["Alfa", "beta", "Gama"], InstalledAppService.Sort(apps, AppSort.Date).Select(a => a.Name));
    }

    [Fact]
    public void TheReportSumsOnlyKnownSizesAndNamesTheLargest()
    {
        var report = new AppReport(InstalledAppService.Describe(
        [
            Raw("a", "A", size: 100),
            Raw("b", "B", size: 300),
            Raw("c", "C", size: null),
        ]));

        Assert.Equal(400L * 1024, report.KnownBytes);
        Assert.Equal("B", report.Largest?.Name);
        Assert.Null(new AppReport([]).Largest);
    }

    private static bool Exists(string path) => path is @"C:\Programas\Teste\unins000.exe" or @"C:\Windows\System32\msiexec.exe";

    [Fact]
    public void AQuotedUninstallerKeepsItsArgumentsApart()
    {
        var launch = InstalledAppService.ParseUninstall(@"""C:\Programas\Teste\unins000.exe"" /SILENT /LOG=""C:\a b\x.log""", Exists);

        Assert.NotNull(launch);
        Assert.Equal(@"C:\Programas\Teste\unins000.exe", launch.FileName);
        Assert.Equal(@"/SILENT /LOG=""C:\a b\x.log""", launch.Arguments);
    }

    [Fact]
    public void AQuotedUninstallerWithoutArgumentsHasEmptyArguments()
    {
        var launch = InstalledAppService.ParseUninstall(@"""C:\Programas\Teste\unins000.exe""", Exists);

        Assert.NotNull(launch);
        Assert.Equal(string.Empty, launch.Arguments);
    }

    [Fact]
    public void MsiexecWithoutAPathIsFoundInTheSystemFolder()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var launch = InstalledAppService.ParseUninstall(
            "MsiExec.exe /X{0B4F2E10-1111-2222-3333-444455556666}",
            path => string.Equals(path, Path.Combine(system, "MsiExec.exe"), StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(launch);
        Assert.Equal(Path.Combine(system, "MsiExec.exe"), launch.FileName);
        Assert.Equal("/X{0B4F2E10-1111-2222-3333-444455556666}", launch.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"""C:\Some\Where\missing.exe"" /S")]
    [InlineData("naoexiste.exe /S")]
    public void NothingIsLaunchedWhenThereIsNoCommandOrTheProgramIsGone(string? command)
    {
        Assert.Null(InstalledAppService.ParseUninstall(command, Exists));
    }

    [Fact]
    public async Task TheServiceReadsFromTheSourceAndDescribesIt()
    {
        var source = new StubSource(ReadResult.Success<IReadOnlyList<InstalledAppRaw>>([Raw("a", "Brave", size: 2048), Raw("b", "Oculto", system: 1)]));
        var service = new InstalledAppService(source);

        var result = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Brave"], result.Value!.Apps.Select(a => a.Name));
    }

    [Fact]
    public async Task OnlyProgramsWhoseUninstallerStillExistsGetALaunch()
    {
        var source = new StubSource(ReadResult.Success<IReadOnlyList<InstalledAppRaw>>(
        [
            Raw("a", "Presente", uninstall: @"""C:\Programas\Teste\unins000.exe"" /SILENT"),
            Raw("b", "Sumido", uninstall: @"""C:\Programas\Sumido\unins000.exe"""),
            Raw("c", "Sem comando", uninstall: null),
        ]));
        var service = new InstalledAppService(source, Exists);

        var result = await service.ReadAsync(TestContext.Current.CancellationToken);

        var apps = result.Value!.Apps;
        Assert.Equal(@"C:\Programas\Teste\unins000.exe", apps.Single(a => a.Name == "Presente").Launch?.FileName);
        Assert.Equal("/SILENT", apps.Single(a => a.Name == "Presente").Launch?.Arguments);
        Assert.Null(apps.Single(a => a.Name == "Sumido").Launch);
        Assert.Null(apps.Single(a => a.Name == "Sem comando").Launch);
    }

    [Fact]
    public async Task AFailedReadStaysAFailure()
    {
        var service = new InstalledAppService(new StubSource(ReadResult.Failure<IReadOnlyList<InstalledAppRaw>>("sem acesso")));

        var result = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("sem acesso", result.Error);
    }

    [Fact]
    public async Task ThisPcRegistryYieldsRealProgramsWithoutSystemComponents()
    {
        var service = new InstalledAppService(new WindowsInstalledAppSource(NullLogger<WindowsInstalledAppSource>.Instance));

        var result = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var apps = result.Value!.Apps;
        Assert.NotEmpty(apps);
        Assert.All(apps, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
        Assert.Equal(apps.Count, apps.Select(a => (a.Name.ToUpperInvariant(), a.Version)).Distinct().Count());
    }

    private sealed class StubSource(ReadResult<IReadOnlyList<InstalledAppRaw>> result) : IInstalledAppSource
    {
        public ReadResult<IReadOnlyList<InstalledAppRaw>> Read() => result;
    }
}
