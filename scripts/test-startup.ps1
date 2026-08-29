param(
    [string]$ExePath = "$PSScriptRoot\..\TimeLens.exe",
    [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $ExePath).Path
if ([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains 47821) {
    throw 'Close the running TimeLens instance before running the isolated startup test.'
}
$testRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts\startup-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$dataDir = Join-Path $testRoot 'fresh-user'

if (-not ('TimeLensStartupProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TimeLensStartupProbe {
    [StructLayout(LayoutKind.Sequential)]
    public struct IconId { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string name, string title);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref IconId id, out Rect rect);
    public static bool HasTrayIcon(IntPtr window) {
        var id = new IconId { Size=(uint)Marshal.SizeOf<IconId>(), Window=window, Id=100 };
        Rect rect;
        return Shell_NotifyIconGetRect(ref id, out rect) == 0;
    }
}
'@
}

$process = $null
$window = [IntPtr]::Zero
try {
    # Run the shipped EXE with no sibling DLLs required and an unrelated working directory.
    # --startup is the exact switch stored in HKCU\...\Run on Windows 10 and 11.
    $process = Start-Process -FilePath $exe -ArgumentList ('--startup --smoke-test "{0}"' -f $dataDir) `
        -WorkingDirectory "$env:WINDIR\System32" -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $testRoot 'stdout.log') `
        -RedirectStandardError (Join-Path $testRoot 'stderr.log')
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "Packaged app exited during startup (code $($process.ExitCode))." }
        $window = [TimeLensStartupProbe]::FindWindowW('TimeLensHiddenWindow', 'TimeLens')
        [uint32]$owner = 0
        if ($window -ne [IntPtr]::Zero) {
            [void][TimeLensStartupProbe]::GetWindowThreadProcessId($window, [ref]$owner)
        }
        if ($owner -eq $process.Id -and [TimeLensStartupProbe]::HasTrayIcon($window)) {
            try {
                $settings = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -TimeoutSec 2
                if ($null -ne $settings.trackInput) { $ready = $true; break }
            } catch { }
        }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'The packaged app did not register its tray icon and start the settings API.' }
    $dashboard = Invoke-WebRequest 'http://127.0.0.1:47821/' -UseBasicParsing -TimeoutSec 5
    if ($dashboard.Content -notmatch '<script[^>]+src="([^"]+)"') { throw 'Embedded dashboard entry or JavaScript is missing.' }
    $asset = $Matches[1]
    $null = Invoke-WebRequest "http://127.0.0.1:47821$asset" -UseBasicParsing -TimeoutSec 5
    $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/summary' -TimeoutSec 10
    foreach ($file in @('activity.db', 'runtime\e_sqlite3.dll', 'runtime\categories.csv', 'runtime\TimeLens.ico')) {
        if (-not (Test-Path -LiteralPath (Join-Path $dataDir $file))) { throw "Missing fresh-install file: $file" }
    }
    if (Test-Path -LiteralPath (Join-Path $dataDir 'crash.log')) { throw 'The packaged app wrote a crash log.' }
    [void][TimeLensStartupProbe]::PostMessageW($window, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    if (-not $process.WaitForExit(5000)) { throw 'The tray app did not exit cleanly.' }
    if ($process.ExitCode -ne 0) { throw "The tray app exited with code $($process.ExitCode)." }
    Write-Host 'PASS: packaged EXE, fresh database, embedded runtime, native tray, dashboard assets, settings and summary APIs.'
    Write-Host "Isolated test files: $testRoot"
} catch {
    foreach ($log in @((Join-Path $dataDir 'crash.log'), (Join-Path $testRoot 'stderr.log'), (Join-Path $testRoot 'stdout.log'))) {
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 30 | Write-Host }
    }
    throw
} finally {
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
}
