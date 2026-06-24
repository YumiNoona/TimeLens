<#
.SYNOPSIS
  One-click installer for TimeLens. Builds, installs to %LOCALAPPDATA%\TimeLens,
  creates Start Menu shortcut, and launches the tray app.
#>

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSCommandPath
$appDir = "$env:LOCALAPPDATA\TimeLens"
$exePath = "$appDir\TimeLens.TrayApp.exe"

Write-Host "=== TimeLens Installer ===" -ForegroundColor Cyan
Write-Host ""

# --- Check .NET 9 SDK ---
$dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "[!] .NET SDK not found." -ForegroundColor Yellow
    Write-Host "    Download from: https://dotnet.microsoft.com/en-us/download/dotnet/9.0"
    Write-Host "    Install the .NET 9 SDK, then run this script again."
    $dl = Read-Host "Open download page now? (Y/n)"
    if ($dl -ne "n") { Start-Process "https://dotnet.microsoft.com/en-us/download/dotnet/9.0" }
    exit 1
}

$ver = dotnet --version
if (-not ($ver -like "9.*")) {
    Write-Host "[!] .NET $ver found, but .NET 9 is required." -ForegroundColor Yellow
    Write-Host "    Download: https://dotnet.microsoft.com/en-us/download/dotnet/9.0"
    exit 1
}
Write-Host "[✓] .NET SDK $ver" -ForegroundColor Green

# --- Build Svelte dashboard ---
Write-Host ""
Write-Host "--- Building dashboard ---" -ForegroundColor Cyan
Push-Location "$root\src\TimeLens.Dashboard"
npm ci --silent
npm run build
Pop-Location
Write-Host "[✓] Dashboard built" -ForegroundColor Green

# --- Publish TrayApp (Native AOT) ---
Write-Host ""
Write-Host "--- Publishing TimeLens (Native AOT) ---" -ForegroundColor Cyan
dotnet publish "$root\src\TimeLens.TrayApp" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true

$publishDir = "$root\src\TimeLens.TrayApp\bin\Release\net9.0\win-x64\publish"

# --- Install to %LOCALAPPDATA%\TimeLens ---
Write-Host ""
Write-Host "--- Installing to $appDir ---" -ForegroundColor Cyan
if (-not (Test-Path $appDir)) { New-Item -ItemType Directory -Path $appDir -Force | Out-Null }

Copy-Item "$publishDir\TimeLens.TrayApp.exe" "$appDir\" -Force
if (Test-Path "$appDir\dashboard") { Remove-Item -Recurse -Force "$appDir\dashboard" }
Copy-Item -Recurse "$publishDir\dashboard" "$appDir\dashboard"
if (Test-Path "$root\LICENSE") { Copy-Item "$root\LICENSE" "$appDir\" }
Write-Host "[✓] Files copied to $appDir" -ForegroundColor Green

# --- Create Start Menu shortcut ---
Write-Host ""
Write-Host "--- Creating shortcuts ---" -ForegroundColor Cyan
$startMenu = [Environment]::GetFolderPath("Programs")
$shortcutPath = "$startMenu\TimeLens.lnk"

$wshell = New-Object -ComObject WScript.Shell
$shortcut = $wshell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $appDir
$shortcut.Description = "TimeLens PC Activity Tracker"
$shortcut.Save()
Write-Host "[✓] Start Menu shortcut created" -ForegroundColor Green

$desktop = [Environment]::GetFolderPath("Desktop")
$desktopShortcut = "$desktop\TimeLens.lnk"
if (-not (Test-Path $desktopShortcut)) {
    $shortcut = $wshell.CreateShortcut($desktopShortcut)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $appDir
    $shortcut.Description = "TimeLens PC Activity Tracker"
    $shortcut.Save()
    Write-Host "[✓] Desktop shortcut created" -ForegroundColor Green
}

# --- Launch ---
Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Installed at: $appDir"
Write-Host "Start Menu:  TimeLens"
Write-Host ""
Write-Host "Launching TimeLens..." -ForegroundColor Cyan
Start-Process -FilePath $exePath -WorkingDirectory $appDir
Start-Sleep -Seconds 2

Write-Host ""
Write-Host "Open http://127.0.0.1:47821/ in your browser." -ForegroundColor Cyan
Write-Host "(Click the tray icon first if the dashboard doesn't load.)" -ForegroundColor Gray
