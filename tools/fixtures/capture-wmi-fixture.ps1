param(
    [Parameter(Mandatory = $true)][string]$OutDirectory,
    [string]$Name = "desktop"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Management

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Rode como administrador: os contadores de disco e a previsão de falha só aparecem assim."
}

$sensitiveNames = @("SerialNumber", "UniqueId", "RegisteredUser", "Owner", "PrimaryOwnerName", "PrimaryOwnerContact", "UserName", "CSName", "PartNumber")
$computer = $env:COMPUTERNAME
$user = $env:USERNAME

function ConvertValue($value) {
    if ($null -eq $value) { return $null }
    if ($value -is [System.Array]) { return @($value | ForEach-Object { ConvertValue $_ }) }
    if ($value -is [System.Management.ManagementBaseObject]) { return (ConvertObject $value) }
    if ($value -is [string]) {
        $text = $value
        if ($computer) { $text = $text -replace [regex]::Escape($computer), "PC-EXEMPLO" }
        if ($user) { $text = $text -replace [regex]::Escape($user), "usuario" }
        return $text
    }
    return $value
}

function ConvertObject($object) {
    $row = [ordered]@{}
    foreach ($property in $object.Properties) {
        if ($property.Name.StartsWith("__")) { continue }
        if ($sensitiveNames -contains $property.Name -and $property.Value) {
            $row[$property.Name] = "EXEMPLO-" + $property.Name.ToUpperInvariant()
        } else {
            $row[$property.Name] = ConvertValue $property.Value
        }
    }
    return $row
}

function ReadClass($scope, $className, $where) {
    $query = "SELECT * FROM $className"
    if ($where) { $query += " WHERE $where" }
    $searcher = New-Object System.Management.ManagementObjectSearcher($scope, $query)
    $rows = @()
    try {
        foreach ($item in $searcher.Get()) { $rows += , (ConvertObject $item) }
    } catch {
        Write-Warning "$scope $className : $($_.Exception.Message)"
    }
    return [ordered]@{ scope = $scope; class = $className; rows = $rows }
}

function ReadRelated($scope, $sourceClass, $relatedClass) {
    $searcher = New-Object System.Management.ManagementObjectSearcher($scope, "SELECT * FROM $sourceClass")
    $items = @()
    foreach ($source in $searcher.Get()) {
        $related = @()
        foreach ($other in $source.GetRelated($relatedClass)) { $related += , (ConvertObject $other) }
        $items += , ([ordered]@{ source = (ConvertObject $source); related = $related })
    }
    return [ordered]@{ scope = $scope; source = $sourceClass; relatedClass = $relatedClass; items = $items }
}

$classes = @(
    (ReadClass "root\cimv2" "Win32_OperatingSystem"),
    (ReadClass "root\cimv2" "Win32_ComputerSystem"),
    (ReadClass "root\cimv2" "Win32_Processor"),
    (ReadClass "root\cimv2" "Win32_BaseBoard"),
    (ReadClass "root\cimv2" "Win32_BIOS"),
    (ReadClass "root\cimv2" "Win32_PhysicalMemory"),
    (ReadClass "root\cimv2" "Win32_PhysicalMemoryArray"),
    (ReadClass "root\cimv2" "Win32_VideoController"),
    (ReadClass "root\cimv2" "Win32_PnPSignedDriver" "DeviceClass = 'DISPLAY'"),
    (ReadClass "root\cimv2" "Win32_DiskDrive"),
    (ReadClass "root\cimv2" "Win32_LogicalDisk"),
    (ReadClass "root\cimv2" "Win32_DiskDriveToDiskPartition"),
    (ReadClass "root\cimv2" "Win32_LogicalDiskToPartition"),
    (ReadClass "root\cimv2" "Win32_PnPEntity"),
    (ReadClass "root\cimv2" "Win32_Battery"),
    (ReadClass "root\Microsoft\Windows\Storage" "MSFT_PhysicalDisk"),
    (ReadClass "root\wmi" "MSStorageDriver_FailurePredictStatus")
)
$related = @((ReadRelated "root\Microsoft\Windows\Storage" "MSFT_PhysicalDisk" "MSFT_StorageReliabilityCounter"))

$document = [ordered]@{
    capturedAt = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    note = "Saídas reais do WMI deste PC, capturadas como administrador por tools/fixtures/capture-wmi-fixture.ps1. Nome do computador, usuário e números de série foram trocados por valores de exemplo."
    classes = $classes
    related = $related
}

$cmdlet = @()
foreach ($disk in (Get-PhysicalDisk)) {
    $counter = $disk | Get-StorageReliabilityCounter
    $cmdlet += , ([ordered]@{
        diskNumber = $disk.DeviceId
        friendlyName = $disk.FriendlyName
        mediaType = [string]$disk.MediaType
        busType = [string]$disk.BusType
        temperature = $counter.Temperature
        temperatureMax = $counter.TemperatureMax
        wear = $counter.Wear
        powerOnHours = $counter.PowerOnHours
        readErrorsTotal = $counter.ReadErrorsTotal
        writeErrorsTotal = $counter.WriteErrorsTotal
        startStopCycleCount = $counter.StartStopCycleCount
    })
}

New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $OutDirectory "wmi-$Name.json"), ($document | ConvertTo-Json -Depth 12), $utf8)
[System.IO.File]::WriteAllText((Join-Path $OutDirectory "get-storagereliabilitycounter-$Name.json"), ($cmdlet | ConvertTo-Json -Depth 4), $utf8)
Write-Output "Gravado em $OutDirectory"
