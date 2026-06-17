# Installiert PdfiumViewer fuer PDF-Bildanalyse (freie Flaechen erkennen)
$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$packagesDir = Join-Path $projectPath "packages"
$pkgDir = Join-Path $packagesDir "PdfiumViewer.2.13.0"
$nupkg = Join-Path $env:TEMP "PdfiumViewer.2.13.0.nupkg"

Write-Host "Installiere PdfiumViewer 2.13.0 ..."

if (-not (Test-Path $nupkg)) {
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/PdfiumViewer/2.13.0" -OutFile $nupkg
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $pkgDir) { Remove-Item $pkgDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pkgDir | Out-Null
[System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $pkgDir)

$viewerDll = Join-Path $pkgDir "lib\net20\PdfiumViewer.dll"
$pdfiumX64 = Join-Path $pkgDir "x64\pdfium.dll"
$pdfiumX86 = Join-Path $pkgDir "x86\pdfium.dll"

foreach ($config in @("Debug", "Release")) {
    $outDir = Join-Path $projectPath "bin\$config"
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

    Copy-Item $viewerDll (Join-Path $outDir "PdfiumViewer.dll") -Force
    Copy-Item $pdfiumX64 (Join-Path $outDir "pdfium.dll") -Force

    $x64Dir = Join-Path $outDir "x64"
    $x86Dir = Join-Path $outDir "x86"
    New-Item -ItemType Directory -Force -Path $x64Dir | Out-Null
    New-Item -ItemType Directory -Force -Path $x86Dir | Out-Null
    Copy-Item $pdfiumX64 (Join-Path $x64Dir "pdfium.dll") -Force
    Copy-Item $pdfiumX86 (Join-Path $x86Dir "pdfium.dll") -Force
}

Write-Host "OK: PdfiumViewer.dll und pdfium.dll nach bin\Debug und bin\Release kopiert."
Write-Host "Status: PDF-Bildanalyse fuer intelligente Wasserzeichen-Platzierung ist aktiv."
