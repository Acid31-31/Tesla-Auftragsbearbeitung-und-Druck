param(
    [string]$Tag = "",
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipGh,
    [switch]$Draft
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

. (Join-Path $root "Get-DokBuildRevision.ps1")
$buildInfo = Get-DokRevisionFromAssembly -Root $root

$productName = "DOK-V01"
$assetName = $buildInfo.ReleaseZipName
$releaseRoot = Join-Path $root "Release-Version"
$releaseFolder = Join-Path $releaseRoot $buildInfo.ProductFolder
$projectFile = Join-Path $root "PDF-umbenenen.csproj"
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    $msbuild = "msbuild"
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = $buildInfo.VersionTag
}

if (-not $SkipBuild) {
    $ensureTesseract = Join-Path $root "ensure-tesseract-bundle.ps1"
    if (Test-Path $ensureTesseract) {
        Write-Host "Bereite Tesseract-OCR-Bundle vor..."
        & powershell -ExecutionPolicy Bypass -File $ensureTesseract -ProjectRoot $root
    }

    Write-Host "Baue $Configuration..."
    & $msbuild $projectFile /t:Rebuild /p:Configuration=$Configuration /p:Platform="AnyCPU"
    if ($LASTEXITCODE -ne 0) {
        throw "Build fehlgeschlagen."
    }

    $signScript = Join-Path $root "sign-app.ps1"
    $certPath = Join-Path $root "cert\DOK-CodeSigning.pfx"
    if ((Test-Path $signScript) -and (Test-Path $certPath)) {
        Write-Host "Signiere Release-EXE..."
        & $signScript -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "Signieren fehlgeschlagen."
        }
    }
    else {
        Write-Warning "Signieren übersprungen (Zertifikat oder sign-app.ps1 fehlt)."
    }
}

$releaseDir = Join-Path $root "bin\$Configuration"
if (-not (Test-Path (Join-Path $releaseDir "$productName.exe"))) {
    throw "Release-EXE nicht gefunden: $releaseDir\$productName.exe"
}

$staging = Join-Path $env:TEMP "DOK-V01-release-staging"
if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging | Out-Null

Get-ChildItem $releaseDir -File | Where-Object {
    $_.Extension -in @('.exe', '.dll', '.config')
} | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $staging $_.Name) -Force
}

$nativeSource = Join-Path $releaseDir "native"
if (Test-Path $nativeSource) {
    $nativeTarget = Join-Path $staging "native"
    New-Item -ItemType Directory -Path $nativeTarget -Force | Out-Null
    Copy-Item (Join-Path $nativeSource "*") $nativeTarget -Recurse -Force
    Write-Host "native-Ordner ins Release-Paket kopiert."
}

$tesseractSource = Join-Path $releaseDir "tesseract"
if (Test-Path $tesseractSource) {
    $tesseractTarget = Join-Path $staging "tesseract"
    New-Item -ItemType Directory -Path $tesseractTarget -Force | Out-Null
    Copy-Item (Join-Path $tesseractSource "*") $tesseractTarget -Recurse -Force
    Write-Host "tesseract-Ordner ins Release-Paket kopiert."
}

$iconSource = Join-Path $root "Assets\AppIcon.ico"
if (Test-Path $iconSource) {
    $assetsFolder = Join-Path $staging "Assets"
    New-Item -ItemType Directory -Path $assetsFolder -Force | Out-Null
    Copy-Item $iconSource (Join-Path $assetsFolder "AppIcon.ico") -Force
    Copy-Item $iconSource (Join-Path $staging "AppIcon.ico") -Force
}

$licenseSource = Join-Path $root "LICENSE_DE.txt"
if (Test-Path $licenseSource) {
    Copy-Item $licenseSource (Join-Path $staging "LICENSE_DE.txt") -Force
}

$cerSource = Join-Path $releaseDir "CodeSigning.cer"
if (-not (Test-Path $cerSource)) {
    $cerSource = Join-Path $root "cert\DOK-CodeSigning.cer"
}
if (-not (Test-Path $cerSource)) {
    $cerSource = Join-Path $root "CodeSigning.cer"
}
if (Test-Path $cerSource) {
    Copy-Item $cerSource (Join-Path $staging "CodeSigning.cer") -Force
    Write-Host "CodeSigning.cer: $cerSource"
}

