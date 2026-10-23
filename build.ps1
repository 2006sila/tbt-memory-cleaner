param(
    [string]$Configuration = "Release",
    [string]$TbtInstallDir = "C:\Program Files\ThinkBook Toolkit"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

foreach ($project in @(
    "src\TbtMemoryCleaner.Core\TbtMemoryCleaner.Core.csproj",
    "src\TbtMemoryCleaner\TbtMemoryCleaner.csproj",
    "src\TbtMemoryCleaner.Ui\TbtMemoryCleaner.Ui.csproj",
    "tools\Tests\Tests.csproj",
    "tools\HostSim\HostSim.csproj",
    "tools\UiRender\UiRender.csproj",
    "tools\LiveTest\LiveTest.csproj",
    "tools\ManifestCheck\ManifestCheck.csproj")) {
    $path = Join-Path $root $project
    if (-not (Test-Path $path)) { continue }
    Write-Host "==> $project"
    & dotnet msbuild $path -t:Restore,Build -p:Configuration=$Configuration -p:TbtInstallDir="$TbtInstallDir" -m:1 -v:m
    if ($LASTEXITCODE -ne 0) { throw "build failed: $project" }
}
