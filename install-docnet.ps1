# Installiert Docnet.Core (PDF-Rendering für intelligente Wasserzeichen-Platzierung)

$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$packagesDir = Join-Path $projectPath "packages"
$outputDir = Join-Path $projectPath "bin\Debug"

$nugetPaths = @(
    "C:\Program Files\NuGet\nuget.exe",
    "C:\Program Files (x86)\NuGet\nuget.exe",
    "$env:LOCALAPPDATA\NuGet\nuget.exe"
)

$nugetExe = $null
foreach ($path in $nugetPaths) {
    if (Test-Path $path) {
        $nugetExe = $path
        break
    }
}

if ($null -eq $nugetExe) {
    Write-Host "NuGet nicht gefunden. Bitte Docnet.Core manuell installieren:"
    Write-Host "Install-Package Docnet.Core -Version 2.6.0"
    exit 1
}

Write-Host "NuGet gefunden: $nugetExe"
Write-Host "Installiere Docnet.Core 2.6.0..."

& $nugetExe install Docnet.Core -Version 2.6.0 -OutputDirectory $packagesDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installation fehlgeschlagen."
    exit 1
}

$docnetDll = Join-Path $packagesDir "Docnet.Core.2.6.0\lib\netstandard2.0\Docnet.Core.dll"
$pdfiumSource = Join-Path $packagesDir "Docnet.Core.2.6.0\runtimes\win-x64\native\pdfium.dll"

if (-not (Test-Path $docnetDll)) {
    Write-Host "Docnet.Core.dll nicht gefunden: $docnetDll"
    exit 1
}

if (-not (Test-Path $pdfiumSource)) {
    Write-Host "pdfium.dll nicht gefunden: $pdfiumSource"
    exit 1
}

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

Copy-Item $docnetDll (Join-Path $outputDir "Docnet.Core.dll") -Force
Copy-Item $pdfiumSource (Join-Path $outputDir "pdfium.dll") -Force

Write-Host "Docnet.Core.dll und pdfium.dll nach $outputDir kopiert."
Write-Host "Bitte Projekt in Visual Studio neu bauen (MSBuild kopiert pdfium.dll automatisch)."
