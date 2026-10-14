# Erstellt den portable KI-Ordner DOK-V01-KI (USB, Nebenordner zum Programm).
# Enthält Ollama + Modell (~1,5–2 GB). Sortieren/Wasserzeichen/Material unveraendert.
param(
    [string]$TargetRoot = "",
    [string]$Model = "llama3.2:3b"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if ([string]::IsNullOrWhiteSpace($TargetRoot)) {
    $TargetRoot = Join-Path $root "USB-Version\DOK-V01-KI"
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

$env:OLLAMA_MODELS = $modelsDir
$env:OLLAMA_HOME = $homeDir
$env:OLLAMA_HOST = "127.0.0.1:11434"

Write-Host "Starte Ollama kurz zum Modell-Download..."
$serve = Start-Process -FilePath $ollamaExe -ArgumentList "serve" -WorkingDirectory $kiDir -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 4

Write-Host "Lade Modell $Model (kann einige Minuten dauern)..."
& $ollamaExe pull $Model
if ($LASTEXITCODE -ne 0) {
    Stop-Process -Id $serve.Id -Force -ErrorAction SilentlyContinue
    throw "ollama pull fehlgeschlagen."
}

Stop-Process -Id $serve.Id -Force -ErrorAction SilentlyContinue

$readme = @"
DOK-V01-KI – portable Text-KI (Ollama)
======================================

Struktur auf USB:
  DOK-V01-Rxx/     Programm (~100 MB)
  DOK-V01-KI/      Text-KI (~1,5–2 GB)  <-- dieser Ordner

Inhalt:
  ollama.exe
  models/          Sprachmodell
  home/            Ollama-Daten
  KI-starten.bat   optional manuell starten

Auf anderem PC:
1. Beide Ordner (Programm + DOK-V01-KI) nebeneinander kopieren
2. DOK-V01 starten
3. Einstellungen -> KI -> Ollama aktivieren, «Portable KI» an

Hinweis: Sortieren, Wasserzeichen und Material laufen ohne diesen Ordner (Regeln).
"@

Set-Content -Path (Join-Path $kiDir "README_KI.txt") -Value $readme -Encoding UTF8

$bat = @"
@echo off
cd /d "%~dp0"
set OLLAMA_MODELS=%~dp0models
set OLLAMA_HOME=%~dp0home
set OLLAMA_HOST=127.0.0.1:11434
start "" /B ollama.exe serve
echo Ollama gestartet (127.0.0.1:11434)
pause
"@
Set-Content -Path (Join-Path $kiDir "KI-starten.bat") -Value $bat -Encoding ASCII

Write-Host "Fertig: $kiDir"
Write-Host "Groesse ca.: $([math]::Round((Get-ChildItem $kiDir -Recurse | Measure-Object Length -Sum).Sum / 1GB, 2)) GB"
