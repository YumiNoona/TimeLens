param(
    [string]$Config = "Release",
    [switch]$NoShortcut,
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = "src\TimeLens.TrayApp\bin\$Config\net9.0\win-x64\publish"
$exePath = "$publishDir\TimeLens.TrayApp.exe"

Write-Host "`n=== Building Svelte dashboard ===" -ForegroundColor Cyan
Set-Location "$root\src\TimeLens.Dashboard"
npm ci --silent
npm run build
Set-Location "$root"

Write-Host "`n=== Publishing .NET app (Native AOT) ===" -ForegroundColor Cyan
dotnet publish src\TimeLens.TrayApp -c $Config -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true

Write-Host "`n=== Copying dashboard to publish output ===" -ForegroundColor Cyan
if (Test-Path "$publishDir\dashboard") {
    Remove-Item -Recurse -Force "$publishDir\dashboard"
}
Copy-Item -Recurse "src\TimeLens.Dashboard\dist" "$publishDir\dashboard"

Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "Output: $publishDir"
Write-Host "Exe: $exePath ($((Get-Item $exePath).Length / 1MB) MB)"
Write-Host "Dashboard: $publishDir\dashboard\index.html"

if (-not $NoShortcut) {
    Write-Host "`n=== Creating Start Menu shortcut ===" -ForegroundColor Cyan
    $startMenu = [Environment]::GetFolderPath("Programs")
    $shortcutPath = "$startMenu\TimeLens.lnk"
    $wshell = New-Object -ComObject WScript.Shell
    $shortcut = $wshell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = (Resolve-Path $exePath).Path
    $shortcut.WorkingDirectory = (Resolve-Path $publishDir).Path
    $shortcut.Description = "TimeLens PC Activity Tracker"
    $shortcut.Save()
    Write-Host "[✓] Start Menu: TimeLens" -ForegroundColor Green
}

if ($Launch) {
    Write-Host "`n=== Launching TimeLens ===" -ForegroundColor Cyan
    Start-Process -FilePath (Resolve-Path $exePath).Path -WorkingDirectory (Resolve-Path $publishDir).Path
}
