param(
    [string]$Source = "dist\tbt.memory.cleaner"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root $Source

if (-not (Test-Path (Join-Path $source "plugin.json"))) { throw "missing package: $source" }
if (Get-Process -Name "ThinkBookToolkit" -ErrorAction SilentlyContinue) {
    throw "ThinkBook Toolkit is running. Exit it first (the page DLL is loaded and locked)."
}

$target = Join-Path $env:USERPROFILE ".thinkbook_toolkit\plugins\tbt.memory.cleaner"
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $source "*") $target -Force

Write-Host "installed -> $target"
Get-ChildItem $target | Select-Object Name, Length | Format-Table -AutoSize | Out-String -Width 100 | Write-Host
Write-Host "Next: start Toolkit, open Plugins, enable 'Memory cleaner' (fingerprint changed, approval is required again)."
