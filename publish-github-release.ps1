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

$productName = "DOK-V01"
$assetName = "DOK-V01-Release.zip"
$projectFile = Join-Path $root "PDF-umbenenen.csproj"
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
    $msbuild = "msbuild"
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
    $assemblyInfo = Get-Content (Join-Path $root "Properties\AssemblyInfo.cs") -Raw
    if ($assemblyInfo -match 'AssemblyVersion\("([^"]+)"\)') {
        $parts = $Matches[1].Split('.')
        $Tag = "v$($parts[0]).$($parts[1])-R$($parts[2])"
    }
    else {
        throw "Release-Tag nicht angegeben und AssemblyVersion nicht gefunden."
    }
}

if (-not $SkipBuild) {
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

$iconSource = Join-Path $root "Assets\AppIcon.ico"
if (Test-Path $iconSource) {
    $assetsFolder = Join-Path $staging "Assets"
    New-Item -ItemType Directory -Path $assetsFolder -Force | Out-Null
    Copy-Item $iconSource (Join-Path $assetsFolder "AppIcon.ico") -Force
    Copy-Item $iconSource (Join-Path $staging "AppIcon.ico") -Force
}

$cerSource = Join-Path $root "CodeSigning.cer"
if (Test-Path $cerSource) {
    Copy-Item $cerSource (Join-Path $staging "CodeSigning.cer") -Force
}

$zipPath = Join-Path $root $assetName
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath)

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$notes = "## DOK-V01 $Tag`n`nAutomatisches Update-Paket fuer die Desktop-App.`n`nSHA256: $hash`n`nDie App prueft diese Pruefsumme und die digitale Signatur vor der Installation."
Set-Content -Path (Join-Path $root "RELEASE_NOTES.md") -Value $notes -Encoding UTF8

Write-Host "ZIP: $zipPath"
Write-Host "SHA256: $hash"
Write-Host "Tag: $Tag"

if ($SkipGh) {
    Write-Host "GitHub-Release übersprungen (-SkipGh)."
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
    "--notes", $notes
)
if ($Draft) {
    $releaseArgs += "--draft"
}

& gh @releaseArgs
if ($LASTEXITCODE -ne 0) {
    throw "gh release create fehlgeschlagen."
}

Write-Host "GitHub-Release erstellt: $Tag"
