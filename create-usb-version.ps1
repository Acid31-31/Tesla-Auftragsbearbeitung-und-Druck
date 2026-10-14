param(

	[string]$Configuration = "Release",

	[string]$CertPath = ".\cert\DOK-CodeSigning.pfx",

	[string]$CertPassword = "",

	[string]$CertPasswordFile = ".\cert\signing-password.txt",

	[string]$CertPublicPath = ".\cert\DOK-CodeSigning.cer"

)



$ErrorActionPreference = "Stop"



$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Set-Location $root

. (Join-Path $root "Get-DokBuildRevision.ps1")
$buildInfo = Get-DokRevisionFromAssembly -Root $root



$productName = "DOK-V01"

$productFolder = $buildInfo.ProductFolder

$exeName = "$productName.exe"

$usbLauncherName = "Programm installieren.exe"
$usbUninstallerName = "Programm deinstallieren.exe"



$projectFile = Join-Path $root "PDF-umbenenen.csproj"

$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"

if (-not (Test-Path $msbuild)) {

	$msbuild = "msbuild"

}



if (-not (Test-Path $CertPath)) {
	throw "Zertifikat nicht gefunden: $CertPath"
}



if ([string]::IsNullOrWhiteSpace($CertPassword) -and (Test-Path $CertPasswordFile)) {

	$CertPassword = (Get-Content $CertPasswordFile -Raw).Trim()

}



Write-Host "[1/5] Icon und Build ($Configuration) starten..."

$iconPath = Join-Path $root "Assets\AppIcon.ico"
if (-not (Test-Path $iconPath)) {
	& powershell -ExecutionPolicy Bypass -File (Join-Path $root "Skripte\Build\create-app-icon.ps1")
	if ($LASTEXITCODE -ne 0) {
		throw "Icon-Erstellung fehlgeschlagen."
	}
}



$objDir = Join-Path $root "obj\$Configuration"
if (Test-Path $objDir) {
	Remove-Item $objDir -Recurse -Force
}

Write-Host "[1b/5] Build ($Configuration) startet..."

$ensureTesseract = Join-Path $root "Skripte\Setup\ensure-tesseract-bundle.ps1"
if (Test-Path $ensureTesseract) {
    Write-Host "[1a/5] Tesseract-OCR-Bundle vorbereiten..."
    & powershell -ExecutionPolicy Bypass -File $ensureTesseract -ProjectRoot $root
}

& $msbuild $projectFile /t:Rebuild /p:Configuration=$Configuration /p:Platform="AnyCPU"

if ($LASTEXITCODE -ne 0) {

	throw "Build fehlgeschlagen."

}



$releaseDir = Join-Path $root "bin\$Configuration"

if (-not (Test-Path $releaseDir)) {

	throw "Build-Ausgabe nicht gefunden: $releaseDir"

}

$releaseExe = Join-Path $releaseDir $exeName
if (-not (Test-Path $releaseExe)) {
	throw "Build-EXE nicht gefunden: $releaseExe"
}

Write-Host "[1c/5] Icon in EXE erzwingen..."
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "Skripte\Build\set-exe-icon.ps1") -ExePath $releaseExe -IconPath (Join-Path $root "Assets\AppIcon.ico")
if ($LASTEXITCODE -ne 0) {
	throw "Icon konnte nicht in Release-EXE gesetzt werden."
}



$usbRoot = Join-Path $root "USB-Version"

$appFolder = Join-Path $usbRoot $productFolder

$zParent = 'Z:\DOK-V01'
$zRevFolder = Join-Path $zParent $productFolder
$zRevZip = Join-Path $zParent $buildInfo.UsbZipName
$backupRoot = 'Z:\Programierung\DOK-V01-Absicherung'
$backupProgram = Join-Path $backupRoot 'Programm'



Write-Host "[2/5] USB-Version bereinigen ($productFolder)..."

if (-not (Test-Path $usbRoot)) {

	New-Item -ItemType Directory -Path $usbRoot | Out-Null

}

