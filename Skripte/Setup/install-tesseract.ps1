# Installiert Tesseract OCR (offline, kein Ollama) fuer DOK-V01
$ErrorActionPreference = 'Stop'

$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolsDir = Join-Path $env:LOCALAPPDATA "DOK-V01\Tools\tesseract"
$installerDir = Join-Path $env:LOCALAPPDATA "DOK-V01\Tools\installers"
$installerPath = Join-Path $installerDir "tesseract-ocr-w64-setup-5.4.0.20240606.exe"
$installerUrl = "https://digi.bib.uni-mannheim.de/tesseract/tesseract-ocr-w64-setup-5.4.0.20240606.exe"

function Test-Tesseract {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    if (-not (Test-Path $Path)) { return $false }
    & $Path --version *> $null
    return $LASTEXITCODE -eq 0
}

function Find-Tesseract {
    @(
        (Join-Path $toolsDir "tesseract.exe"),
        (Join-Path $projectPath "bin\Debug\tesseract\tesseract.exe"),
        "C:\Program Files\Tesseract-OCR\tesseract.exe",
        "C:\Program Files (x86)\Tesseract-OCR\tesseract.exe"
    ) | Where-Object { Test-Tesseract $_ } | Select-Object -First 1
}

function Copy-ToAppFolder {
    param([string]$SourceExe)
    if (-not $SourceExe) { return }
    $targetDir = Join-Path $projectPath "bin\Debug\tesseract"
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }
    $sourceRoot = Split-Path -Parent $SourceExe
    Copy-Item $SourceExe (Join-Path $targetDir "tesseract.exe") -Force
    $tessdata = Join-Path $sourceRoot "tessdata"
    if (Test-Path $tessdata) {
        $destTessdata = Join-Path $targetDir "tessdata"
        if (-not (Test-Path $destTessdata)) {
            New-Item -ItemType Directory -Path $destTessdata -Force | Out-Null
        }
        Copy-Item (Join-Path $tessdata "*") $destTessdata -Recurse -Force
    }
}

$existing = Find-Tesseract
if ($existing) {
    Write-Host "Tesseract bereits vorhanden: $existing"
    Copy-ToAppFolder $existing
    exit 0
}

if (-not (Test-Path $installerDir)) {
    New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
}

if (-not (Test-Path $installerPath)) {
    Write-Host "Lade Tesseract-Installer herunter (~48 MB)..."
    Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing
}

if (-not (Test-Path $toolsDir)) {
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
}

Write-Host "Starte Tesseract-Installation (einmalig, ggf. UAC bestaetigen)..."
Write-Host "Zielordner: $toolsDir"
$installArgs = @(
    "/VERYSILENT",
    "/NORESTART",
    "/SUPPRESSMSGBOXES",
    "/DIR=`"$toolsDir`""
)

$p = Start-Process -FilePath $installerPath -ArgumentList $installArgs -Wait -PassThru
if ($p.ExitCode -ne 0) {
    Write-Host "Stille Installation fehlgeschlagen (Code $($p.ExitCode)). Starte interaktiven Installer..."
    Start-Process -FilePath $installerPath -Wait
}

$existing = Find-Tesseract
if ($existing) {
    Write-Host "Tesseract installiert: $existing"
    Copy-ToAppFolder $existing
    exit 0
}

Write-Host ""
Write-Host "Tesseract noch nicht gefunden."
Write-Host "Bitte Installer manuell ausfuehren:"
Write-Host "  $installerPath"
Write-Host "oder: winget install UB-Mannheim.TesseractOCR"
exit 1
