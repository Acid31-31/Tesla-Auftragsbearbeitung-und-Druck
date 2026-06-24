param(
    [string]$CertPath = ".\cert\DOK-CodeSigning.cer"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not (Test-Path $CertPath)) {
    throw "Zertifikat nicht gefunden: $CertPath"
}

$certutil = Get-Command certutil.exe -ErrorAction SilentlyContinue
if ($null -eq $certutil) {
    throw "certutil.exe nicht gefunden."
}

& certutil.exe -addstore -user Root $CertPath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Import in Root fehlgeschlagen (ExitCode $LASTEXITCODE)."
}

& certutil.exe -addstore -user TrustedPublisher $CertPath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Import in TrustedPublisher fehlgeschlagen (ExitCode $LASTEXITCODE)."
}

Write-Host "Zertifikat für aktuellen Benutzer importiert (Root + TrustedPublisher)."
Write-Host "Windows und Antivirus sollten signierte EXEs jetzt eher akzeptieren."
