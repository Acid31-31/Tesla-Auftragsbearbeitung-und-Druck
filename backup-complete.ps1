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
$backupRootName = "DOK-V01_Komplett_${timestamp}_${revision}"
$backupRoot = Join-Path $parent $backupRootName
$backupProject = Join-Path $backupRoot "PDF-umbenenen"
$backupData = Join-Path $backupRoot "Datensicherung"

Write-Host "Erstelle komplette Datensicherung: $backupRoot"

$excludeDirs = @('bin', 'obj', '.vs', 'Backups', 'packages')
$excludeNamePatterns = @('PDF-umbenenen_BACKUP_*', 'DOK-V01_Komplett_*', 'DOK-V01_Datensicherung_*')

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

function Copy-TreeIfExists([string]$source, [string]$destination, [string]$label, [ref]$fileCount, [ref]$totalBytes, [System.Collections.Generic.List[string]]$sections, [System.Collections.Generic.List[string]]$warnings) {
    if (!(Test-Path $source)) {
        $warnings.Add("$label`: nicht vorhanden ($source)")
        return
    }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $items = Get-ChildItem -Path $source -Recurse -File -Force -ErrorAction SilentlyContinue
    if ($null -eq $items -or $items.Count -eq 0) {
        $warnings.Add("$label`: leer ($source)")
        return
    }

    foreach ($item in $items) {
        $relative = $item.FullName.Substring($source.Length).TrimStart('\')
        $target = Join-Path $destination $relative
        $targetDir = Split-Path $target -Parent
        if (!(Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }
        Copy-Item -Path $item.FullName -Destination $target -Force
        $fileCount.Value++
        $totalBytes.Value += $item.Length
    }

    $sections.Add("$label ($($items.Count) Datei(en))")
}

New-Item -ItemType Directory -Path $backupProject -Force | Out-Null
Get-ChildItem -Path $root -Force | ForEach-Object {
    if (ShouldExcludePath $_.FullName $root) { return }
    Copy-Item -Path $_.FullName -Destination (Join-Path $backupProject $_.Name) -Recurse -Force
}

$fileCount = 0
$totalBytes = 0
$sections = New-Object 'System.Collections.Generic.List[string]'
$warnings = New-Object 'System.Collections.Generic.List[string]'

$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
Copy-TreeIfExists (Join-Path $localAppData "DOK-V01") (Join-Path $backupData "AppData\DOK-V01") "App-Einstellungen DOK-V01" ([ref]$fileCount) ([ref]$totalBytes) $sections $warnings
Copy-TreeIfExists (Join-Path $localAppData "PDF-umbenenen") (Join-Path $backupData "AppData\PDF-umbenenen") "Wasserzeichen/KI" ([ref]$fileCount) ([ref]$totalBytes) $sections $warnings

$pathsXml = Join-Path $localAppData "DOK-V01\paths.xml"
$excelPath = ""
$pdfFolder = Join-Path $root "bin\Debug\Ablage"
$orderPdf = ""

if (Test-Path $pathsXml) {
    [xml]$pathsDoc = Get-Content $pathsXml -Encoding UTF8
    if ($pathsDoc.Root.ExcelPath) { $excelPath = [string]$pathsDoc.Root.ExcelPath }
    if ($pathsDoc.Root.PdfFolder) { $pdfFolder = [string]$pathsDoc.Root.PdfFolder }
    if ($pathsDoc.Root.OrderVerificationPdfPath) { $orderPdf = [string]$pathsDoc.Root.OrderVerificationPdfPath }
}

Copy-TreeIfExists $pdfFolder (Join-Path $backupData "Arbeitsdaten\Ablage") "Ablage-Ordner" ([ref]$fileCount) ([ref]$totalBytes) $sections $warnings

if ($excelPath -and (Test-Path $excelPath)) {
    $excelDest = Join-Path $backupData "Arbeitsdaten\Excel"
    New-Item -ItemType Directory -Path $excelDest -Force | Out-Null
    Copy-Item -Path $excelPath -Destination (Join-Path $excelDest (Split-Path $excelPath -Leaf)) -Force
    $fileCount++
    $totalBytes += (Get-Item $excelPath).Length
    $sections.Add("Excel: $(Split-Path $excelPath -Leaf)")
} else {
    $warnings.Add("Excel-Datei nicht vorhanden.")
}

if ($orderPdf -and (Test-Path $orderPdf)) {
    $orderDest = Join-Path $backupData "Arbeitsdaten\Bestell-PDF"
    New-Item -ItemType Directory -Path $orderDest -Force | Out-Null
    Copy-Item -Path $orderPdf -Destination (Join-Path $orderDest (Split-Path $orderPdf -Leaf)) -Force
    $fileCount++
    $totalBytes += (Get-Item $orderPdf).Length
    $sections.Add("Bestell-PDF: $(Split-Path $orderPdf -Leaf)")
} else {
    $warnings.Add("Bestell-PDF nicht vorhanden.")
}

$infoPath = Join-Path $backupRoot "BACKUP_INFO.txt"
$sizeMb = [Math]::Round($totalBytes / 1MB, 2)
$sectionText = ($sections | ForEach-Object { "- $_" }) -join [Environment]::NewLine
$warningText = if ($warnings.Count -gt 0) { ($warnings | ForEach-Object { "- $_" }) -join [Environment]::NewLine } else { "- keine" }

@"
DOK-V01 KOMPLETTE DATENSICHERUNG
================================
Revision:  $revision
Version:   $version
Datum:     $(Get-Date -Format "dd.MM.yyyy HH:mm:ss")
Quelle:    $root
Ziel:      $backupRoot
Dateien:   $fileCount
Groesse:   $sizeMb MB

Enthalten:
- Quellcode (ohne bin/, obj/, .vs/, packages/, Backups/)
- App-Einstellungen (%LocalAppData%\DOK-V01, PDF-umbenenen)
- Arbeitsdaten (Ablage, Excel, Bestell-PDF)

Gesicherte Bereiche:
$sectionText

Hinweise:
$warningText
"@ | Set-Content -Path $infoPath -Encoding UTF8

$backupDir = Join-Path $root "Backups"
if (!(Test-Path $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir | Out-Null
}

$zipPath = Join-Path $backupDir "DOK-V01_Komplett_${revision}_${timestamp}.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $backupRoot "*") -DestinationPath $zipPath -Force

Write-Host "Ordner-Backup: $backupRoot"
Write-Host "ZIP-Backup:    $zipPath"
Write-Host "Dateien:       $fileCount ($sizeMb MB)"
