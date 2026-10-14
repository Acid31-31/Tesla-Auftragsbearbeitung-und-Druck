param(
    [string]$DestinationRoot = ''
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

. (Join-Path $root "Get-DokBuildRevision.ps1")
$buildInfo = Get-DokRevisionFromAssembly -Root $root

$zParent = 'Z:\DOK-V01'
$revFolder = Join-Path $zParent $buildInfo.ProductFolder
$revZip = Join-Path $zParent $buildInfo.UsbZipName

$programSource = Join-Path $root ("USB-Version\" + $buildInfo.ProductFolder)
if (-not (Test-Path (Join-Path $programSource "DOK-V01.exe"))) {
    throw "USB-Programm fehlt: $programSource. Zuerst .\create-usb-version.ps1 ausfuehren."
}

$backupRoot = if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    'Z:\Programierung\DOK-V01-Absicherung'
} else {
    Join-Path $DestinationRoot 'DOK-V01-Absicherung'
}

$backupProgram = Join-Path $backupRoot 'Programm'
Write-Host "Absicherung nach: $backupRoot"
New-Item -ItemType Directory -Path $backupProgram -Force | Out-Null

& robocopy $programSource $backupProgram /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Kopieren fehlgeschlagen (robocopy $LASTEXITCODE)."
}

$sourceZipName = "DOK-V01-Quelle-$($buildInfo.RevisionLabel).zip"
$sourceZipDest = Join-Path $backupRoot $sourceZipName
$stage = Join-Path $env:TEMP "DOK-V01-Quelle-$($buildInfo.RevisionLabel)"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

$excludeDirNames = @(
    '.git', '.vs', 'bin', 'obj', 'USB-Version', 'Release-Version',
    'Archive', 'Backups', 'packages'
)
Get-ChildItem $root -Force | Where-Object {
    $_.Name -notin $excludeDirNames
} | ForEach-Object {
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

Write-Host "Z-Hauptordner: $zParent"
New-Item -ItemType Directory -Path $zParent -Force | Out-Null
Get-ChildItem $zParent -Force -ErrorAction SilentlyContinue | Where-Object {
    ($_.Name -like 'DOK-V01-R*' -and $_.Name -ne $buildInfo.ProductFolder) `
    -or ($_.Name -like 'DOK-V01-R*.zip' -and $_.Name -ne $buildInfo.UsbZipName)
} | ForEach-Object {
    Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

& robocopy $programSource $revFolder /MIR /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Z-Kopie fehlgeschlagen (robocopy $LASTEXITCODE)."
}
Copy-Item (Join-Path $root ("USB-Version\" + $buildInfo.UsbZipName)) $revZip -Force

$readmeLines = @(
    "DOK-V01 - Absicherung $($buildInfo.RevisionLabel)"
    "Stand: $(Get-Date -Format 'dd.MM.yyyy HH:mm')"
    ""
    "PROGRAMM: Programm\STARTEN.bat"
    "USB-ORDNER: $revFolder"
    "REV-ZIP: $revZip"
    "QUELLCODE: $sourceZipName"
    "PROJEKT: $root"
)
Set-Content -Path (Join-Path $backupRoot 'README_SICHERUNG.txt') -Value $readmeLines -Encoding UTF8

Write-Host "Fertig: $backupRoot"
explorer.exe $backupRoot
explorer.exe $zParent
