# NuGet Package Installation Script
# Dieses Script installiert iTextSharp für das PDF-Watermark-Feature

$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path

# Versuche nuget.exe zu finden
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
	Write-Host "⚠️ NuGet nicht gefunden. Bitte manuell installieren:"
	Write-Host "1. Öffne Visual Studio"
	Write-Host "2. Tools > NuGet Package Manager > Package Manager Console"
	Write-Host "3. Führe aus: Install-Package iTextSharp -Version 5.5.13.3"
	exit
}

Write-Host "NuGet gefunden: $nugetExe"
Write-Host "Installiere iTextSharp..."

& $nugetExe install iTextSharp -Version 5.5.13.3 -OutputDirectory "$projectPath\packages"

if ($LASTEXITCODE -eq 0) {
	Write-Host "✅ iTextSharp erfolgreich installiert"
} else {
	Write-Host "❌ Installation fehlgeschlagen"
	exit 1
}
