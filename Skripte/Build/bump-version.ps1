param(
    [int]$Build = 0,
    [string]$VersionLabel = "1.1",
    [string]$BuildDate = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

$assemblyInfoPath = Join-Path $root "Properties\AssemblyInfo.cs"
$appInfoPath = Join-Path $root "AppInfo.cs"

$assemblyInfo = Get-Content $assemblyInfoPath -Raw
if ($assemblyInfo -notmatch 'AssemblyVersion\("(\d+)\.(\d+)\.0\.(\d+)"\)') {
    throw "AssemblyVersion in AssemblyInfo.cs nicht gefunden."
}

$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$currentBuild = [int]$Matches[3]
$newBuild = if ($Build -gt 0) { $Build } else { $currentBuild + 1 }

if ([string]::IsNullOrWhiteSpace($BuildDate)) {
    $BuildDate = (Get-Date).ToString("dd.MM.yyyy")
}

$revisionLabel = "R$newBuild"
$assemblyVersion = "$major.$minor.0.$newBuild"
$informationalVersion = "$VersionLabel $revisionLabel"

$assemblyInfo = [regex]::Replace(
    $assemblyInfo,
    '\[assembly: AssemblyVersion\("[^"]+"\)\]',
    "[assembly: AssemblyVersion(`"$assemblyVersion`")]")
$assemblyInfo = [regex]::Replace(
    $assemblyInfo,
    '\[assembly: AssemblyFileVersion\("[^"]+"\)\]',
    "[assembly: AssemblyFileVersion(`"$assemblyVersion`")]")
$assemblyInfo = [regex]::Replace(
    $assemblyInfo,
    '\[assembly: AssemblyInformationalVersion\("[^"]+"\)\]',
    "[assembly: AssemblyInformationalVersion(`"$informationalVersion`")]")
Set-Content -Path $assemblyInfoPath -Value $assemblyInfo -Encoding UTF8

$appInfo = Get-Content $appInfoPath -Raw
$appInfo = [regex]::Replace($appInfo, 'public const string VersionLabel = "[^"]+";', "public const string VersionLabel = `"$VersionLabel`";")
$appInfo = [regex]::Replace($appInfo, 'public const string RevisionLabel = "[^"]+";', "public const string RevisionLabel = `"$revisionLabel`";")
$appInfo = [regex]::Replace($appInfo, 'public const string BuildDate = "[^"]+";', "public const string BuildDate = `"$BuildDate`";")
$appInfo = [regex]::Replace($appInfo, 'public const int VersionBuild = \d+;', "public const int VersionBuild = $newBuild;")
Set-Content -Path $appInfoPath -Value $appInfo -Encoding UTF8

Write-Host "Version aktualisiert: $informationalVersion ($assemblyVersion)"
Write-Host "Release-Tag: v$VersionLabel-$revisionLabel"
