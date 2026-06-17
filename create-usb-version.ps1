param(
	[string]$Configuration = "Release",
	[string]$CertPath = "",
	[string]$CertPassword = "",
	[string]$CertPublicPath = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$projectFile = Join-Path $root "PDF-umbenenen.csproj"
$msbuild = "msbuild"

Write-Host "[1/5] Build ($Configuration) startet..."
& $msbuild $projectFile /t:Rebuild /p:Configuration=$Configuration /p:Platform="AnyCPU"
if ($LASTEXITCODE -ne 0) {
	throw "Build fehlgeschlagen."
}

$releaseDir = Join-Path $root "bin\$Configuration"
if (-not (Test-Path $releaseDir)) {
	throw "Build-Ausgabe nicht gefunden: $releaseDir"
}

$usbRoot = Join-Path $root "USB-Version"
$appFolder = Join-Path $usbRoot "Tesla-Auftragsbearbeitung-und-Druck"

Write-Host "[2/5] Zielordner wird vorbereitet..."
if (Test-Path $appFolder) {
	Remove-Item $appFolder -Recurse -Force
}
New-Item -ItemType Directory -Path $appFolder | Out-Null

Write-Host "[3/5] Dateien werden kopiert..."
Get-ChildItem $releaseDir -File | Where-Object {
	$_.Extension -in @('.exe', '.dll', '.config')
} | ForEach-Object {
	Copy-Item $_.FullName -Destination (Join-Path $appFolder $_.Name) -Force
}

$exeName = "Tesla-Auftragsbearbeitung-und-Druck.exe"
$exePath = Join-Path $appFolder $exeName

if ((Test-Path $CertPath) -and (Test-Path $exePath)) {
    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if (-not [string]::IsNullOrWhiteSpace($signtool)) {
        Write-Host "[3b/5] Signiere EXE mit Zertifikat..."
        $args = @("sign", "/fd", "SHA256", "/f", $CertPath)
        if (-not [string]::IsNullOrWhiteSpace($CertPassword)) {
            $args += @("/p", $CertPassword)
        }
        $args += @("/tr", "http://timestamp.digicert.com", "/td", "SHA256", $exePath)

        & $signtool @args
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Signieren fehlgeschlagen (ExitCode $LASTEXITCODE). USB-Version wird trotzdem erstellt."
        }
    }
    else {
        Write-Warning "signtool.exe nicht gefunden. EXE bleibt unsigniert."
    }
}

if (Test-Path $CertPublicPath) {
    Copy-Item $CertPublicPath (Join-Path $appFolder "CodeSigning.cer") -Force
}

$certInstallPs1 = @"
`$ErrorActionPreference = 'Stop'
`$certFile = Join-Path (Split-Path -Parent `$MyInvocation.MyCommand.Path) 'CodeSigning.cer'
if (-not (Test-Path `$certFile)) {
    throw 'CodeSigning.cer wurde nicht gefunden.'
}

Import-Certificate -FilePath `$certFile -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
Import-Certificate -FilePath `$certFile -CertStoreLocation 'Cert:\CurrentUser\TrustedPublisher' | Out-Null
Write-Host 'Zertifikat für aktuellen Benutzer importiert.'
"@
Set-Content -Path (Join-Path $appFolder "ZERTIFIKAT_INSTALLIEREN.ps1") -Value $certInstallPs1 -Encoding UTF8

$certInstallBat = @"
@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0ZERTIFIKAT_INSTALLIEREN.ps1"
pause
"@
Set-Content -Path (Join-Path $appFolder "ZERTIFIKAT_INSTALLIEREN.bat") -Value $certInstallBat -Encoding ASCII

$startBat = @"
@echo off
cd /d "%~dp0"
start "" "$exeName"
"@
Set-Content -Path (Join-Path $appFolder "STARTEN.bat") -Value $startBat -Encoding ASCII

