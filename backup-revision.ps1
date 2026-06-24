$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$appInfoPath = Join-Path $root "AppInfo.cs"
$revision = "R00"
$version = "0.0"
if (Test-Path $appInfoPath) {
    $appInfo = Get-Content $appInfoPath -Raw
    $revMatch = [Regex]::Match($appInfo, 'RevisionLabel\s*=\s*"([^"]+)"')
    $verMatch = [Regex]::Match($appInfo, 'VersionLabel\s*=\s*"([^"]+)"')
    if ($revMatch.Success) { $revision = $revMatch.Groups[1].Value.Trim() }
    if ($verMatch.Success) { $version = $verMatch.Groups[1].Value.Trim() }
}
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$parent = Split-Path -Parent $root

$excludeDirs = @('bin', 'obj', '.vs', 'Backups', 'packages')
$excludeNamePatterns = @('PDF-umbenenen_BACKUP_*')

$backupRootName = "PDF-umbenenen_BACKUP_${timestamp}_${revision}"
$backupRoot = Join-Path $parent $backupRootName
$backupProject = Join-Path $backupRoot "PDF-umbenenen"

Write-Host "Erstelle Ordner-Backup: $backupRoot"

function ShouldExcludePath([string]$fullPath, [string]$sourceRoot) {
    $relative = $fullPath.Substring($sourceRoot.Length).TrimStart('\')
    foreach ($dir in $excludeDirs) {
        if ($relative -eq $dir -or $relative.StartsWith("$dir\")) { return $true }
    }
    $name = Split-Path $relative -Leaf
    foreach ($pattern in $excludeNamePatterns) {
        if ($name -like $pattern) { return $true }
    }
    return $false
}

New-Item -ItemType Directory -Path $backupProject -Force | Out-Null

Get-ChildItem -Path $root -Force | ForEach-Object {
    if (ShouldExcludePath $_.FullName $root) { return }
    Copy-Item -Path $_.FullName -Destination (Join-Path $backupProject $_.Name) -Recurse -Force
}

$infoPath = Join-Path $backupRoot "BACKUP_INFO.txt"
@"
DOK-V01 APP BACKUP
================
Revision: $revision
Version:  $version
Datum:    $(Get-Date -Format "dd.MM.yyyy HH:mm:ss")
Quelle:   $root
Ziel:     $backupRoot

Enthalten:
- Kompletter Quellcode (ohne bin/, obj/, .vs/, packages/, Backups/)

Neu in ${revision}:
- KI-Assistent (Menü, Chat, Qualitätsprüfung, Ollama)
- Intelligente Wasserzeichen-Platzierung (PDF-Analyse)
- Werkstoff/Materialstärke aus Schriftfeld
- Wasserzeichen-Einstellungsfenster
- Dark/Light Theme für Menüs und Dialoge
"@ | Set-Content -Path $infoPath -Encoding UTF8

$backupDir = Join-Path $root "Backups"
if (!(Test-Path $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir | Out-Null
}

$zipPath = Join-Path $backupDir "DOK-V01_Backup_${revision}_${timestamp}.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $backupRoot "*") -DestinationPath $zipPath -Force

Write-Host "Ordner-Backup: $backupRoot"
Write-Host "ZIP-Backup:    $zipPath"
