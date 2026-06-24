param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string]$IconPath = ".\Assets\AppIcon.ico"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$exePathFull = (Resolve-Path $ExePath).Path
$iconPathFull = (Resolve-Path $IconPath).Path
$toolsDir = Join-Path $root "Tools"
$injectorExe = Join-Path $toolsDir "IconInjector.exe"
$injectorSource = Join-Path $toolsDir "IconInjector.cs"

if (-not (Test-Path $injectorSource)) {
    throw "IconInjector.cs nicht gefunden: $injectorSource"
}

$sourceTime = (Get-Item $injectorSource).LastWriteTimeUtc
$needsBuild = -not (Test-Path $injectorExe)
if (-not $needsBuild) {
    $needsBuild = (Get-Item $injectorExe).LastWriteTimeUtc -lt $sourceTime
}

if ($needsBuild) {
    $csc = Join-Path ${env:WINDIR} "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) {
        $csc = Join-Path ${env:WINDIR} "Microsoft.NET\Framework\v4.0.30319\csc.exe"
    }
    if (-not (Test-Path $csc)) {
        throw "csc.exe nicht gefunden."
    }

    & $csc /nologo /optimize+ /out:$injectorExe $injectorSource
    if ($LASTEXITCODE -ne 0) {
        throw "IconInjector Build fehlgeschlagen."
    }
}

& $injectorExe $exePathFull $iconPathFull
if ($LASTEXITCODE -ne 0) {
    throw "Icon konnte nicht gesetzt werden: $exePathFull"
}