$installPs1 = @"
`$ErrorActionPreference = 'Stop'
`$source = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$target = Join-Path `$env:LOCALAPPDATA 'Tesla-Auftragsbearbeitung-und-Druck'

if (Test-Path `$target) { Remove-Item `$target -Recurse -Force }
New-Item -ItemType Directory -Path `$target | Out-Null
Copy-Item (Join-Path `$source '*') `$target -Recurse -Force

`$desktop = [Environment]::GetFolderPath('Desktop')
`$shortcutPath = Join-Path `$desktop 'Tesla Auftragsbearbeitung & Druck.lnk'
`$shell = New-Object -ComObject WScript.Shell
`$shortcut = `$shell.CreateShortcut(`$shortcutPath)
`$shortcut.TargetPath = Join-Path `$target '$exeName'
`$shortcut.WorkingDirectory = `$target
`$shortcut.Save()

Write-Host 'Installation abgeschlossen.'
Write-Host "Installationsordner: `$target"
"@
Set-Content -Path (Join-Path $appFolder "Installieren.ps1") -Value $installPs1 -Encoding UTF8

$installBat = @"
@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0Installieren.ps1"
pause
"@
Set-Content -Path (Join-Path $appFolder "INSTALLIEREN.bat") -Value $installBat -Encoding ASCII

$uninstallPs1 = @"
`$ErrorActionPreference = 'SilentlyContinue'
`$target = Join-Path `$env:LOCALAPPDATA 'Tesla-Auftragsbearbeitung-und-Druck'
`$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Tesla Auftragsbearbeitung & Druck.lnk'
if (Test-Path `$shortcutPath) { Remove-Item `$shortcutPath -Force }
if (Test-Path `$target) { Remove-Item `$target -Recurse -Force }
Write-Host 'Deinstallation abgeschlossen.'
"@
Set-Content -Path (Join-Path $appFolder "Deinstallieren.ps1") -Value $uninstallPs1 -Encoding UTF8

$uninstallBat = @"
@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0Deinstallieren.ps1"
pause
"@
Set-Content -Path (Join-Path $appFolder "DEINSTALLIEREN.bat") -Value $uninstallBat -Encoding ASCII

$readme = @"
Tesla Auftragsbearbeitung & Druck - USB-Version

Voraussetzungen auf Ziel-PC:
- Windows
- .NET Framework 4.8

Schnellstart (ohne Installation):
- STARTEN.bat

Einfache Installation (pro Benutzer, ohne Admin):
1) INSTALLIEREN.bat ausführen
2) Desktop-Verknüpfung "Tesla Auftragsbearbeitung & Druck" nutzen

Deinstallation:
- DEINSTALLIEREN.bat ausführen

Zertifikat (für interne PCs):
- Falls CodeSigning.cer vorhanden: ZERTIFIKAT_INSTALLIEREN.bat ausführen
- Dadurch wird das Zertifikat in "Vertrauenswürdige Stammzertifizierungsstellen" und "Vertrauenswürdige Herausgeber" (aktueller Benutzer) importiert.

Sicherheit / Acronis-Hinweis:
- Bei vielen PDF-Änderungen kann Acronis Active Protection eine Warnung zeigen.
- In Acronis bitte "Vertrauen" für die EXE wählen und optional den Arbeitsordner freigeben.
- Mit Zertifikat signierte EXE reduziert solche Warnungen.

Hinweise:
- Bei erstem Start kann Windows SmartScreen nachfragen.
- Für Vorschau/Druck muss ein PDF-Viewer installiert sein.
"@
Set-Content -Path (Join-Path $appFolder "README_USB.txt") -Value $readme -Encoding UTF8

Write-Host "[4/5] ZIP wird erstellt..."
$zipPath = Join-Path $usbRoot "Tesla-Auftragsbearbeitung-und-Druck.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $appFolder "*") -DestinationPath $zipPath -Force

Write-Host "[5/5] Fertig"
Write-Host "Ordner: $appFolder"
Write-Host "ZIP:    $zipPath"