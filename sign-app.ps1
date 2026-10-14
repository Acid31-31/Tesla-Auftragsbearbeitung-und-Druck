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

function Find-SignTool {
    $existing = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($existing -and -not [string]::IsNullOrWhiteSpace($existing.Source) -and (Test-Path -LiteralPath $existing.Source)) {
        return $existing.Source
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    $pf86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    if (-not [string]::IsNullOrWhiteSpace($pf86)) {
        $kitBin = Join-Path $pf86 "Windows Kits\10\bin"
        if (Test-Path -LiteralPath $kitBin) {
            Get-ChildItem -LiteralPath $kitBin -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                $candidates.Add((Join-Path $_.FullName "x64\signtool.exe"))
            }
        }
        $candidates.Add((Join-Path $pf86 "Windows Kits\10\App Certification Kit\signtool.exe"))
        $candidates.Add((Join-Path $pf86 "Microsoft SDKs\ClickOnce\SignTool\signtool.exe"))
    }

    $candidates.Add("C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe")

    foreach ($p in ($candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Descending)) {
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

$signtool = Find-SignTool
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

$exportedCer = Join-Path (Split-Path $exePath -Parent) "CodeSigning.cer"
try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertPath, $CertPassword)
    [System.IO.File]::WriteAllBytes($exportedCer, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    Copy-Item $exportedCer (Join-Path $root "CodeSigning.cer") -Force
    Write-Host "CodeSigning.cer exportiert: $exportedCer"
}
catch {
    Write-Warning "CodeSigning.cer konnte nicht exportiert werden: $_"
}
