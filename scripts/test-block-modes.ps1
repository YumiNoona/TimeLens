param(
    [string]$ExePath = "$PSScriptRoot\..\TimeLens.exe",
    [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$api = 'http://127.0.0.1:47821'
$target = 'timelensblockprobe.exe'

if ([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains 47821) {
    throw 'Close the running TimeLens instance before running the isolated block-mode test.'
}

dotnet build "$root\tests\TimeLens.BlockProbe\TimeLens.BlockProbe.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'The block probe failed to build.' }

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$probePath = (Resolve-Path "$root\tests\TimeLens.BlockProbe\bin\Release\net9.0-windows\TimeLensBlockProbe.exe").Path
$testRoot = Join-Path $root "artifacts\block-modes-$([Guid]::NewGuid().ToString('N'))"
$dataDir = Join-Path $testRoot 'fresh-user'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

if (-not ('TimeLensBlockModeProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TimeLensBlockModeProbe {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string className, string title);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
'@
}

function Save-BlockSettings([string]$mode) {
    $payload = @{
        focusMode = $true
        focusBlocklist = '[{"i":"timelensblockprobe.exe","m":"u"}]'
        blockAction = $mode
    } | ConvertTo-Json -Compress
    $null = Invoke-RestMethod "$api/api/settings" -Method Post -ContentType 'application/json' -Body $payload -TimeoutSec 5
}

function Start-Probe {
    $process = Start-Process -FilePath $probePath -WorkingDirectory (Split-Path -Parent $probePath) -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    $window = [IntPtr]::Zero
    do {
        Start-Sleep -Milliseconds 50
        $process.Refresh()
        if ($process.HasExited) { throw "Block probe exited before enforcement (code $($process.ExitCode))." }
        $window = [TimeLensBlockModeProbe]::FindWindowW('TimeLensBlockProbeWindow', 'TimeLens Block Probe')
        [uint32]$owner = 0
        if ($window -ne [IntPtr]::Zero) { [void][TimeLensBlockModeProbe]::GetWindowThreadProcessId($window, [ref]$owner) }
    } until (($window -ne [IntPtr]::Zero -and $owner -eq $process.Id) -or [DateTime]::UtcNow -ge $deadline)
    if ($window -eq [IntPtr]::Zero -or $owner -ne $process.Id) { throw 'Block probe window did not initialize.' }
    return @{ Process = $process; Window = $window }
}

function Invoke-Enforce {
    $payload = @{ exe = $target } | ConvertTo-Json -Compress
    $null = Invoke-RestMethod "$api/api/block/enforce" -Method Post -ContentType 'application/json' -Body $payload -TimeoutSec 5
}

function Close-Probe($probe) {
    if (-not $probe.Process.HasExited) {
        [void][TimeLensBlockModeProbe]::PostMessageW($probe.Window, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
        if (-not $probe.Process.WaitForExit(2000)) { $probe.Process.Kill(); $probe.Process.WaitForExit() }
    }
    $probe.Process.Dispose()
}

$timeLensProcess = $null
$timeLensWindow = [IntPtr]::Zero
$activeProbe = $null
try {
    $timeLensProcess = Start-Process -FilePath $exe -ArgumentList ('--startup --smoke-test "{0}"' -f $dataDir) `
        -WorkingDirectory "$env:WINDIR\System32" -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $testRoot 'stdout.log') `
        -RedirectStandardError (Join-Path $testRoot 'stderr.log')
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $ready = $false
    do {
        Start-Sleep -Milliseconds 100
        $timeLensProcess.Refresh()
        if ($timeLensProcess.HasExited) { throw "Packaged app exited during startup (code $($timeLensProcess.ExitCode))." }
        $timeLensWindow = [TimeLensBlockModeProbe]::FindWindowW('TimeLensHiddenWindow', 'TimeLens')
        [uint32]$owner = 0
        if ($timeLensWindow -ne [IntPtr]::Zero) { [void][TimeLensBlockModeProbe]::GetWindowThreadProcessId($timeLensWindow, [ref]$owner) }
        if ($owner -eq $timeLensProcess.Id) {
            try { $null = Invoke-RestMethod "$api/api/settings" -TimeoutSec 1; $ready = $true } catch { }
        }
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)
    if (-not $ready) { throw 'The packaged app did not start its tray window and settings API.' }

    Save-BlockSettings 'notify'
    $activeProbe = Start-Probe
    Invoke-Enforce
    Start-Sleep -Milliseconds 250
    $activeProbe.Process.Refresh()
    if ($activeProbe.Process.HasExited -or [TimeLensBlockModeProbe]::IsIconic($activeProbe.Window)) {
        throw 'Notify changed the target process or window.'
    }
    Close-Probe $activeProbe
    $activeProbe = $null
    Write-Host 'PASS: Notify displays the reminder without enforcing.'

    Save-BlockSettings 'hide'
    $activeProbe = Start-Probe
    Invoke-Enforce
    $deadline = [DateTime]::UtcNow.AddSeconds(2)
    while (-not [TimeLensBlockModeProbe]::IsIconic($activeProbe.Window) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
    $activeProbe.Process.Refresh()
    if ($activeProbe.Process.HasExited -or -not [TimeLensBlockModeProbe]::IsIconic($activeProbe.Window)) {
        throw 'Hide did not minimize the target while keeping it running.'
    }
    Close-Probe $activeProbe
    $activeProbe = $null
    Write-Host 'PASS: Hide minimizes the target and leaves it running.'

    Save-BlockSettings 'kill'
    $activeProbe = Start-Probe
    Invoke-Enforce
    if (-not $activeProbe.Process.WaitForExit(3000)) { throw 'Kill did not terminate the target.' }
    $activeProbe.Process.Dispose()
    $activeProbe = $null
    Write-Host 'PASS: Kill terminates the target process.'

    Save-BlockSettings 'strict'
    $activeProbe = Start-Probe
    Invoke-Enforce
    if (-not $activeProbe.Process.WaitForExit(3000)) { throw 'Strict did not terminate the target.' }
    $activeProbe.Process.Dispose()
    $activeProbe = $null
    Write-Host 'PASS: Strict performs immediate minimize/terminate enforcement.'

    $activeProbe = Start-Probe
    if (-not $activeProbe.Process.WaitForExit(8000)) { throw 'Strict five-second re-check did not terminate a relaunched target.' }
    $activeProbe.Process.Dispose()
    $activeProbe = $null
    Write-Host 'PASS: Strict re-check terminates a relaunched target within five seconds.'

    if (Test-Path -LiteralPath (Join-Path $dataDir 'crash.log')) { throw 'The packaged app wrote a crash log.' }
    Write-Host "Isolated test files: $testRoot"
}
catch {
    foreach ($log in @((Join-Path $dataDir 'crash.log'), (Join-Path $testRoot 'stderr.log'), (Join-Path $testRoot 'stdout.log'))) {
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 30 | Write-Host }
    }
    throw
}
finally {
    if ($activeProbe) { Close-Probe $activeProbe }
    if ($timeLensProcess -and -not $timeLensProcess.HasExited) {
        if ($timeLensWindow -ne [IntPtr]::Zero) {
            [void][TimeLensBlockModeProbe]::PostMessageW($timeLensWindow, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
        }
        if (-not $timeLensProcess.WaitForExit(5000)) { $timeLensProcess.Kill(); $timeLensProcess.WaitForExit() }
    }
    if ($timeLensProcess) { $timeLensProcess.Dispose() }
}
