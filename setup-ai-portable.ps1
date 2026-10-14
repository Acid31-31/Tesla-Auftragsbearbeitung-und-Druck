# Erstellt den portable KI-Ordner AI_DOK-V01 (USB, neben Programm-Ordner).
# Enthält Ollama + Textmodell llama3.2:3b (~2 GB).
param(
    [string]$TargetRoot = "",
    [string]$Model = "tinyllama",
    [string]$VisionModel = "moondream"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if ([string]::IsNullOrWhiteSpace($TargetRoot)) {
    if (Test-Path "Z:\DOK-V01") {
        $TargetRoot = "Z:\DOK-V01\AI_DOK-V01"
    }
    else {
        $TargetRoot = Join-Path $root "USB-Version\AI_DOK-V01"
    }
}

$legacyKi = Join-Path (Split-Path $TargetRoot -Parent) "DOK-V01-KI"
if ((Test-Path $legacyKi) -and -not (Test-Path $TargetRoot)) {
    Write-Host "Benenne Legacy-Ordner um: DOK-V01-KI -> AI_DOK-V01"
    Rename-Item -Path $legacyKi -NewName "AI_DOK-V01"
    $TargetRoot = (Resolve-Path (Join-Path (Split-Path $TargetRoot -Parent) "AI_DOK-V01")).Path
}

$kiDir = $TargetRoot
$modelsDir = Join-Path $kiDir "models"
$homeDir = Join-Path $kiDir "home"
$ollamaExe = Join-Path $kiDir "ollama.exe"

Write-Host "KI-Ordner: $kiDir"
New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
New-Item -ItemType Directory -Path $homeDir -Force | Out-Null

if (-not (Test-Path $ollamaExe)) {
    Write-Host "Lade Ollama fuer Windows..."
    $zipUrl = "https://github.com/ollama/ollama/releases/latest/download/ollama-windows-amd64.zip"
    $zipPath = Join-Path $env:TEMP "ollama-windows-amd64.zip"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Path $zipPath -DestinationPath $kiDir -Force
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $ollamaExe)) {
        throw "ollama.exe nicht gefunden nach Entpacken in $kiDir"
    }
}

$llamaServer = Join-Path $kiDir "lib\ollama\llama-server.exe"
if (-not (Test-Path $llamaServer)) {
    throw "lib\ollama\llama-server.exe fehlt - Ollama-ZIP unvollstaendig in $kiDir"
}

$env:OLLAMA_MODELS = $modelsDir
$env:OLLAMA_HOME = $homeDir
$env:OLLAMA_HOST = "127.0.0.1:11434"

Get-Process ollama -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Starte Ollama kurz zum Modell-Download..."
$serve = Start-Process -FilePath $ollamaExe -ArgumentList "serve" -WorkingDirectory $kiDir -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 5

Write-Host "Lade Modell $Model (kann einige Minuten dauern)..."
& $ollamaExe pull $Model
if ($LASTEXITCODE -ne 0) {
    Stop-Process -Id $serve.Id -Force -ErrorAction SilentlyContinue
    throw "ollama pull fehlgeschlagen."
}

if (-not [string]::IsNullOrWhiteSpace($VisionModel)) {
    Write-Host "Lade Zeichnungs-Vision-Modell $VisionModel (Bestandteil der App)..."
    & $ollamaExe pull $VisionModel
    if ($LASTEXITCODE -ne 0) {
        Stop-Process -Id $serve.Id -Force -ErrorAction SilentlyContinue
        throw "ollama pull $VisionModel fehlgeschlagen."
    }
}

Stop-Process -Id $serve.Id -Force -ErrorAction SilentlyContinue

$readme = @"
AI_DOK-V01 – portable KI (Ollama)
=====================================

Struktur auf USB / anderem PC:
  DOK-V01-Rxx/       Programm
  AI_DOK-V01/        KI inkl. Zeichnungs-Vision  <-- dieser Ordner

Inhalt:
  ollama.exe
  models/            Textmodell + Vision-Modell (moondream)
  home/              Ollama-Daten
  AI-starten.bat     optional manuell starten

Auf anderem PC:
1. Beide Ordner nebeneinander kopieren (Programm + AI_DOK-V01)
2. DOK-V01 starten – Ordner wird automatisch erkannt
3. Zeichnungs-Vision laeuft von selbst (in den KI-Einstellungen nur abschaltbar)
"@

Set-Content -Path (Join-Path $kiDir "README_KI.txt") -Value $readme -Encoding UTF8

$bat = @"
@echo off
cd /d "%~dp0"
set OLLAMA_MODELS=%~dp0models
set OLLAMA_HOME=%~dp0home
set OLLAMA_HOST=127.0.0.1:11434
set OLLAMA_NUM_GPU=0
start "" /B ollama.exe serve
echo Ollama gestartet (127.0.0.1:11434)
pause
"@
Set-Content -Path (Join-Path $kiDir "AI-starten.bat") -Value $bat -Encoding ASCII

if (Test-Path (Join-Path $kiDir "KI-starten.bat")) {
    Remove-Item (Join-Path $kiDir "KI-starten.bat") -Force -ErrorAction SilentlyContinue
}

Write-Host "Fertig: $kiDir"
Write-Host "Groesse ca.: $([math]::Round((Get-ChildItem $kiDir -Recurse -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1GB, 2)) GB"
