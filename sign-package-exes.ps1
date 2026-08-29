param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [string]$CertPath = ".\cert\DOK-CodeSigning.pfx",
    [string]$CertPasswordFile = ".\cert\signing-password.txt",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not (Test-Path $PackageRoot)) {
    throw "Paketordner nicht gefunden: $PackageRoot"
}

if (-not (Test-Path $CertPath)) {
    Write-Warning "Zertifikat fehlt - Paket-EXEs werden nicht signiert."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($CertPassword) -and (Test-Path $CertPasswordFile)) {
    $CertPassword = (Get-Content $CertPasswordFile -Raw).Trim()
}

if ([string]::IsNullOrWhiteSpace($CertPassword)) {
    throw "Kein Zertifikat-Passwort fuer sign-package-exes.ps1."
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

$executables = Get-ChildItem $PackageRoot -Filter *.exe -Recurse -File | Sort-Object FullName
if ($executables.Count -eq 0) {
    Write-Host "Keine EXE-Dateien in $PackageRoot"
    exit 0
}

Write-Host "Signiere $($executables.Count) EXE(s) in $PackageRoot ..."
foreach ($exe in $executables) {
    Write-Host "  -> $($exe.Name)"
    $signed = $false
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        & $signtool sign /fd SHA256 /f $CertPath /p $CertPassword /tr http://timestamp.digicert.com /td SHA256 $exe.FullName
        if ($LASTEXITCODE -eq 0) {
            $signed = $true
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $signed) {
        throw "Signieren fehlgeschlagen: $($exe.FullName)"
    }
}

Write-Host "Alle Paket-EXEs signiert."
