# Sero C2 - Build & package for distribution
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
# Or:    double-click build.bat

$ErrorActionPreference = "Stop"
$Root   = $PSScriptRoot
$Server = Join-Path $Root "server"
$Out    = Join-Path $Root "dist"

function Write-Step($msg) { Write-Host "[*] $msg" -ForegroundColor Cyan }
function Write-OK($msg)   { Write-Host "[+] $msg" -ForegroundColor Green }
function Write-Err($msg)  { Write-Host "[!] $msg" -ForegroundColor Red; exit 1 }

Write-Host "=== Sero C2 - Build ===" -ForegroundColor Yellow

# Clean output
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Path $Out | Out-Null

# Build server (self-contained, no runtime needed on target)
Write-Step "Building server (net9.0-windows)..."
$csproj = Get-ChildItem $Server -Filter "*.csproj" | Select-Object -First 1
if (-not $csproj) { Write-Err "No .csproj found in server/" }

$tmpOut = Join-Path $env:TEMP "sero_publish_$(Get-Random)"
dotnet publish $csproj.FullName `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:PublishTrimmed=false `
    -o $tmpOut

if ($LASTEXITCODE -ne 0) { Write-Err "Server build failed" }

$serverExe = Get-ChildItem $tmpOut -Filter "SeroServer.exe" | Select-Object -First 1
if (-not $serverExe) {
    $serverExe = Get-ChildItem $tmpOut -Filter "*.exe" | Select-Object -First 1
}
if (-not $serverExe) { Write-Err "Server exe not found in publish output" }
Copy-Item $serverExe.FullName (Join-Path $Out "SeroServer.exe")
Write-OK "Server -> dist\SeroServer.exe"

# Copy stub source (needed by builder at runtime)
$stubSrc = Join-Path $Root "stub"
$stubOut = Join-Path $Out "stub"
if (Test-Path $stubSrc) {
    New-Item -ItemType Directory -Path $stubOut | Out-Null
    Copy-Item (Join-Path $stubSrc "*.cs")     $stubOut -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $stubSrc "*.csproj") $stubOut -ErrorAction SilentlyContinue
    Write-OK "Stub sources -> dist\stub\"
}

# Copy Stubs folder (loader.cpp)
$loaderSrc = Join-Path $Server "Stubs\loader.cpp"
if (Test-Path $loaderSrc) {
    $stubsOut = Join-Path $Out "Stubs"
    New-Item -ItemType Directory -Path $stubsOut | Out-Null
    Copy-Item $loaderSrc $stubsOut
    Write-OK "Loader -> dist\Stubs\"
}

# Copy icon
Get-ChildItem $Root -Filter "*.ico" | ForEach-Object { Copy-Item $_.FullName $Out -ErrorAction SilentlyContinue }

# Cleanup temp
Remove-Item $tmpOut -Recurse -Force -ErrorAction SilentlyContinue

Write-OK "Build complete -> dist\"
Write-Host ""
Get-ChildItem $Out -Recurse | ForEach-Object {
    Write-Host ("  " + $_.FullName.Replace($Out + "\", ""))
}
Write-Host ""
Write-Host "No prerequisites on target machine (self-contained)." -ForegroundColor Gray
