param(
    [string]$PackagesDirectory = "packages"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$nugetExe = (Get-Command nuget.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($nugetExe)) {
    $nugetExe = Join-Path $env:TEMP "nuget.exe"
    if (-not (Test-Path $nugetExe)) {
        Write-Host "Lade nuget.exe..."
        Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nugetExe
    }
}

$packagesConfig = Join-Path $root "packages.config"
if (-not (Test-Path $packagesConfig)) {
    throw "packages.config nicht gefunden."
}

New-Item -ItemType Directory -Path (Join-Path $root $PackagesDirectory) -Force | Out-Null
& $nugetExe restore $packagesConfig -PackagesDirectory (Join-Path $root $PackagesDirectory)
if ($LASTEXITCODE -ne 0) {
    throw "NuGet-Restore fehlgeschlagen."
}

Write-Host "NuGet-Pakete wiederhergestellt."
