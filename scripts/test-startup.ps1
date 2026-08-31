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
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfoW(uint action, uint param, out Rect value, uint flags);
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

    # Exercise the production custom-reminder API, image pipeline, and the native
    # toast dispatch that marshals API calls onto the tray message-loop thread.
    $notificationBody = @{ blockTitle = 'Deep Work'; blockMessage = 'Close {target} — {mode} mode is active.'; blockNotifyIntervalSeconds = 5; blockNotifyPosition = 'left' } | ConvertTo-Json -Compress
    $notificationBytes = [Text.Encoding]::UTF8.GetBytes($notificationBody)
    $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -Method Post -ContentType 'application/json; charset=utf-8' -Body $notificationBytes -TimeoutSec 5
    $savedSettings = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -TimeoutSec 5
    if ($savedSettings.blockTitle -ne 'Deep Work' -or $savedSettings.blockMessage -notmatch '\{target\}' -or
        $savedSettings.blockNotifyIntervalSeconds -ne 5 -or $savedSettings.blockNotifyPosition -ne 'left') {
        throw 'Custom block notification settings did not persist.'
    }

    # Websites intentionally expose only Notify and Strict. Legacy desktop-only
    # actions safely migrate to Strict and the extension receives presentation data.
    $blocklistJson = '[{"i":"example.com","m":"u"}]'
    $tabId = 6000
    foreach ($mode in @('notify', 'hide', 'kill', 'strict')) {
        $modeSettings = @{ focusMode = $true; blockAction = $mode; focusBlocklist = $blocklistJson } | ConvertTo-Json -Compress
        $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -Method Post -ContentType 'application/json' -Body $modeSettings -TimeoutSec 5
        $browserEvent = @{ domain = 'www.example.com'; url = 'https://www.example.com/path'; title = 'Block contract'; browser = 'test'; tabId = $tabId } | ConvertTo-Json -Compress
        $browserResult = Invoke-RestMethod 'http://127.0.0.1:47821/api/browser-event' -Method Post -ContentType 'application/json' -Body $browserEvent -TimeoutSec 5
        $expectedAction = if ($mode -eq 'notify') { 'notify' } else { 'strict' }
        $expectedBlocked = $expectedAction -eq 'strict'
        if ($browserResult.action -ne $expectedAction -or [bool]$browserResult.blocked -ne $expectedBlocked -or
            $browserResult.presentation.surface -ne 'browser' -or $browserResult.presentation.target -ne 'example.com') {
            throw "Browser block response was incorrect for $mode mode."
        }
        $tabId++
    }
    foreach ($mode in @('notify', 'strict')) {
        $perTargetBlocklist = "[{`"i`":`"example.com`",`"m`":`"u`",`"a`":`"$mode`"}]"
        $modeSettings = @{ focusMode = $true; blockAction = 'hide'; focusBlocklist = $perTargetBlocklist } | ConvertTo-Json -Compress
        $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -Method Post -ContentType 'application/json' -Body $modeSettings -TimeoutSec 5
        $state = Invoke-RestMethod 'http://127.0.0.1:47821/api/browser-block-state?domain=www.example.com' -TimeoutSec 5
        if ($state.action -ne $mode -or [bool]$state.blocked -ne ($mode -eq 'strict')) {
            throw "Per-target website response was incorrect for $mode mode."
        }
    }
    $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -Method Post -ContentType 'application/json' -Body (@{ focusBlocklist = '[]' } | ConvertTo-Json -Compress) -TimeoutSec 5
    $unblockedState = Invoke-RestMethod 'http://127.0.0.1:47821/api/browser-block-state?domain=www.example.com' -TimeoutSec 5
    if ($unblockedState.action -ne 'none' -or [bool]$unblockedState.blocked) { throw 'Removing a website did not unblock it immediately.' }

    # File Explorer is discoverable and can use non-destructive modes, but the API
    # must reject Kill/Strict so focus controls cannot tear down the Windows shell.
    $safeExplorer = @{ focusBlocklist = '[{"i":"explorer.exe","m":"u","a":"notify"}]' } | ConvertTo-Json -Compress
    $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -Method Post -ContentType 'application/json' -Body $safeExplorer -TimeoutSec 5
    $unsafeExplorer = @{ focusBlocklist = '[{"i":"explorer.exe","m":"u","a":"kill"}]' } | ConvertTo-Json -Compress
    $unsafeResponse = Invoke-WebRequest 'http://127.0.0.1:47821/api/settings' -Method Post -ContentType 'application/json' -Body $unsafeExplorer -SkipHttpErrorCheck -TimeoutSec 5
    if ($unsafeResponse.StatusCode -ne 400) { throw 'Destructive File Explorer blocking was not rejected.' }

    $resetBlockSettings = @{ focusMode = $false; blockAction = 'hide'; focusBlocklist = '[]' } | ConvertTo-Json -Compress
    $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -Method Post -ContentType 'application/json' -Body $resetBlockSettings -TimeoutSec 5

    # Valid one-pixel PNG; the API decodes and normalizes it to its local toast asset.
    $png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII='
    $imageBody = @{ dataUrl = "data:image/png;base64,$png" } | ConvertTo-Json -Compress
    $imageResult = Invoke-RestMethod 'http://127.0.0.1:47821/api/block/media' -Method Post -ContentType 'application/json' -Body $imageBody -TimeoutSec 5
    if (-not $imageResult.version) { throw 'Custom block image upload did not return a version.' }
    $imageResponse = Invoke-WebRequest 'http://127.0.0.1:47821/api/block/media' -UseBasicParsing -TimeoutSec 5
    if ($imageResponse.Headers.'Content-Type' -notmatch 'image/png' -or $imageResponse.RawContentLength -le 0) {
        throw 'Custom block image could not be read back.'
    }
    $mediaContractSettings = @{ focusMode = $true; focusBlocklist = '[{"i":"example.com","m":"u","a":"notify"}]' } | ConvertTo-Json -Compress
    $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -Method Post -ContentType 'application/json' -Body $mediaContractSettings -TimeoutSec 5
    $mediaContract = Invoke-RestMethod 'http://127.0.0.1:47821/api/browser-block-state?domain=example.com' -TimeoutSec 5
    if ($mediaContract.presentation.mediaType -ne 'image/png' -or
        $mediaContract.presentation.repeatIntervalSeconds -ne 5 -or
        $mediaContract.presentation.position -ne 'left' -or
        -not $mediaContract.presentation.mediaUrl -or -not $mediaContract.presentation.imageUrl) {
        throw 'Browser reminder media, interval, position, or compatibility URL was missing.'
    }
    $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/settings' -Method Post -ContentType 'application/json' -Body (@{ focusMode = $false; focusBlocklist = '[]' } | ConvertTo-Json -Compress) -TimeoutSec 5

    $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/block/preview' -Method Post -TimeoutSec 5
    $toastDeadline = [DateTime]::UtcNow.AddSeconds(3)
    $toastWindow = [IntPtr]::Zero
    while ([DateTime]::UtcNow -lt $toastDeadline) {
        $toastWindow = [TimeLensStartupProbe]::FindWindowW('TLToast', $null)
        if ($toastWindow -ne [IntPtr]::Zero -and [TimeLensStartupProbe]::IsWindowVisible($toastWindow)) { break }
        Start-Sleep -Milliseconds 100
    }
    [uint32]$toastOwner = 0
    if ($toastWindow -ne [IntPtr]::Zero) {
        [void][TimeLensStartupProbe]::GetWindowThreadProcessId($toastWindow, [ref]$toastOwner)
    }
    if ($toastWindow -eq [IntPtr]::Zero -or $toastOwner -ne $process.Id) {
        throw 'Custom block notification preview did not create a native toast owned by TimeLens.'
    }
    if (-not [TimeLensStartupProbe]::IsWindowVisible($toastWindow)) {
        throw 'Custom block notification preview created a hidden toast.'
    }
    $toastRect = New-Object TimeLensStartupProbe+Rect
    $workRect = New-Object TimeLensStartupProbe+Rect
    if (-not [TimeLensStartupProbe]::GetWindowRect($toastWindow, [ref]$toastRect) -or
        -not [TimeLensStartupProbe]::SystemParametersInfoW(0x0030, 0, [ref]$workRect, 0)) {
        throw 'Could not inspect the native toast placement.'
    }
    if ($toastRect.Left -gt ($workRect.Left + (($workRect.Right - $workRect.Left) / 2))) {
        throw 'Custom block notification is not positioned on the left side of the work area.'
    }
    if (($workRect.Bottom - $toastRect.Bottom) -lt 12 -or ($workRect.Bottom - $toastRect.Bottom) -gt 48) {
        throw 'Custom block notification bottom padding is outside the expected range.'
    }
    $bodyClick = [IntPtr]((70 -shl 16) -bor 220)
    [void][TimeLensStartupProbe]::SendMessageW($toastWindow, 0x0202, [IntPtr]::Zero, $bodyClick)
    Start-Sleep -Milliseconds 100
    if (-not [TimeLensStartupProbe]::IsWindow($toastWindow)) {
        throw 'Clicking the body dismissed a notification that should persist until its close button is used.'
    }
    $closeX = ($toastRect.Right - $toastRect.Left) - 24
    $closeClick = [IntPtr]((25 -shl 16) -bor $closeX)
    [void][TimeLensStartupProbe]::SendMessageW($toastWindow, 0x0202, [IntPtr]::Zero, $closeClick)
    Start-Sleep -Milliseconds 100
    if ([TimeLensStartupProbe]::IsWindow($toastWindow)) { throw 'The native toast close button did not dismiss the notification.' }

    $null = Invoke-RestMethod 'http://127.0.0.1:47821/api/block/media' -Method Delete -TimeoutSec 5
    if (Get-ChildItem -LiteralPath $dataDir -Filter 'block-notification*' -ErrorAction SilentlyContinue) {
        throw 'Custom block media was not removed.'
    }
    foreach ($file in @('activity.db', 'runtime\e_sqlite3.dll', 'runtime\categories.csv', 'runtime\TimeLens.ico')) {
        if (-not (Test-Path -LiteralPath (Join-Path $dataDir $file))) { throw "Missing fresh-install file: $file" }
    }
    if (Test-Path -LiteralPath (Join-Path $dataDir 'crash.log')) { throw 'The packaged app wrote a crash log.' }
    [void][TimeLensStartupProbe]::PostMessageW($window, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    if (-not $process.WaitForExit(5000)) { throw 'The tray app did not exit cleanly.' }
    if ($process.ExitCode -ne 0) { throw "The tray app exited with code $($process.ExitCode)." }
    Write-Host 'PASS: packaged EXE, fresh database, embedded runtime, native tray/persistent toast, dashboard assets, browser block contract, settings, custom block media, and summary APIs.'
    Write-Host "Isolated test files: $testRoot"
} catch {
    foreach ($log in @((Join-Path $dataDir 'crash.log'), (Join-Path $testRoot 'stderr.log'), (Join-Path $testRoot 'stdout.log'))) {
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 30 | Write-Host }
    }
    throw
} finally {
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
}
