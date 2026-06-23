param(
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "`n=== Building Svelte dashboard ===" -ForegroundColor Cyan
Set-Location "$root\src\TimeLens.Dashboard"
npm install
npm run build

Write-Host "`n=== Publishing .NET app (Native AOT) ===" -ForegroundColor Cyan
Set-Location "$root"
dotnet publish src\TimeLens.TrayApp -c $Config -r win-x64 --self-contained true

$publishDir = "src\TimeLens.TrayApp\bin\$Config\net9.0\win-x64\publish"

Write-Host "`n=== Copying dashboard to publish output ===" -ForegroundColor Cyan
if (Test-Path "$publishDir\dashboard") {
    Remove-Item -Recurse -Force "$publishDir\dashboard"
}
Copy-Item -Recurse "src\TimeLens.Dashboard\dist" "$publishDir\dashboard"

Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "Output: $publishDir"
Write-Host "Exe: TimeLens.TrayApp.exe ($((Get-Item "$publishDir\TimeLens.TrayApp.exe").Length / 1MB) MB)"
Write-Host "Dashboard: $publishDir\dashboard\index.html"