Get-ChildItem $usbRoot -Force -ErrorAction SilentlyContinue | Where-Object {

	$_.Name -like "$productName*" -or $_.Name -like "DOK-V01-R*"

} | ForEach-Object {

	Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue

}



Write-Host "[2b/5] Zielordner wird vorbereitet..."

if (Test-Path $appFolder) {

	try {

		Remove-Item $appFolder -Recurse -Force -ErrorAction Stop

	}

	catch {

		Write-Warning "USB-Ordner ist gesperrt (Explorer offen?) - Dateien werden ueberschrieben."

	}

}

if (-not (Test-Path $appFolder)) {

	New-Item -ItemType Directory -Path $appFolder | Out-Null

}



Write-Host "[3/5] Dateien werden kopiert..."

Copy-DokProgramPayload -ReleaseDir $releaseDir -Destination $appFolder -ProjectRoot $root



$exePath = Join-Path $appFolder $exeName

if (-not (Test-Path $exePath)) {

	throw "Hauptprogramm nicht gefunden: $exePath"

}



Write-Host "[3b/5] USB-Starter wird erstellt..."

Copy-Item $exePath (Join-Path $appFolder $usbLauncherName) -Force
Copy-Item $exePath (Join-Path $appFolder $usbUninstallerName) -Force

$signPackageScript = Join-Path $root "sign-package-exes.ps1"
if (Test-Path $signPackageScript) {
    Write-Host "[3c/5] Signiere alle Paket-EXEs..."
    & powershell -ExecutionPolicy Bypass -File $signPackageScript -PackageRoot $appFolder
    if ($LASTEXITCODE -ne 0) {
        throw "Paket-Signierung fehlgeschlagen."
    }
}



$deployCer = Join-Path $root "CodeSigning.cer"

if (Test-Path $deployCer) {

    Copy-Item $deployCer (Join-Path $appFolder "CodeSigning.cer") -Force

}

elseif (Test-Path $CertPublicPath) {

    Copy-Item $CertPublicPath (Join-Path $appFolder "CodeSigning.cer") -Force

}

$GitHubUpdatesArePublic = $true
$updateToken = $env:GITHUB_UPDATE_TOKEN
if (-not $GitHubUpdatesArePublic) {
if ([string]::IsNullOrWhiteSpace($updateToken)) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        try { $updateToken = (gh auth token 2>$null).Trim() } catch { }
    }
}
if (-not [string]::IsNullOrWhiteSpace($updateToken)) {
    Set-Content -Path (Join-Path $appFolder "github.token") -Value $updateToken.Trim() -NoNewline -Encoding ASCII
    Write-Host "github.token fuer private Repo-Updates in USB-Version aufgenommen."
}
}



$startBat = @"

@echo off

cd /d "%~dp0"

start "" "$usbLauncherName"

"@

Set-Content -Path (Join-Path $appFolder "STARTEN.bat") -Value $startBat -Encoding ASCII



$uninstallBat = @"

@echo off

cd /d "%~dp0"

start "" "$usbUninstallerName"

"@

Set-Content -Path (Join-Path $appFolder "DEINSTALLIEREN.bat") -Value $uninstallBat -Encoding ASCII



$readme = @"

$productName $buildInfo.RevisionLabel - USB-Version



EINMALIGE FESTINSTALLATION AUF JEDEM PC:

1) USB-Stick einstecken

2) Ordner "$productFolder" oeffnen

3) "$usbLauncherName" doppelklicken (Setup-Assistent mit Lizenz & Urheberrecht)

4) Lizenz akzeptieren und Installieren klicken

5) Windows-Administratorabfrage (UAC) bestaetigen

6) Installation im gleichen Ordner (USB / kopierter DOK-V01-Ordner)

7) Desktop-Verknuepfung "$productName" wird erstellt



AB DANN:

- Nur noch Desktop-Verknuepfung nutzen (nicht USB, nicht Firmenordner-Kopie)

