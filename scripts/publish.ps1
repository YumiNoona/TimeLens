<#
.SYNOPSIS
  Developer build script for TimeLens. Builds the Svelte dashboard,
  publishes the .NET tray app as one self-contained Native AOT executable.
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Release",
    [switch]$SkipDashboard,
    [switch]$SkipInstaller,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dashboardDir = "$root\src\TimeLens.Dashboard"
$trayAppDir = "$root\src\TimeLens.TrayApp"
$publishDir = "$trayAppDir\bin\$Config\net9.0\win-x64\publish"
$exePath = "$publishDir\TimeLens.TrayApp.exe"
$rootExePath = "$root\TimeLens.exe"
$installerScript = "$root\installer\TimeLens.iss"
$installerOutput = "$root\installer\output\TimeLens-Setup.exe"
$rootInstallerPath = "$root\TimeLens-Setup.exe"

$header = { Write-Host "`n$($args[0])" -ForegroundColor Cyan }
$ok = { Write-Host "  [ok] $($args[0])" -ForegroundColor Green }
$fail = { Write-Host "  [FAIL] $($args[0])" -ForegroundColor Red }

# --- Tool checks ---
& $header "=== Checking tools ==="

$nodeVersion = $null
$npmVersion = $null
$dotnetVersion = $null

try {
    $nodeVersion = node --version 2>&1
    & $ok "node $nodeVersion"
} catch {
    & $fail "node is not installed"
    Write-Host "       Download: https://nodejs.org (LTS recommended)" -ForegroundColor Yellow
    exit 1
}

try {
    $npmVersion = npm --version 2>&1
    & $ok "npm v$npmVersion"
} catch {
    & $fail "npm is not installed (should come with Node.js)"
    exit 1
}

try {
    $dotnetOutput = dotnet --version 2>&1
    $dotnetVersion = $dotnetOutput -replace '\s+', ' '
    & $ok ".NET SDK $dotnetVersion"
} catch {
    & $fail ".NET SDK is not installed"
    Write-Host "       Download: https://dotnet.microsoft.com/en-us/download/dotnet/9.0" -ForegroundColor Yellow
    exit 1
}

# --- Build dashboard ---
if (-not $SkipDashboard) {
    & $header "=== Building Svelte dashboard ==="
    Push-Location $dashboardDir
    try {
        $previousErrorPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci exited with code $LASTEXITCODE" }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build exited with code $LASTEXITCODE" }
        $ErrorActionPreference = $previousErrorPreference
        & $ok "Dashboard built"
    } catch {
        $ErrorActionPreference = "Stop"
        & $fail "Dashboard build failed"
        Write-Host "       $($_.Exception.Message)" -ForegroundColor Yellow
        exit 1
    } finally {
        Pop-Location
    }
} else {
    Write-Host "  Skipping dashboard build (--SkipDashboard)" -ForegroundColor DarkGray
}

# --- Publish .NET ---
& $header "=== Publishing TimeLens (Native AOT, $Config) ==="
try {
    dotnet publish "$trayAppDir" -c $Config -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish exited with code $LASTEXITCODE" }
    & $ok "Native AOT publish complete"
} catch {
    & $fail "dotnet publish failed"
    Write-Host "       $($_.Exception.Message)" -ForegroundColor Yellow
    exit 1
}

$innoCompiler = $null
if (-not $SkipInstaller) {
    $innoCandidates = @(
        (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $innoCompiler = $innoCandidates | Select-Object -First 1
    if (-not $innoCompiler) {
        & $fail "Inno Setup 6 is not installed"
        Write-Host "       Install with: winget install --id JRSoftware.InnoSetup -e" -ForegroundColor Yellow
        exit 1
    }
    & $ok "Inno Setup $innoCompiler"
}

# --- Deploy to root (double-click ready) ---
& $header "=== Deploying to root ==="
if (Test-Path "$root\dashboard") { Remove-Item -Recurse -Force "$root\dashboard" }
Copy-Item -Force "$publishDir\TimeLens.TrayApp.exe" "$root\TimeLens.exe"
& $ok "Standalone root TimeLens.exe ready"

# --- Build installer ---
if (-not $SkipInstaller) {
    & $header "=== Building Windows installer ==="
    [xml]$trayProject = Get-Content "$trayAppDir\TimeLens.TrayApp.csproj"
    $appVersion = [string]$trayProject.Project.PropertyGroup.Version
    & $innoCompiler "/DAppVersion=$appVersion" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup exited with code $LASTEXITCODE" }
    Copy-Item -Force $installerOutput $rootInstallerPath
    & $ok "TimeLens-Setup.exe ready"
} else {
    Write-Host "  Skipping installer build (--SkipInstaller)" -ForegroundColor DarkGray
}

# --- Summary ---
& $header "=== Build summary ==="
$exeItem = Get-Item $exePath -ErrorAction SilentlyContinue
$exeSizeMB = if ($exeItem) { [math]::Round($exeItem.Length / 1MB, 1) } else { 0 }
Write-Host "  Exe:         $exePath" -ForegroundColor White
Write-Host "  Exe size:    ${exeSizeMB} MB" -ForegroundColor White
Write-Host "  Packaging:   Single self-contained executable" -ForegroundColor White
Write-Host "  Config:      $Config" -ForegroundColor White
Write-Host "  Output:      $publishDir" -ForegroundColor White
if (-not $SkipInstaller) {
    $installerItem = Get-Item $rootInstallerPath -ErrorAction SilentlyContinue
    $installerSizeMB = if ($installerItem) { [math]::Round($installerItem.Length / 1MB, 1) } else { 0 }
    Write-Host "  Installer:   $rootInstallerPath (${installerSizeMB} MB)" -ForegroundColor White
}

# --- Launch ---
if ($Launch) {
    & $header "=== Launching ==="
    if (Test-Path $rootExePath) {
        Start-Process -FilePath $rootExePath -WorkingDirectory $root
        & $ok "TimeLens started"
        Write-Host "  Dashboard: http://127.0.0.1:47821/" -ForegroundColor Cyan
    } else {
        & $fail "Exe not found — build may have failed"
    }
}

Write-Host ""
