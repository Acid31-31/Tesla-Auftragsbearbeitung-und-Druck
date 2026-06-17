param(
	[string]$Subject = "CN=Tesla Auftragsbearbeitung und Druck",
	[string]$PfxPassword = "Tesla123!ChangeMe",
	[string]$OutputDir = ".\cert"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not (Test-Path $OutputDir)) {
	New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$pfxPath = Join-Path $OutputDir "Tesla-CodeSigning.pfx"
$cerPath = Join-Path $OutputDir "Tesla-CodeSigning.cer"

Write-Host "Erzeuge selbstsigniertes Code-Signing-Zertifikat..."
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(3)

$securePwd = ConvertTo-SecureString $PfxPassword -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePwd | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

Write-Host "Fertig."
Write-Host "PFX: $pfxPath"
Write-Host "CER: $cerPath"
Write-Host "\nUSB-Build mit Signatur:" 
Write-Host "powershell -ExecutionPolicy Bypass -File .\create-usb-version.ps1 -Configuration Release -CertPath \"$pfxPath\" -CertPassword \"$PfxPassword\" -CertPublicPath \"$cerPath\""
