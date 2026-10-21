using M1vClock.Core.Paths;
using M1vClock.Core.Security;

namespace M1vClock.Core.Tests;

public sealed class SecurityTests
{
    private static readonly string DotnetHost = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");

    [Fact]
    public void SignedFileFromExpectedPublisherIsTrusted()
    {
        Assert.SkipUnless(File.Exists(DotnetHost), "dotnet.exe não está na pasta padrão.");

        var result = new TrustVerifier().Verify(DotnetHost, new ExpectedPublisher(".NET", "Microsoft Corporation"));

        Assert.Equal(TrustStatus.Trusted, result.Status);
    }

    [Fact]
    public void SignedFileFromAnotherPublisherIsRejected()
    {
        Assert.SkipUnless(File.Exists(DotnetHost), "dotnet.exe não está na pasta padrão.");

        var result = new TrustVerifier().Verify(DotnetHost, ExpectedPublisher.Corsair);

        Assert.Equal(TrustStatus.UnexpectedPublisher, result.Status);
        Assert.Equal("Microsoft Corporation", result.Organization);
    }

    [Fact]
    public void PublisherMustMatchExactlyNotJustContain()
    {
        Assert.SkipUnless(File.Exists(DotnetHost), "dotnet.exe não está na pasta padrão.");

        var result = new TrustVerifier().Verify(DotnetHost, new ExpectedPublisher(".NE", "Microsoft"));

        Assert.Equal(TrustStatus.UnexpectedPublisher, result.Status);
    }

    [Fact]
    public void UnsignedFileIsReportedAsNotSigned()
    {
        using var temp = new TempDirectory();
        var path = temp.File("sem-assinatura.dll");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0, 0, 0, 0]);

        var result = new TrustVerifier().Verify(path, ExpectedPublisher.Corsair);

        Assert.False(result.IsTrusted);
        Assert.Equal(TrustStatus.NotSigned, result.Status);
    }

    [Fact]
    public void SecretRoundTripsWithDpapi()
    {
        var protectedValue = SecretProtector.Protect("chave-secreta");

        Assert.DoesNotContain("chave-secreta", protectedValue, StringComparison.Ordinal);
        Assert.Equal("chave-secreta", SecretProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void DevelopmentPathsCreateFoldersInsideTheRoot()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.File("raiz"), isHardened: false);

        var logs = paths.EnsureDirectory(paths.Logs);

        Assert.True(Directory.Exists(logs));
        Assert.StartsWith(paths.Root, logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PathsOutsideTheRootAreRejected()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.File("raiz"), isHardened: false);

        Assert.Throws<ArgumentException>(() => paths.EnsureDirectory(temp.File("fora")));
        Assert.Throws<ArgumentException>(() => paths.EnsureDirectory(Path.Combine(paths.Root, "..", "fora")));
        Assert.Throws<ArgumentException>(() => paths.EnsureDirectory(paths.Root + "2"));
    }

    [Fact]
    public void UserDirectoryRejectsPathCharacters()
    {
        var paths = new AppPaths(@"C:\ProgramData\m1vClock", isHardened: false);

        Assert.Throws<ArgumentException>(() => paths.UserDirectory(@"..\..\Windows"));
    }

    [Fact]
    public void HardenedDirectoryIsOwnedByAdministratorsAndNotWritableByUsers()
    {
        Assert.SkipUnless(ElevationInfo.Current().IsElevated, "Precisa de administrador.");
        using var temp = new TempDirectory();
        var target = temp.File("protegida");

        var status = HardenedDirectory.Ensure(target);

        Assert.Equal(HardenedDirectoryStatus.Created, status);
        Assert.True(HardenedDirectory.IsTrusted(new DirectoryInfo(target)));
        Assert.Equal(HardenedDirectoryStatus.Verified, HardenedDirectory.Ensure(target));
    }

    [Fact]
    public void UserCreatedDirectoryIsQuarantinedAndRecreated()
    {
        Assert.SkipUnless(ElevationInfo.Current().IsElevated, "Precisa de administrador.");
        using var temp = new TempDirectory();
        var target = temp.File("plantada");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "plantado.txt"), "x");

        var status = HardenedDirectory.Ensure(target);

        Assert.Equal(HardenedDirectoryStatus.Quarantined, status);
        Assert.Empty(Directory.GetFiles(target));
        Assert.Single(Directory.GetDirectories(temp.Path, "plantada.untrusted-*"));
    }
}
