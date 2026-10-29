param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) '.artifacts/packages')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src\browser-extensions'
$sharedRoot = Join-Path $sourceRoot 'shared'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path ([IO.Path]::GetTempPath()) "TimeLens-extensions-$([Guid]::NewGuid().ToString('N'))"

New-Item -ItemType Directory -Path $output -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
try {
    foreach ($browser in @('chrome', 'firefox', 'edge', 'brave')) {
        $stage = Join-Path $stagingRoot $browser
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        $family = if ($browser -eq 'firefox') { 'firefox' } else { 'chrome' }
        Copy-Item -Path (Join-Path $sourceRoot "$family\*") -Destination $stage -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $sharedRoot 'background.js') -Destination (Join-Path $stage 'background.js') -Force
        Copy-Item -LiteralPath (Join-Path $sharedRoot 'content.js') -Destination (Join-Path $stage 'content.js') -Force

        $archive = Join-Path $output "TimeLens-$($browser.Substring(0,1).ToUpper() + $browser.Substring(1))-Extension.zip"
        Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal
        Write-Host "Packaged $archive"
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        $resolvedStage = [IO.Path]::GetFullPath($stagingRoot)
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $resolvedStage.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path' }
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
