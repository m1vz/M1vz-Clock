using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace M1vClock.Architecture.Tests;

public sealed class SecurityRuleTests
{
    private static readonly string[] ProcessLaunchers =
    [
        "src/M1vClock.Core/Processes/ProcessRunner.cs",
        "src/M1vClock.Core/Shell/ShellLauncher.cs",
    ];

    private static readonly string[] TelemetryNames =
    [
        "telemetry", "applicationinsights", "sentry", "appcenter", "mixpanel", "segment", "datadog", "newrelic", "amplitude", "google.analytics",
    ];

    [Fact]
    public void NoStringLiteralPointsToPlainHttp()
    {
        var found = new List<string>();
        foreach (var file in SourceTree.CSharpFiles("src"))
        {
            foreach (var token in SourceTree.ParseRoot(file).DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken)))
            {
                if (token.ValueText.Contains("http://", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(SourceTree.Relative(file));
                }
            }
        }

        Assert.True(found.Count == 0, "endereço http:// (só HTTPS é permitido): " + string.Join(", ", found));
    }

    [Fact]
    public void WorkingSetIsNeverEmptied()
    {
        var found = SourceTree.CSharpFiles("src")
            .Where(file => File.ReadAllText(file).Contains("EmptyWorkingSet", StringComparison.OrdinalIgnoreCase))
            .Select(SourceTree.Relative)
            .ToList();

        Assert.True(found.Count == 0, "EmptyWorkingSet é proibido: " + string.Join(", ", found));
    }

    [Fact]
    public void ExternalProcessesAreOnlyStartedByTheTwoApprovedClasses()
    {
        var offenders = SourceTree.CSharpFiles("src")
            .Where(file => !ProcessLaunchers.Contains(SourceTree.Relative(file), StringComparer.Ordinal))
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("Process.Start(", StringComparison.Ordinal) || text.Contains("new Process(", StringComparison.Ordinal);
            })
            .Select(SourceTree.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "processo iniciado fora de ProcessRunner/ShellLauncher: " + string.Join(", ", offenders));
    }

    [Fact]
    public void ProcessRunnerNeverUsesTheShell()
    {
        var text = SourceTree.Read("src/M1vClock.Core/Processes/ProcessRunner.cs");

        Assert.Contains("UseShellExecute = false", text, StringComparison.Ordinal);
        Assert.Contains("ArgumentList", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryProjectWithNativeImportsRestrictsTheDllSearchPath()
    {
        var offenders = new List<string>();
        foreach (var project in Directory.EnumerateDirectories(Path.Combine(SourceTree.Root, "src")))
        {
            var relative = SourceTree.Relative(project);
            var files = SourceTree.CSharpFiles(relative);
            var importsNative = files.Any(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("[LibraryImport(", StringComparison.Ordinal) || text.Contains("[DllImport(", StringComparison.Ordinal);
            });
            var restricted = files.Any(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]", StringComparison.Ordinal);
            });
            if (importsNative && !restricted)
            {
                offenders.Add(relative);
            }
        }

        Assert.True(offenders.Count == 0, "P/Invoke sem restringir a busca de DLL a System32: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NoTelemetryPackageIsReferenced()
    {
        var packages = SourceTree.Read("Directory.Packages.props").ToUpperInvariant();

        var found = TelemetryNames.Where(name => packages.Contains(name.ToUpperInvariant(), StringComparison.Ordinal)).ToList();

        Assert.True(found.Count == 0, "pacote de telemetria: " + string.Join(", ", found));
    }

    [Fact]
    public void OnlyNuGetOrgIsAPackageSource()
    {
        var config = SourceTree.Read("NuGet.config");

        Assert.Contains("<clear />", config, StringComparison.Ordinal);
        Assert.Contains("https://api.nuget.org/v3/index.json", config, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", config, StringComparison.OrdinalIgnoreCase);
    }
}
