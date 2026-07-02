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



$productName = "DOK-V01"

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
	& powershell -ExecutionPolicy Bypass -File (Join-Path $root "create-app-icon.ps1")
	if ($LASTEXITCODE -ne 0) {
		throw "Icon-Erstellung fehlgeschlagen."
	}
}



$objDir = Join-Path $root "obj\$Configuration"
if (Test-Path $objDir) {
	Remove-Item $objDir -Recurse -Force
}

Write-Host "[1b/5] Build ($Configuration) startet..."

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
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "set-exe-icon.ps1") -ExePath $releaseExe -IconPath (Join-Path $root "Assets\AppIcon.ico")
if ($LASTEXITCODE -ne 0) {
	throw "Icon konnte nicht in Release-EXE gesetzt werden."
}



$usbRoot = Join-Path $root "USB-Version"

$appFolder = Join-Path $usbRoot $productName



Write-Host "[2/5] USB-Version bereinigen..."

if (-not (Test-Path $usbRoot)) {

	New-Item -ItemType Directory -Path $usbRoot | Out-Null

}

Get-ChildItem $usbRoot -Force | ForEach-Object {

	if ($_.Name -eq $productName -or $_.Name -eq "$productName.zip") {

		return

	}

	Remove-Item $_.FullName -Recurse -Force

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

Get-ChildItem $releaseDir -File | Where-Object {

	$_.Extension -in @('.exe', '.dll', '.config')

} | ForEach-Object {

	Copy-Item $_.FullName -Destination (Join-Path $appFolder $_.Name) -Force

}

$iconSource = Join-Path $root "Assets\AppIcon.ico"
if (Test-Path $iconSource) {
	$assetsFolder = Join-Path $appFolder "Assets"
	New-Item -ItemType Directory -Path $assetsFolder -Force | Out-Null
	Copy-Item $iconSource (Join-Path $assetsFolder "AppIcon.ico") -Force
	Copy-Item $iconSource (Join-Path $appFolder "AppIcon.ico") -Force
}

$licenseSource = Join-Path $root "LICENSE_DE.txt"
if (Test-Path $licenseSource) {
	Copy-Item $licenseSource (Join-Path $appFolder "LICENSE_DE.txt") -Force
}



$exePath = Join-Path $appFolder $exeName

if (-not (Test-Path $exePath)) {

	throw "Hauptprogramm nicht gefunden: $exePath"

}



if ((Test-Path $CertPath) -and (Test-Path $exePath)) {

    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source

    if ([string]::IsNullOrWhiteSpace($signtool)) {

        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |

            Sort-Object FullName -Descending |

            Select-Object -First 1 -ExpandProperty FullName

    }

    if (-not [string]::IsNullOrWhiteSpace($signtool)) {

        Write-Host "[3b/5] Signiere EXE mit Zertifikat..."

        $signArgs = @("sign", "/fd", "SHA256", "/f", $CertPath)

        if (-not [string]::IsNullOrWhiteSpace($CertPassword)) {

            $signArgs += @("/p", $CertPassword)

        }

        $signArgs += @("/tr", "http://timestamp.digicert.com", "/td", "SHA256", $exePath)



        & $signtool @signArgs

        if ($LASTEXITCODE -ne 0) {

            Write-Warning "Signieren fehlgeschlagen (ExitCode $LASTEXITCODE). USB-Version wird trotzdem erstellt."

        }

    }

    else {

        Write-Warning "signtool.exe nicht gefunden. EXE bleibt unsigniert."

    }

}



Write-Host "[3c/5] USB-Starter wird erstellt..."

Copy-Item $exePath (Join-Path $appFolder $usbLauncherName) -Force
Copy-Item $exePath (Join-Path $appFolder $usbUninstallerName) -Force



$deployCer = Join-Path $root "CodeSigning.cer"

if (Test-Path $deployCer) {

    Copy-Item $deployCer (Join-Path $appFolder "CodeSigning.cer") -Force

}

elseif (Test-Path $CertPublicPath) {

    Copy-Item $CertPublicPath (Join-Path $appFolder "CodeSigning.cer") -Force

}

$updateToken = $env:GITHUB_UPDATE_TOKEN
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

$productName - USB-Version



EINMALIGE FESTINSTALLATION AUF JEDEM PC:

1) USB-Stick einstecken

2) Ordner "$productName" oeffnen

3) "$usbLauncherName" doppelklicken (Setup-Assistent mit Lizenz & Urheberrecht)

4) Lizenz akzeptieren und Installieren klicken

5) Windows-Administratorabfrage (UAC) bestaetigen

6) Installation nach C:\Program Files\$productName

7) Desktop-Verknuepfung "$productName" wird erstellt



AB DANN:

- Nur noch Desktop-Verknuepfung nutzen (nicht USB, nicht Firmenordner-Kopie)

- Arbeitsdaten/PDFs: %LOCALAPPDATA%\$productName\Ablage

- Updates automatisch von GitHub



Voraussetzungen:

- Windows mit .NET Framework 4.8

- Internet für GitHub-Updates



Deinstallation:

- "$usbUninstallerName" oder DEINSTALLIEREN.bat (Setup-Assistent)

"@

Set-Content -Path (Join-Path $appFolder "README_USB.txt") -Value $readme -Encoding UTF8



Write-Host "[4/5] ZIP wird erstellt..."

$zipPath = Join-Path $usbRoot "$productName.zip"

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Compress-Archive -Path (Join-Path $appFolder "*") -DestinationPath $zipPath -Force



Write-Host "[5/5] Explorer-Icon-Cache aktualisieren..."
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "refresh-explorer-icon-cache.ps1") -RestartExplorer

Write-Host "[6/6] Fertig"

Write-Host "Ordner: $appFolder"

Write-Host "ZIP:    $zipPath"


