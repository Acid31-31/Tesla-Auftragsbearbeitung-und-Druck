param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [Parameter(Mandatory = $true)]
    [string]$CertPath,
    [string]$CertPassword = "",
    [string]$CertPasswordFile = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

if ([string]::IsNullOrWhiteSpace($CertPassword) -and -not [string]::IsNullOrWhiteSpace($CertPasswordFile) -and (Test-Path $CertPasswordFile)) {
    $CertPassword = (Get-Content $CertPasswordFile -Raw).Trim()
}

if (-not (Test-Path $CertPath)) {
    throw "Händler-Zertifikat nicht gefunden: $CertPath"
}

if ([string]::IsNullOrWhiteSpace($CertPassword)) {
    throw "Zertifikat-Passwort fehlt (-CertPassword oder -CertPasswordFile)."
}

$exePath = Join-Path $root "bin\$Configuration\DOK-V01.exe"
if (-not (Test-Path $exePath)) {
    & (Join-Path $root "PDF-umbenenen.csproj") | Out-Null
    & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" (Join-Path $root "PDF-umbenenen.csproj") /t:Build /p:Configuration=$Configuration /p:Platform=AnyCPU
    if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen." }
}

$signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($signtool)) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($signtool)) {
    throw "signtool.exe nicht gefunden."
}

Write-Host "Signiere mit Händler-Zertifikat: $CertPath"
& $signtool sign /fd SHA256 /f $CertPath /p $CertPassword /tr http://timestamp.digicert.com /td SHA256 $exePath
if ($LASTEXITCODE -ne 0) { throw "Signieren fehlgeschlagen." }

& $signtool verify /pa /v $exePath
Write-Host "Fertig. Anschließend: .\create-usb-version.ps1 -Configuration $Configuration"
