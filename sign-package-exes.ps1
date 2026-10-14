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
