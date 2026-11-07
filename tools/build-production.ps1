param(
    [string]$FrontendDir = ""
)

$ErrorActionPreference = "Stop"
$BackendRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

if ([string]::IsNullOrWhiteSpace($FrontendDir)) {
    $FrontendDir = Resolve-Path (Join-Path $BackendRoot "..\ac-hud")
} else {
    $FrontendDir = Resolve-Path $FrontendDir
}

$ReleaseRoot = Join-Path $BackendRoot "release"
$BuildRoot = Join-Path $BackendRoot ".build-production"
$WebOutput = Join-Path $ReleaseRoot "web"
$BackendOutput = Join-Path $ReleaseRoot "backend"
$CollectorOutput = Join-Path $ReleaseRoot "collector"
$NodeOutput = Join-Path $ReleaseRoot "node"
$AssetsOutput = Join-Path $ReleaseRoot "assets"

Write-Host "Building frontend..."
& npm --prefix $FrontendDir run build
if ($LASTEXITCODE -ne 0) { throw "Frontend build failed." }

Remove-Item -LiteralPath $ReleaseRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $BuildRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $WebOutput, $BackendOutput, $CollectorOutput, $NodeOutput, $AssetsOutput, $BuildRoot | Out-Null

Write-Host "Publishing telemetry collector..."
& dotnet publish (Join-Path $BackendRoot "collector\AcTelemetryCollector.csproj") `
    -c Release -r win-x64 --self-contained true -o $CollectorOutput
if ($LASTEXITCODE -ne 0) { throw "Collector publish failed." }

Write-Host "Publishing launcher..."
& dotnet publish (Join-Path $BackendRoot "launcher\AcHudLauncher.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $BuildRoot
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed." }

Write-Host "Assembling portable bundle..."
Copy-Item -Path (Join-Path $FrontendDir "dist\*") -Destination $WebOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $BackendRoot "src") -Destination $BackendOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $BackendRoot "data") -Destination $BackendOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $BackendRoot "package.json") -Destination $BackendOutput -Force
Copy-Item -Path (Join-Path $BackendRoot "assets\*") -Destination $AssetsOutput -Recurse -Force

$nodeCommand = Get-Command node.exe -ErrorAction Stop
Copy-Item -LiteralPath $nodeCommand.Source -Destination (Join-Path $NodeOutput "node.exe") -Force
$nodeLicense = Join-Path (Split-Path $nodeCommand.Source -Parent) "LICENSE"
if (Test-Path -LiteralPath $nodeLicense) {
    Copy-Item -LiteralPath $nodeLicense -Destination $NodeOutput -Force
}

Copy-Item -LiteralPath (Join-Path $BuildRoot "AcHud.exe") -Destination (Join-Path $ReleaseRoot "AcHud.exe") -Force
Copy-Item -LiteralPath (Join-Path $BackendRoot "README.md") -Destination (Join-Path $ReleaseRoot "BACKEND-README.md") -Force

Write-Host ""
Write-Host "Production bundle created at: $ReleaseRoot"
Write-Host "Run: $ReleaseRoot\AcHud.exe"
