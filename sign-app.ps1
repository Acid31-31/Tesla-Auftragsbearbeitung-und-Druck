param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$CertPath = ".\cert\DOK-CodeSigning.pfx",
    [string]$CertPasswordFile = ".\cert\signing-password.txt",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$exePath = Join-Path $root "bin\$Configuration\DOK-V01.exe"
if (-not (Test-Path $exePath)) {
    throw "EXE nicht gefunden: $exePath`nBitte zuerst bauen."
}

if (-not (Test-Path $CertPath)) {
    throw "Zertifikat nicht gefunden: $CertPath`nBitte zuerst create-selfsigned-cert.ps1 ausführen."
}

if ([string]::IsNullOrWhiteSpace($CertPassword) -and (Test-Path $CertPasswordFile)) {
    $CertPassword = (Get-Content $CertPasswordFile -Raw).Trim()
}

if ([string]::IsNullOrWhiteSpace($CertPassword)) {
    throw "Kein Zertifikat-Passwort. Parameter -CertPassword oder Datei $CertPasswordFile angeben."
}

$signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($signtool)) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if ([string]::IsNullOrWhiteSpace($signtool)) {
    throw "signtool.exe nicht gefunden. Windows SDK / Visual Studio Build Tools installieren."
}

Write-Host "Signiere: $exePath"
& $signtool sign /fd SHA256 /f $CertPath /p $CertPassword /tr http://timestamp.digicert.com /td SHA256 $exePath
if ($LASTEXITCODE -ne 0) {
    throw "Signieren fehlgeschlagen (ExitCode $LASTEXITCODE)."
}

Write-Host "Signatur prüfen..."
& $signtool verify /pa /v $exePath
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Signatur ist vorhanden, aber das selbstsignierte Zertifikat ist auf diesem PC noch nicht vertrauenswürdig."
    Write-Warning "Das Zertifikat wird beim ersten Start der App automatisch installiert."
}
else {
    Write-Host "Signatur verifiziert."
}

Write-Host "Erfolgreich signiert ($Configuration)."
