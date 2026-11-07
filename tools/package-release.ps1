param(
    [string]$FrontendDir = ""
)

$ErrorActionPreference = "Stop"
$BackendRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$ReleaseRoot = Join-Path $BackendRoot "release"
$ArtifactsRoot = Join-Path $BackendRoot "artifacts"
$Version = "1.0.0"
$ZipPath = Join-Path $ArtifactsRoot "AC-Display-Unit-v$Version-win-x64.zip"
$InstallerCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

Write-Host "Building production bundle..."
$buildArgs = @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $BackendRoot "tools\build-production.ps1"))
if (-not [string]::IsNullOrWhiteSpace($FrontendDir)) {
    $buildArgs += @("-FrontendDir", $FrontendDir)
}
& powershell @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Production bundle build failed." }

Remove-Item -LiteralPath $ArtifactsRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $ArtifactsRoot | Out-Null

Write-Host "Creating portable ZIP..."
Compress-Archive -Path (Join-Path $ReleaseRoot "*") -DestinationPath $ZipPath -CompressionLevel Optimal -Force

if (-not (Test-Path -LiteralPath $InstallerCompiler)) {
    throw "Inno Setup compiler not found at $InstallerCompiler."
}

Write-Host "Creating Windows installer..."
& $InstallerCompiler (Join-Path $BackendRoot "installer\AcDisplayUnit.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

Write-Host ""
Write-Host "Release artifacts created at: $ArtifactsRoot"
