param(
    [string]$Configuration = "Release",
    [string]$TbtInstallDir = "C:\Program Files\ThinkBook Toolkit"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root "build.ps1") -Configuration $Configuration -TbtInstallDir $TbtInstallDir
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$logic = Join-Path $root "src\TbtMemoryCleaner\bin\$Configuration\net9.0"
$ui = Join-Path $root "src\TbtMemoryCleaner.Ui\bin\$Configuration\net9.0-windows"
$stage = Join-Path $root "dist\tbt.memory.cleaner"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$files = @(
    @{ From = (Join-Path $root "plugin\plugin.json"); To = "plugin.json" },
    @{ From = (Join-Path $logic "TbtMemoryCleaner.dll"); To = "TbtMemoryCleaner.dll" },
    @{ From = (Join-Path $logic "TbtMemoryCleaner.deps.json"); To = "TbtMemoryCleaner.deps.json" },
    @{ From = (Join-Path $logic "TbtMemoryCleaner.Core.dll"); To = "TbtMemoryCleaner.Core.dll" },
    @{ From = (Join-Path $ui "TbtMemoryCleaner.Ui.dll"); To = "TbtMemoryCleaner.Ui.dll" },
    @{ From = (Join-Path $ui "TbtMemoryCleaner.Ui.deps.json"); To = "TbtMemoryCleaner.Ui.deps.json" }
)

foreach ($item in $files) {
    if (-not (Test-Path $item.From)) { throw "missing build output: $($item.From)" }
    Copy-Item $item.From (Join-Path $stage $item.To) -Force
}

foreach ($deps in @("TbtMemoryCleaner.deps.json", "TbtMemoryCleaner.Ui.deps.json")) {
    $text = Get-Content (Join-Path $stage $deps) -Raw
    if ($text -match "ThinkBookToolkit\.") { throw "$deps must not reference host contract assemblies" }
    if ($deps -eq "TbtMemoryCleaner.deps.json" -and $text -notmatch "TbtMemoryCleaner.Core") { throw "logic deps.json must declare TbtMemoryCleaner.Core" }
}

$unexpected = Get-ChildItem $stage | Where-Object { $_.Extension -in @(".pdb", ".xml") -or $_.Name -like "*.runtimeconfig.json" }
if ($unexpected) { throw "unexpected files in package: $($unexpected.Name -join ', ')" }

$zip = Join-Path $root "dist\tbt.memory.cleaner-$Configuration.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "package: $zip"
Write-Host "sha256 : $hash"
Get-ChildItem $stage | Select-Object Name, Length | Format-Table -AutoSize | Out-String -Width 120 | Write-Host
