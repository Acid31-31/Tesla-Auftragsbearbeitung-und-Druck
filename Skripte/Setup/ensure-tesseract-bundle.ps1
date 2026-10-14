# Bereitet portable Tesseract-OCR fuer DOK-V01 vor (Release/USB, kein manueller Schritt).
param(
    [string]$ProjectRoot = "",
    [string[]]$CopyTo = @(),
    [switch]$SkipDownload
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}

$bundleDir = Join-Path $ProjectRoot "native\win-x64\tesseract"
$installerDir = Join-Path $env:LOCALAPPDATA "DOK-V01\Tools\installers"
$installerPath = Join-Path $installerDir "tesseract-ocr-w64-setup-5.4.0.20240606.exe"
$installerUrl = "https://digi.bib.uni-mannheim.de/tesseract/tesseract-ocr-w64-setup-5.4.0.20240606.exe"
$stagingInstallDir = Join-Path $env:TEMP "DOK-V01-tesseract-bundle"

function Test-TesseractExe {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    if (-not (Test-Path $Path)) { return $false }
    & $Path --version *> $null
    return $LASTEXITCODE -eq 0
}

function Find-ExistingTesseractRoot {
    $candidates = @(
        $bundleDir,
        (Join-Path $env:LOCALAPPDATA "DOK-V01\Tools\tesseract"),
        (Join-Path $ProjectRoot "bin\Release\tesseract"),
        (Join-Path $ProjectRoot "bin\Debug\tesseract"),
        "C:\Program Files\Tesseract-OCR",
        "C:\Program Files (x86)\Tesseract-OCR"
    )

    foreach ($dir in $candidates) {
        $exe = Join-Path $dir "tesseract.exe"
        if (Test-TesseractExe $exe) {
            return $dir
        }
    }

    return $null
}

function Copy-TesseractTree {
    param(
        [string]$SourceRoot,
        [string]$TargetRoot
    )

    if (-not (Test-Path $SourceRoot)) { return }
    if (-not (Test-Path $TargetRoot)) {
        New-Item -ItemType Directory -Path $TargetRoot -Force | Out-Null
    }

    Get-ChildItem $SourceRoot -File | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $TargetRoot $_.Name) -Force
    }

    $sourceTessdata = Join-Path $SourceRoot "tessdata"
    if (Test-Path $sourceTessdata) {
        $targetTessdata = Join-Path $TargetRoot "tessdata"
        if (-not (Test-Path $targetTessdata)) {
            New-Item -ItemType Directory -Path $targetTessdata -Force | Out-Null
        }
        Copy-Item (Join-Path $sourceTessdata "*") $targetTessdata -Recurse -Force
    }
}

function Ensure-RequiredTessdata {
    param([string]$TessdataDir)

    if (-not (Test-Path $TessdataDir)) {
        New-Item -ItemType Directory -Path $TessdataDir -Force | Out-Null
    }

    $required = @{
        'deu.traineddata' = 'https://github.com/tesseract-ocr/tessdata_fast/raw/main/deu.traineddata'
        'eng.traineddata' = 'https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata'
        'osd.traineddata'  = 'https://github.com/tesseract-ocr/tessdata_fast/raw/main/osd.traineddata'
    }

    foreach ($entry in $required.GetEnumerator()) {
        $target = Join-Path $TessdataDir $entry.Key
        if (Test-Path $target) { continue }
        if ($SkipDownload) {
            Write-Warning "Fehlende Sprachdatei: $($entry.Key)"
            continue
        }
        Write-Host "Lade $($entry.Key) ..."
        Invoke-WebRequest -Uri $entry.Value -OutFile $target -UseBasicParsing
    }
}

function Trim-Tessdata {
    param([string]$TessdataDir)

    if (-not (Test-Path $TessdataDir)) { return }

    $keep = @('deu.traineddata', 'eng.traineddata', 'osd.traineddata')
    Get-ChildItem $TessdataDir -File -Filter "*.traineddata" | Where-Object {
        $keep -notcontains $_.Name
    } | ForEach-Object {
        Remove-Item $_.FullName -Force
    }
}

function Install-ToDirectory {
    param([string]$TargetDir)

    if (-not (Test-Path $installerDir)) {
        New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
    }

    if (-not (Test-Path $installerPath)) {
        if ($SkipDownload) {
            throw "Tesseract-Installer fehlt und -SkipDownload ist gesetzt."
        }
        Write-Host "Lade Tesseract-Installer (~48 MB)..."
        Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing
    }

    if (Test-Path $stagingInstallDir) {
        Remove-Item $stagingInstallDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stagingInstallDir -Force | Out-Null

    Write-Host "Installiere Tesseract nach $stagingInstallDir ..."
    $installArgs = @(
        "/VERYSILENT",
        "/NORESTART",
        "/SUPPRESSMSGBOXES",
        "/DIR=`"$stagingInstallDir`""
    )
    $p = Start-Process -FilePath $installerPath -ArgumentList $installArgs -Wait -PassThru
    if ($p.ExitCode -ne 0 -or -not (Test-Path (Join-Path $stagingInstallDir "tesseract.exe"))) {
        throw "Tesseract-Installation fehlgeschlagen (Exit $($p.ExitCode))."
    }

    if (-not (Test-Path $TargetDir)) {
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    }
    Copy-TesseractTree $stagingInstallDir $TargetDir
    Trim-Tessdata (Join-Path $TargetDir "tessdata")
    Ensure-RequiredTessdata (Join-Path $TargetDir "tessdata")
}

$existingRoot = Find-ExistingTesseractRoot
if ($existingRoot -and -not (Test-TesseractExe (Join-Path $bundleDir "tesseract.exe"))) {
    Write-Host "Kopiere Tesseract-Bundle nach $bundleDir ..."
    if (-not (Test-Path $bundleDir)) {
        New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null
    }
    Copy-TesseractTree $existingRoot $bundleDir
    Trim-Tessdata (Join-Path $bundleDir "tessdata")
    Ensure-RequiredTessdata (Join-Path $bundleDir "tessdata")
}
elseif (-not (Test-TesseractExe (Join-Path $bundleDir "tesseract.exe"))) {
    if ($SkipDownload) {
        Write-Warning "Kein Tesseract-Bundle vorhanden (-SkipDownload)."
    }
    else {
        Install-ToDirectory $bundleDir
    }
}

if (-not (Test-TesseractExe (Join-Path $bundleDir "tesseract.exe"))) {
    Write-Warning "Tesseract-Bundle nicht verfuegbar: $bundleDir"
}
else {
    Write-Host "Tesseract-Bundle bereit: $bundleDir"
    Ensure-RequiredTessdata (Join-Path $bundleDir "tessdata")
}

foreach ($target in $CopyTo) {
    if ([string]::IsNullOrWhiteSpace($target)) { continue }
    if (-not (Test-TesseractExe (Join-Path $bundleDir "tesseract.exe"))) { continue }

    Write-Host "Kopiere Tesseract nach $target ..."
    Copy-TesseractTree $bundleDir $target
}
