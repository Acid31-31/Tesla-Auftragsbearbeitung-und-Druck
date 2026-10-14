param(
    [string]$Subject = "CN=Alexander Hoelzer, O=Alexander Hoelzer, L=Kruft, C=DE",
    [string]$FriendlyName = "Alexander Hoelzer Code Signing",
    [string]$OutputDir = ".\cert",
    [int]$ValidYears = 3
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$archiveDir = Join-Path $OutputDir "archive"
if (-not (Test-Path $archiveDir)) {
    New-Item -ItemType Directory -Path $archiveDir | Out-Null
}

$pfxPath = Join-Path $OutputDir "DOK-CodeSigning.pfx"
$cerPath = Join-Path $OutputDir "DOK-CodeSigning.cer"
$passwordPath = Join-Path $OutputDir "signing-password.txt"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

foreach ($oldFile in @($pfxPath, $cerPath)) {
    if (Test-Path $oldFile) {
        $archiveName = [IO.Path]::GetFileNameWithoutExtension($oldFile) + "_$timestamp" + [IO.Path]::GetExtension($oldFile)
        Move-Item $oldFile (Join-Path $archiveDir $archiveName) -Force
        Write-Host "Altes Zertifikat archiviert: $archiveName"
    }
}

Write-Host "Entferne alte Code-Signing-Zertifikate aus dem Benutzer-Speicher..."
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Subject -like "*Tesla*" -or
        $_.FriendlyName -like "*Tesla*" -or
        $_.Subject -like "*Alexander Hoelzer*" -or $_.Subject -like "*Alexander H*lzer*"
    } |
    ForEach-Object {
        Write-Host "  Entferne: $($_.Subject) ($($_.Thumbprint))"
        Remove-Item $_.PSPath -Force
    }

$password = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ }) + "!9"
Set-Content -Path $passwordPath -Value $password -Encoding UTF8 -NoNewline
Write-Host "Neues Passwort gespeichert in: $passwordPath (nur lokal, nicht in Git)"

Write-Host "Erzeuge neues selbstsigniertes Code-Signing-Zertifikat..."
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -FriendlyName $FriendlyName `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($ValidYears)

$securePwd = ConvertTo-SecureString $password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePwd | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

$deployCerPath = Join-Path $root "CodeSigning.cer"
Copy-Item $cerPath $deployCerPath -Force
Write-Host "Deploy-Zertifikat: $deployCerPath"

Write-Host ""
Write-Host "Fertig."
Write-Host "PFX:      $pfxPath"
Write-Host "CER:      $cerPath"
Write-Host "Passwort: $passwordPath"
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host ""
Write-Host "App signieren:"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\sign-app.ps1 -Configuration Debug"
Write-Host "USB-Version mit Signatur:"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\create-usb-version.ps1 -Configuration Release"
