# Bridge Production Build & Packaging Script
# Usage: powershell -ExecutionPolicy Bypass -File tools/publish-release.ps1

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$DistDir = Join-Path $RootDir "dist"
$WinX64Dir = Join-Path $DistDir "win-x64"
$Version = "1.0.0"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "         BRIDGE WORKSTATION: PRODUCTION RELEASE PIPELINE" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Step 1: Run full test suite
Write-Host "`n[1/5] Running automated verification test suite..." -ForegroundColor Yellow
dotnet test "$RootDir\tests\VpsManager.Core.Tests\VpsManager.Core.Tests.csproj" --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Test suite failed. Release build aborted."
    exit 1
}
Write-Host "All verification tests passed successfully." -ForegroundColor Green

# Step 2: Clean output directory
Write-Host "`n[2/5] Cleaning previous distribution outputs..." -ForegroundColor Yellow
if (Test-Path $WinX64Dir) {
    Remove-Item -Path $WinX64Dir -Recurse -Force
}
New-Item -ItemType Directory -Path $WinX64Dir -Force | Out-Null

# Step 3: Publish self-contained executable
Write-Host "`n[3/5] Compiling self-contained single-file binary..." -ForegroundColor Yellow
dotnet publish "$RootDir\src\VpsManager.Client\VpsManager.Client.csproj" `
    --configuration Release `
    -p:PublishProfile=win-x64-standalone `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed."
    exit 1
}

$ExePath = Join-Path $WinX64Dir "Bridge.exe"
if (-not (Test-Path $ExePath)) {
    # If compiled with old name, rename to Bridge.exe
    $OldExe = Join-Path $WinX64Dir "VpsManager.Client.exe"
    if (Test-Path $OldExe) {
        Rename-Item -Path $OldExe -NewName "Bridge.exe"
    }
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Expected binary $ExePath was not found."
    exit 1
}

$ExeSizeMb = [math]::Round((Get-Item $ExePath).Length / 1MB, 2)
Write-Host "Compiled binary: $ExePath ($ExeSizeMb MB)" -ForegroundColor Green

# Step 4: Package Portable Zip
Write-Host "`n[4/5] Creating portable release package (.zip)..." -ForegroundColor Yellow
$ZipPath = Join-Path $DistDir "Bridge-v$Version-win-x64-portable.zip"
if (Test-Path $ZipPath) {
    Remove-Item -Path $ZipPath -Force
}
Compress-Archive -Path $ExePath -DestinationPath $ZipPath -CompressionLevel Optimal
$ZipSizeMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 2)
Write-Host "Portable package created: $ZipPath ($ZipSizeMb MB)" -ForegroundColor Green

# Step 5: Build Inno Setup Installer
Write-Host "`n[5/5] Building Windows Setup Installer..." -ForegroundColor Yellow

$IsccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$IsccPath = $null
foreach ($candidate in $IsccCandidates) {
    if (Test-Path $candidate) {
        $IsccPath = $candidate
        break
    }
}

if (-not $IsccPath) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) {
        $IsccPath = $cmd.Source
    }
}

$InstallerPath = Join-Path $DistDir "Bridge-Setup-v$Version-win-x64.exe"

if ($IsccPath) {
    Write-Host "Found Inno Setup compiler: $IsccPath" -ForegroundColor DarkGray
    $IssScript = Join-Path $RootDir "tools\installer\bridge-setup.iss"
    & "$IsccPath" "$IssScript"
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Inno Setup compilation encountered an issue."
    } elseif (Test-Path $InstallerPath) {
        $InstallerSizeMb = [math]::Round((Get-Item $InstallerPath).Length / 1MB, 2)
        Write-Host "Setup installer created: $InstallerPath ($InstallerSizeMb MB)" -ForegroundColor Green
    }
} else {
    Write-Warning "Inno Setup (ISCC.exe) was not found. Skipping installer generation. Portable zip is ready."
}

# Final Summary
Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "             RELEASE PIPELINE COMPLETED" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " 1. Single-File Executable:" -ForegroundColor White
Write-Host "    $ExePath ($ExeSizeMb MB)" -ForegroundColor Cyan
Write-Host " 2. Portable Zip Distribution:" -ForegroundColor White
Write-Host "    $ZipPath ($ZipSizeMb MB)" -ForegroundColor Cyan

if (Test-Path $InstallerPath) {
    $InstallerSizeMb = [math]::Round((Get-Item $InstallerPath).Length / 1MB, 2)
    Write-Host " 3. Windows Setup Installer:" -ForegroundColor White
    Write-Host "    $InstallerPath ($InstallerSizeMb MB)" -ForegroundColor Cyan
}

Write-Host "==========================================================" -ForegroundColor Green
