$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $scriptDir 'build.ps1')
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exe = Resolve-Path (Join-Path $scriptDir '..\bin\Debug\DOK-V01.exe')
$dir = Split-Path $exe
Start-Process -FilePath $exe -WorkingDirectory $dir
Write-Host "DOK-V01 gestartet."
