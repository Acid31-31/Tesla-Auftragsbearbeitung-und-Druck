param(
    [string]$AblageFolder = "$env:LOCALAPPDATA\DOK-V01\Ablage"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root
$diagExe = Join-Path $root "Tools\PdfDiag\bin\Debug\PdfDiag.exe"
$appExe = Join-Path $root "bin\Debug\DOK-V01.exe"

Write-Host "=== DOK-V01 Testauftrag ===" -ForegroundColor Cyan
Write-Host "Ablage: $AblageFolder"
Write-Host ""

if (-not (Test-Path $AblageFolder)) {
    New-Item -ItemType Directory -Path $AblageFolder -Force | Out-Null
}

$pdfs = @(Get-ChildItem $AblageFolder -Filter "*.pdf" -ErrorAction SilentlyContinue | Where-Object {
    -not $_.Name.StartsWith("_") -and $_.Name -ne "Bestellung.pdf"
})
$excel = @(Get-ChildItem $AblageFolder -Filter "Daten*.xlsx" -ErrorAction SilentlyContinue)

Write-Host "Dateien in Ablage:"
Write-Host ("  PDF-Zeichnungen: {0}" -f $pdfs.Count)
Write-Host ("  Excel:           {0}" -f ($excel.Name -join ", "))
Write-Host ""

if ($pdfs.Count -eq 0) {
    Write-Host "Keine PDFs gefunden." -ForegroundColor Yellow
    Write-Host "Bitte jetzt in DOK-V01 ablegen:" -ForegroundColor Yellow
    Write-Host "  1) PDF-Zeichnungen per Drag and Drop in die Ablage" -ForegroundColor Yellow
    Write-Host "  2) Daten.xlsx (optional Daten_Angebot.xlsx)" -ForegroundColor Yellow
    Write-Host "  3) Dieses Skript erneut starten: .\test-auftrag.ps1" -ForegroundColor Yellow
    Write-Host ""
    if (Test-Path $appExe) {
        Write-Host "Starte DOK-V01 zum Ablegen der Dateien..."
        Start-Process $appExe
    }
    exit 2
}

$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
& $msbuild (Join-Path $root "Tools\PdfDiag\PdfDiag.csproj") /p:Configuration=Debug /v:minimal | Out-Null

Write-Host "=== KI-Wasserzeichen-Analyse (ohne Dateien zu aendern) ===" -ForegroundColor Cyan
& $diagExe --watermark-test $AblageFolder
$code = $LASTEXITCODE

Write-Host ""
Write-Host "=== Naechster Schritt in der App ===" -ForegroundColor Cyan
Write-Host "  1) Menue Wasserzeichen -> Intelligente Platzierung AN"
Write-Host "  2) Sortieren und umbenennen"
Write-Host "  3) Zusammenfuegen und Vorschau -> bei Bedarf eine Zeichnung korrigieren"
Write-Host ""

if (Test-Path $appExe) {
    Start-Process $appExe
}

exit $code
