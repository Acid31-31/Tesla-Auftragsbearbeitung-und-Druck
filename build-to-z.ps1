# Baut DOK-V01 direkt nach Z:\DOK-V01\DOK-V01-Rxx (kein Kopieren von bin\Release auf E:).
param(
    [string]$DriveRoot = "Z:\DOK-V01",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

. (Join-Path $root "Get-DokBuildRevision.ps1")
$buildInfo = Get-DokRevisionFromAssembly -Root $root
$targetDir = Join-Path $DriveRoot $buildInfo.ProductFolder

if (-not (Test-Path $DriveRoot)) {
    throw "Laufwerk/Ordner nicht erreichbar: $DriveRoot"
}

$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) { $msbuild = "msbuild" }

$ensureTesseract = Join-Path $root "ensure-tesseract-bundle.ps1"
if (Test-Path $ensureTesseract) {
    Write-Host "Tesseract-Bundle vorbereiten..."
    & powershell -ExecutionPolicy Bypass -File $ensureTesseract -ProjectRoot $root
}

$ensureAi = Join-Path $root "ensure-ai-portable-bundle.ps1"
if (Test-Path $ensureAi) {
    Write-Host "KI-Ordner AI_DOK-V01 pruefen (vollstaendig fuer USB)..."
    & powershell -ExecutionPolicy Bypass -File $ensureAi -ProjectRoot $root
}

if (Test-Path $targetDir) {
    Write-Host "Altes Ziel entfernen: $targetDir"
    Remove-Item $targetDir -Recurse -Force
}
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

$outPath = $targetDir.TrimEnd('\') + '\'
Write-Host "Release-Build direkt nach: $outPath"

& $msbuild (Join-Path $root "PDF-umbenenen.csproj") /t:Rebuild /p:Configuration=$Configuration /p:Platform=AnyCPU "/p:OutputPath=$outPath"
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen." }

foreach ($extra in @(
    @{ Src = "LICENSE_DE.txt"; Dst = "LICENSE_DE.txt" },
    @{ Src = "CodeSigning.cer"; Dst = "CodeSigning.cer" },
    @{ Src = "cert\DOK-CodeSigning.cer"; Dst = "CodeSigning.cer" },
    @{ Src = "Assets\AppIcon.ico"; Dst = "AppIcon.ico" }
)) {
    $srcPath = Join-Path $root $extra.Src
    if (Test-Path $srcPath) {
        Copy-Item $srcPath (Join-Path $targetDir $extra.Dst) -Force
    }
}

$assetsDir = Join-Path $targetDir "Assets"
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
$icon = Join-Path $root "Assets\AppIcon.ico"
if (Test-Path $icon) {
    Copy-Item $icon (Join-Path $assetsDir "AppIcon.ico") -Force
}

$signPackage = Join-Path $root "sign-package-exes.ps1"
if (Test-Path $signPackage) {
    Write-Host "Signiere EXEs auf Z:..."
    & powershell -ExecutionPolicy Bypass -File $signPackage -PackageRoot $targetDir
}

Write-Host "Fertig: $targetDir"
Write-Host "Start: $(Join-Path $targetDir 'DOK-V01.exe')"
$aiSibling = Join-Path (Split-Path $targetDir -Parent) "AI_DOK-V01"
if (Test-Path $aiSibling) {
    Write-Host "KI-Ordner (mitkopieren auf anderen PC): $aiSibling"
    explorer.exe $aiSibling
}
explorer.exe $targetDir