- Programm und Arbeitsdaten liegen im Installationsordner (Unterordner Ablage)

- Updates automatisch von GitHub



Voraussetzungen:

- Windows mit .NET Framework 4.8

- Internet für GitHub-Updates



Deinstallation:

- "$usbUninstallerName" oder DEINSTALLIEREN.bat (Setup-Assistent)

"@

Set-Content -Path (Join-Path $appFolder "README_USB.txt") -Value $readme -Encoding UTF8



Write-Host "[4/5] ZIP wird erstellt..."

$zipPath = Join-Path $usbRoot $buildInfo.UsbZipName

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($appFolder, $zipPath)

Write-Host "[4b/5] Z-Hauptordner und Absicherung..."
New-Item -ItemType Directory -Path $zParent,$backupProgram -Force | Out-Null

Get-ChildItem $zParent -Force -ErrorAction SilentlyContinue | Where-Object {
	($_.Name -like 'DOK-V01-R*' -and $_.Name -ne $productFolder) `
	-or ($_.Name -like 'DOK-V01-R*.zip' -and $_.Name -ne $buildInfo.UsbZipName)
} | ForEach-Object {
	Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

& robocopy $appFolder $zRevFolder /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
	throw "Z-Kopie fehlgeschlagen (robocopy exit $LASTEXITCODE)"
}
Copy-Item $zipPath $zRevZip -Force

& robocopy $appFolder $backupProgram /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
	throw "Absicherung Programm fehlgeschlagen (robocopy exit $LASTEXITCODE)"
}

$sourceZipName = "DOK-V01-Quelle-$($buildInfo.RevisionLabel).zip"
$sourceZipDest = Join-Path $backupRoot $sourceZipName
$stage = Join-Path $env:TEMP "DOK-V01-Quelle-$($buildInfo.RevisionLabel)"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

$excludeDirNames = @('.git', '.vs', 'bin', 'obj', 'USB-Version', 'Release-Version', 'Archive', 'Backups', 'packages', 'AI_DOK-V01', 'DOK-V01-KI')
Get-ChildItem $root -Force | Where-Object { $_.Name -notin $excludeDirNames } | ForEach-Object {
	Copy-Item $_.FullName -Destination (Join-Path $stage $_.Name) -Recurse -Force
}
$tempZip = Join-Path $env:TEMP $sourceZipName
if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $tempZip -Force
Copy-Item $tempZip $sourceZipDest -Force
Remove-Item $stage -Recurse -Force
Remove-Item $tempZip -Force

Get-ChildItem $backupRoot -Filter 'DOK-V01-Quelle-*.zip' |
	Where-Object { $_.Name -ne $sourceZipName } |
	ForEach-Object { Remove-Item $_.FullName -Force }

$readmeLines = @(
	"DOK-V01 - Absicherung $($buildInfo.RevisionLabel)"
	"Stand: $(Get-Date -Format 'dd.MM.yyyy HH:mm')"
	""
	"PROGRAMM: Programm\STARTEN.bat"
	"USB-ORDNER: $zRevFolder"
	"REV-ZIP: $zRevZip"
	"QUELLCODE: $sourceZipName"
	"PROJEKT: $root"
)
Set-Content -Path (Join-Path $backupRoot 'README_SICHERUNG.txt') -Value $readmeLines -Encoding UTF8



Write-Host "[5/5] Explorer-Icon-Cache aktualisieren..."
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "Skripte\Build\refresh-explorer-icon-cache.ps1") -RestartExplorer

Write-Host "[6/6] Fertig"

Write-Host "Projekt USB:  $appFolder"
Write-Host "Projekt ZIP:  $zipPath"
Write-Host "Z-Hauptordner: $zRevFolder"
Write-Host "Z-ZIP:         $zRevZip"
Write-Host "Absicherung:   $backupRoot"

explorer.exe $zParent
explorer.exe $backupRoot
explorer.exe $appFolder


