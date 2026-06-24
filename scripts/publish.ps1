<#
.SYNOPSIS
  Developer build script for TimeLens. Builds the Svelte dashboard,
  publishes the .NET tray app (Native AOT), and optionally runs Inno Setup.
  End users should download the pre-built installer from GitHub Releases.
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Config = "Release",
    [switch]$SkipDashboard,
    [switch]$Installer,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dashboardDir = "$root\src\TimeLens.Dashboard"
$trayAppDir = "$root\src\TimeLens.TrayApp"
$distDir = "$dashboardDir\dist"
$publishDir = "$trayAppDir\bin\$Config\net9.0\win-x64\publish"
$exePath = "$publishDir\TimeLens.TrayApp.exe"

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
        npm ci 2>&1 | Out-Null
        npm run build 2>&1 | Out-Null
        & $ok "Dashboard built"
    } catch {
        & $fail "Dashboard build failed"
        Pop-Location
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
    & $ok "Native AOT publish complete"
} catch {
    & $fail "dotnet publish failed"
    exit 1
}

# --- Copy dashboard ---
& $header "=== Bundling dashboard ==="
if (Test-Path "$publishDir\dashboard") {
    Remove-Item -Recurse -Force "$publishDir\dashboard"
}
if (Test-Path $distDir) {
    Copy-Item -Recurse $distDir "$publishDir\dashboard"
    & $ok "Dashboard copied to publish output"
} else {
    Write-Host "  Dashboard dist/ not found — exe will use embedded resources" -ForegroundColor DarkGray
}

# --- Deploy to root (double-click ready) ---
& $header "=== Deploying to root ==="
Remove-Item -Force "$root\*.dll", "$root\*.json", "$root\*.pdb", "$root\*.ico", "$root\*.csv" -ErrorAction SilentlyContinue
Remove-Item -Force "$root\TimeLens.exe", "$root\TimeLens.TrayApp.exe" -ErrorAction SilentlyContinue
if (Test-Path "$root\dashboard") { Remove-Item -Recurse -Force "$root\dashboard" }
Copy-Item -Force "$publishDir\TimeLens.TrayApp.exe" "$root\TimeLens.exe"
if (Test-Path $distDir) { Copy-Item -Recurse -Force $distDir "$root\dashboard" }
Copy-Item -Force "$publishDir\categories.csv" "$root\categories.csv" -ErrorAction SilentlyContinue
& $ok "Root TimeLens.exe ready"

# --- Summary ---
& $header "=== Build summary ==="
$exeItem = Get-Item $exePath -ErrorAction SilentlyContinue
$exeSizeMB = if ($exeItem) { [math]::Round($exeItem.Length / 1MB, 1) } else { 0 }
$dashboardSizeMB = 0
$dashboardFiles = 0
if (Test-Path "$publishDir\dashboard") {
    $dashboardSizeMB = [math]::Round((Get-ChildItem "$publishDir\dashboard" -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    $dashboardFiles = (Get-ChildItem "$publishDir\dashboard" -Recurse -File).Count
}
Write-Host "  Exe:         $exePath" -ForegroundColor White
Write-Host "  Exe size:    ${exeSizeMB} MB" -ForegroundColor White
Write-Host "  Dashboard:   ${dashboardSizeMB} MB ($dashboardFiles files)" -ForegroundColor White
Write-Host "  Total:       $([math]::Round($exeSizeMB + $dashboardSizeMB, 1)) MB" -ForegroundColor White
Write-Host "  Config:      $Config" -ForegroundColor White
Write-Host "  Output:      $publishDir" -ForegroundColor White

# --- Inno Setup ---
if ($Installer) {
    & $header "=== Building installer ==="
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    $issFile = "$root\scripts\TimeLens.iss"

    if (-not (Test-Path $iscc)) {
        & $fail "Inno Setup not found at: $iscc"
        Write-Host "       Install via: choco install innosetup" -ForegroundColor Yellow
        Write-Host "       Or download: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    } elseif (-not (Test-Path $issFile)) {
        & $fail "Installer script not found: $issFile"
    } else {
        try {
            $version = if ($exeItem -and $exeItem.VersionInfo.FileVersion) { $exeItem.VersionInfo.FileVersion } else { "0.0.0" }
            & $iscc $issFile /DAppVersion=$version
            $setupExe = "$root\dist\TimeLens-Setup-$version.exe"
            if (Test-Path $setupExe) {
                $setupSize = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
                & $ok "Installer built: TimeLens-Setup-$version.exe (${setupSize} MB)"
            }
        } catch {
            & $fail "Inno Setup build failed: $_"
        }
    }
}

# --- Launch ---
if ($Launch) {
    & $header "=== Launching ==="
    if (Test-Path $exePath) {
        Start-Process -FilePath $exePath -WorkingDirectory $publishDir
        & $ok "TimeLens started"
        Write-Host "  Dashboard: http://127.0.0.1:47821/" -ForegroundColor Cyan
    } else {
        & $fail "Exe not found — build may have failed"
    }
}

Write-Host ""
