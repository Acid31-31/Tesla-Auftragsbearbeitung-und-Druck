param(
    [switch]$RestartExplorer
)

$ErrorActionPreference = "SilentlyContinue"

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ShellNotify {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
"@

# 0x8000000 = SHCNE_ASSOCCHANGED, 0x1000 = SHCNF_IDLIST
[ShellNotify]::SHChangeNotify(0x08000000, 0x00001000, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null

$ie4uinit = Join-Path ${env:WINDIR} "System32\ie4uinit.exe"
if (Test-Path $ie4uinit) {
    Start-Process $ie4uinit -ArgumentList "-ClearIconCache" -WindowStyle Hidden -Wait
    Start-Process $ie4uinit -ArgumentList "-show" -WindowStyle Hidden -Wait
}

$explorerCache = Join-Path $env:LOCALAPPDATA "Microsoft\Windows\Explorer"
Get-ChildItem $explorerCache -Filter "iconcache*" -Force -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem $explorerCache -Filter "thumbcache*" -Force -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

if ($RestartExplorer) {
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Start-Process explorer
}

Write-Host "Explorer-Icon-Cache wurde aktualisiert."
