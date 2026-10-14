param(
    [string]$SourceInstall = "",
    [string]$ReleaseTag = "v1.1-R23"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

if ([string]::IsNullOrWhiteSpace($SourceInstall)) {
    . (Join-Path $root "Get-DokBuildRevision.ps1")
    $buildInfo = Get-DokRevisionFromAssembly -Root $root
    $SourceInstall = Join-Path $root ("USB-Version\" + $buildInfo.ProductFolder)
}

$testRoot = Join-Path $env:TEMP "DOK-V01-UpdateTest"
$installDir = Join-Path $testRoot "install"
$stagingDir = Join-Path $testRoot "staging"
$zipPath = Join-Path $testRoot "DOK-V01-Release.zip"

Write-Host "=== DOK-V01 Test-Update ===" -ForegroundColor Cyan
Write-Host "Quelle (alt): $SourceInstall"
Write-Host "Ziel-Release: $ReleaseTag"

if (-not (Test-Path (Join-Path $SourceInstall "DOK-V01.exe"))) {
    throw "Quell-Installation nicht gefunden: $SourceInstall"
}

if (Test-Path $testRoot) {
    Remove-Item $testRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -Path (Join-Path $SourceInstall "*") -Destination $installDir -Recurse -Force

$before = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $installDir "DOK-V01.exe"))
Write-Host "Installierte Version vor Update: $($before.ProductVersion)"

Write-Host "GitHub-Release wird geladen..."
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/Acid31-31/Tesla-Auftragsbearbeitung-und-Druck/releases/tags/$ReleaseTag" -Headers @{ "User-Agent" = "DOK-V01-Test" }
$asset = $release.assets | Where-Object { $_.name -eq "DOK-V01-Release.zip" } | Select-Object -First 1
if (-not $asset) { throw "Release-ZIP nicht gefunden." }

$shaMatch = [regex]::Match($release.body, 'SHA-?256\s*[:=]\s*([0-9a-fA-F]{64})')
if (-not $shaMatch.Success) { throw "SHA256 in Release-Notes fehlt." }
$expectedSha = $shaMatch.Groups[1].Value.ToLowerInvariant()

Write-Host "Download: $($asset.browser_download_url)"
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UserAgent "DOK-V01-Test"

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $expectedSha) {
    throw "SHA256 ungueltig. Erwartet: $expectedSha Erhalten: $hash"
}
Write-Host "SHA256 OK" -ForegroundColor Green

if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $stagingDir)

$stagedExe = Get-ChildItem -Path $stagingDir -Filter "DOK-V01.exe" -Recurse | Select-Object -First 1
if (-not $stagedExe) { throw "DOK-V01.exe im ZIP nicht gefunden." }
$stagedRoot = $stagedExe.Directory.FullName

Write-Host "Update wird angewendet (Staging -> Test-Installation)..."
$updaterExe = Join-Path $stagedRoot "DOK-V01.exe"
$args = @('--apply-update', $stagedRoot, $installDir, '0')
$p = Start-Process -FilePath $updaterExe -ArgumentList $args -PassThru
$deadline = (Get-Date).AddMinutes(3)
while (-not $p.HasExited -and (Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 1
}
if (-not $p.HasExited) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    throw "Updater-Timeout nach 3 Minuten."
}

$after = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $installDir "DOK-V01.exe"))
Write-Host "Installierte Version nach Update: $($after.ProductVersion)" -ForegroundColor Green

if ($after.ProductVersion -notmatch 'R23') {
    throw "Test fehlgeschlagen: Erwartet R23, erhalten $($after.ProductVersion)"
}

Write-Host ""
Write-Host "TEST-UPDATE ERFOLGREICH" -ForegroundColor Green
Write-Host "Test-Installation: $installDir"
