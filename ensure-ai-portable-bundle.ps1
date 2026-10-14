# Stellt sicher, dass AI_DOK-V01 vollstaendig ist (ollama.exe + Modell) - nur auf dem Build-PC.
# Auf anderen PCs: Ordner AI_DOK-V01 mitkopieren, kein Internet noetig.
param(
    [string]$ProjectRoot = "",
    [string]$AiRoot = "",
    [string]$Model = "tinyllama",
    [string]$VisionModel = "moondream",
    [string[]]$CopyTo = @()
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}

if ([string]::IsNullOrWhiteSpace($AiRoot)) {
    $driveRoot = "Z:\DOK-V01"
    if (Test-Path $driveRoot) {
        $AiRoot = Join-Path $driveRoot "AI_DOK-V01"
    }
    else {
        $AiRoot = Join-Path $ProjectRoot "USB-Version\AI_DOK-V01"
    }
}

function Test-PortableAiModelPresent {
    param(
        [string]$Root,
        [string]$ModelName
    )

    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path $Root)) {
        return $false
    }

    $parts = $ModelName -split ":", 2
    $name = $parts[0]
    $tag = if ($parts.Length -gt 1) { $parts[1] } else { "latest" }
    $manifest = Join-Path $Root "models\manifests\registry.ollama.ai\library\$name\$tag"
    return Test-Path $manifest
}

function Test-PortableAiLayout {
    param([string]$Root)

    if ([string]::IsNullOrWhiteSpace($Root)) { return $false }
    $ollama = Join-Path $Root "ollama.exe"
    $llamaServer = Join-Path $Root "lib\ollama\llama-server.exe"
    return (Test-Path $ollama) -and (Test-Path $llamaServer)
}

Write-Host "KI-Ordner pruefen: $AiRoot"

$setupScript = Join-Path $ProjectRoot "setup-ai-portable.ps1"
$needsSetup = -not (Test-PortableAiLayout $AiRoot) -or -not (Test-PortableAiModelPresent $AiRoot $Model) -or -not (Test-PortableAiModelPresent $AiRoot $VisionModel)

if ($needsSetup) {
    if (-not (Test-Path $setupScript)) {
        throw "setup-ai-portable.ps1 nicht gefunden"
    }

    Write-Host "KI-Ordner unvollstaendig - richte ein (einmalig auf Build-PC, ca. 2 GB)..."
    & powershell -ExecutionPolicy Bypass -File $setupScript -TargetRoot $AiRoot -Model $Model -VisionModel $VisionModel
    if ($LASTEXITCODE -ne 0) { throw "setup-ai-portable.ps1 fehlgeschlagen." }
}

if (-not (Test-PortableAiLayout $AiRoot)) {
    throw "KI-Ordner unvollstaendig: ollama.exe oder lib\ollama\llama-server.exe fehlt in $AiRoot"
}

if (-not (Test-PortableAiModelPresent $AiRoot $VisionModel)) {
    throw "Vision-Modell $VisionModel fehlt in $AiRoot\models"
}

$sizeGb = [math]::Round((Get-ChildItem $AiRoot -Recurse -File -ErrorAction SilentlyContinue |
    Measure-Object Length -Sum).Sum / 1GB, 2)
Write-Host "KI-Ordner bereit: $AiRoot ($sizeGb GB, Modell $Model + Vision $VisionModel)"

foreach ($dest in $CopyTo) {
    if ([string]::IsNullOrWhiteSpace($dest)) { continue }

    Write-Host "KI-Ordner kopieren nach: $dest"
    if (-not (Test-Path $dest)) {
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
    }

    & robocopy $AiRoot $dest /MIR /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "Robocopy fehlgeschlagen ($LASTEXITCODE): $AiRoot -> $dest"
    }
}
