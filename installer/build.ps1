param(
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$props = [xml](Get-Content (Join-Path $repo 'Directory.Build.props'))
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Versão não encontrada no Directory.Build.props.' }

$publish = Join-Path $repo 'artifacts\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "Publicando m1vClock $version (self-contained, win-x64)..."
dotnet publish (Join-Path $repo 'src\M1vClock.App\M1vClock.App.csproj') -c $Configuration -r win-x64 --self-contained true -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($LASTEXITCODE)." }

Write-Host 'Baixando o Inno Setup (pacote NuGet, só para o build)...'
dotnet restore (Join-Path $PSScriptRoot 'tools.proj') -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet restore falhou ($LASTEXITCODE)." }
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$iscc = Join-Path $packages 'tools.innosetup\6.7.1\tools\ISCC.exe'
if (-not (Test-Path $iscc)) { throw "ISCC.exe não encontrado em $iscc." }

Write-Host 'Montando o instalador...'
& $iscc "/DAppVersion=$version" "/DPublishDir=$publish" "/DRepoRoot=$repo" (Join-Path $PSScriptRoot 'm1vClock.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC falhou ($LASTEXITCODE)." }

$setup = Join-Path $repo "artifacts\installer\m1vClock-Setup-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Set-Content -Path "$setup.sha256" -Value "$hash  m1vClock-Setup-$version.exe" -Encoding ascii
Write-Host "Pronto: $setup"
Write-Host "SHA-256: $hash"