$GitHubUpdatesArePublic = $true
$updateToken = $env:GITHUB_UPDATE_TOKEN
if (-not $GitHubUpdatesArePublic) {
if ([string]::IsNullOrWhiteSpace($updateToken)) {
  $gh = Get-Command gh -ErrorAction SilentlyContinue
  if ($gh) {
    try { $updateToken = (gh auth token 2>$null).Trim() } catch { }
  }
}
if (-not [string]::IsNullOrWhiteSpace($updateToken)) {
    Set-Content -Path (Join-Path $staging "github.token") -Value $updateToken.Trim() -NoNewline -Encoding ASCII
    Write-Host "github.token fuer private Repo-Updates ins Paket aufgenommen."
}
}

$zipPath = Join-Path $releaseRoot $assetName
$legacyZipPath = Join-Path $root "DOK-V01-Release.zip"

if (-not (Test-Path $releaseRoot)) {
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
}

Get-ChildItem $releaseRoot -Force -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -like "$productName*" -or $_.Name -like "DOK-V01-R*"
} | ForEach-Object {
    Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $releaseFolder) {
    Remove-Item $releaseFolder -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null
Copy-Item (Join-Path $staging "*") $releaseFolder -Recurse -Force
Write-Host "Release-Ordner: $releaseFolder"

$signPackageScript = Join-Path $root "sign-package-exes.ps1"
if (Test-Path $signPackageScript) {
    Write-Host "Signiere alle EXEs im Release-Paket (inkl. Tesseract)..."
    & powershell -ExecutionPolicy Bypass -File $signPackageScript -PackageRoot $releaseFolder
    if ($LASTEXITCODE -ne 0) {
        throw "Paket-Signierung fehlgeschlagen."
    }
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
if (Test-Path $legacyZipPath) {
    Remove-Item $legacyZipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($releaseFolder, $zipPath)

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

$notesSourcePath = Join-Path $root "RELEASE_NOTES.md"
$changelog = ""
if (Test-Path $notesSourcePath) {
    $changelog = (Get-Content -Path $notesSourcePath -Raw -Encoding UTF8)
    if ($null -eq $changelog) { $changelog = "" }
    $changelog = $changelog.Trim()
}

if ([string]::IsNullOrWhiteSpace($changelog)) {
    $changelog = @"
## DOK-V01 $Tag

Bitte vor dem Release die Datei RELEASE_NOTES.md mit den Aenderungen fuellen.
"@
}

# SHA256 muss in den Release-Notes stehen (Update-Pruefung). Changelog bleibt in RELEASE_NOTES.md erhalten.
$notes = @"
$changelog

---
SHA256: $hash

Die App prueft diese Pruefsumme und die digitale Signatur vor der Installation.
"@

$generatedNotesPath = Join-Path $root "RELEASE_NOTES.generated.md"
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($generatedNotesPath, $notes, $utf8NoBom)

Write-Host "ZIP: $zipPath"
Write-Host "SHA256: $hash"
Write-Host "Tag: $Tag"

if ($SkipGh) {
    Write-Host "GitHub-Release übersprungen (-SkipGh)."
    $usbFolder = Join-Path $root "USB-Version\$($buildInfo.ProductFolder)"
    explorer.exe $releaseFolder
    if (Test-Path $usbFolder) {
        explorer.exe $usbFolder
    }
    exit 0
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    Write-Warning "GitHub CLI (gh) nicht gefunden. ZIP und SHA256 wurden erstellt - Release manuell hochladen."
    exit 0
}

$releaseArgs = @(
    "release", "create", $Tag,
    $zipPath,
    "--title", "DOK-V01 $Tag",
    "--notes-file", $generatedNotesPath
)
if ($Draft) {
    $releaseArgs += "--draft"
}

& gh @releaseArgs
if ($LASTEXITCODE -ne 0) {
    throw "gh release create fehlgeschlagen."
}

Write-Host "GitHub-Release erstellt: $Tag"

$usbFolder = Join-Path $root "USB-Version\$($buildInfo.ProductFolder)"
explorer.exe $releaseFolder
if (Test-Path $usbFolder) {
    explorer.exe $usbFolder
}
