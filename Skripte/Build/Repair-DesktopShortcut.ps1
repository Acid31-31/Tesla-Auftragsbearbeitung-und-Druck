param(
    [string]$TargetExe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "bin\Debug\DOK-V01.exe")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $TargetExe)) {
    throw "Programm nicht gefunden: $TargetExe"
}

$baseDir = Split-Path -Parent $TargetExe
$iconPath = Join-Path $baseDir "AppIcon.ico"
if (-not (Test-Path $iconPath)) {
    $iconPath = Join-Path $baseDir "Assets\AppIcon.ico"
}
if (-not (Test-Path $iconPath)) {
    $iconPath = $TargetExe
}

$wsh = New-Object -ComObject WScript.Shell
$desktopPaths = @(
    [Environment]::GetFolderPath("Desktop"),
    [Environment]::GetFolderPath("CommonDesktopDirectory")
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

$shortcutName = "DOK-V01.lnk"
foreach ($desktop in $desktopPaths) {
    $shortcutPath = Join-Path $desktop $shortcutName
    try {
        $shortcut = $wsh.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $TargetExe
        $shortcut.WorkingDirectory = $baseDir
        $shortcut.IconLocation = if ($iconPath.EndsWith(".ico")) { "$iconPath,0" } else { "$TargetExe,0" }
        $shortcut.Description = "DOK-V01 starten"
        $shortcut.Save()
        Write-Host "Verknuepfung aktualisiert: $shortcutPath"
    }
    catch {
        Write-Warning "Verknuepfung konnte nicht gespeichert werden: $shortcutPath ($($_.Exception.Message))"
    }
}

Write-Host "Icon: $iconPath"
