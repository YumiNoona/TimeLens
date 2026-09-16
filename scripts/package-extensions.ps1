param(
    [string]$OutputDirectory = (Split-Path -Parent $PSScriptRoot)
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
    foreach ($browser in @('chrome', 'firefox')) {
        $stage = Join-Path $stagingRoot $browser
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceRoot "$browser\*") -Destination $stage -Recurse -Force
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
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
