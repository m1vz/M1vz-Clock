using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace M1vClock.Architecture.Tests;

public sealed partial class ProjectStructureTests
{
    private static readonly string[] Modules =
    [
        "Hardware", "Benchmarks", "Tuning", "Optimize", "Diagnostics", "Games", "Integrations", "Maker",
    ];

    [GeneratedRegex(@"^\d+(\.\d+){1,3}(-[A-Za-z0-9.]+)?$")]
    private static partial Regex ExactVersion();

    [Fact]
    public void CoreDependsOnNoOtherProjectAndNotOnWpf()
    {
        var core = Load("src/M1vClock.Core/M1vClock.Core.csproj");

        Assert.Empty(References(core));
        Assert.DoesNotContain("UseWPF", core.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Hardware")]
    [InlineData("Benchmarks")]
    [InlineData("Tuning")]
    [InlineData("Optimize")]
    [InlineData("Diagnostics")]
    [InlineData("Games")]
    [InlineData("Integrations")]
    [InlineData("Maker")]
    public void ModulesDependOnlyOnCore(string module)
    {
        var project = Load($"src/M1vClock.{module}/M1vClock.{module}.csproj");

        Assert.Equal(["M1vClock.Core.csproj"], References(project));
    }

    [Fact]
    public void AppReferencesCoreAndEveryModule()
    {
        var references = References(Load("src/M1vClock.App/M1vClock.App.csproj"));

        Assert.Contains("M1vClock.Core.csproj", references);
        foreach (var module in Modules)
        {
            Assert.Contains($"M1vClock.{module}.csproj", references);
        }
    }

    [Fact]
    public void ProjectsNeverPinPackageVersionsTheyOwn()
    {
        var offenders = SourceTree.Files(".", "*.csproj")
            .Where(file => Load(SourceTree.Relative(file)).Descendants("PackageReference").Any(p => p.Attribute("Version") is not null))
            .Select(SourceTree.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "versão fora do Directory.Packages.props: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryCentralPackageVersionIsExact()
    {
        var versions = Load("Directory.Packages.props").Descendants("PackageVersion")
            .Select(p => (Name: p.Attribute("Include")!.Value, Version: p.Attribute("Version")!.Value))
            .ToList();

        Assert.NotEmpty(versions);
        Assert.All(versions, p => Assert.True(ExactVersion().IsMatch(p.Version), $"{p.Name} usa versão flutuante: {p.Version}"));
    }

    [Fact]
    public void BuildIsStrictForEveryProject()
    {
        var props = SourceTree.Read("Directory.Build.props");

        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", props, StringComparison.Ordinal);
        Assert.Contains("<Nullable>enable</Nullable>", props, StringComparison.Ordinal);
        Assert.Contains("<AnalysisLevel>latest-recommended</AnalysisLevel>", props, StringComparison.Ordinal);
        Assert.Contains("<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>", props, StringComparison.Ordinal);
    }

    [Fact]
    public void SolutionListsEveryProjectOnDiskAndNothingElse()
    {
        var listed = Load("m1vClock.slnx").Descendants("Project")
            .Select(p => p.Attribute("Path")!.Value.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        var onDisk = SourceTree.Files("src", "*.csproj").Concat(SourceTree.Files("tests", "*.csproj"))
            .Select(SourceTree.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(onDisk, listed);
    }

    private static XDocument Load(string relativePath) => XDocument.Parse(SourceTree.Read(relativePath));

    private static List<string> References(XDocument project) =>
        project.Descendants("ProjectReference")
            .Select(r => Path.GetFileName(r.Attribute("Include")!.Value.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToList();
}
