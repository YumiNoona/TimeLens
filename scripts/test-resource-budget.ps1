param(
    [string]$ExePath = "$PSScriptRoot\..\.artifacts\release\TimeLens.exe",
    [ValidateRange(10, 300)][int]$SampleSeconds = 30
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$probeRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ".artifacts\resource-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
$dataDir = Join-Path $probeRoot 'fresh-user'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$origin = "http://127.0.0.1:$port"
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$probeProcess = $null
try {
    $probeProcess = Start-Process -FilePath $exe -ArgumentList ('--smoke-test "{0}" --api-port {1}' -f $dataDir, $port) `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $probeRoot 'stdout.log') -RedirectStandardError (Join-Path $probeRoot 'stderr.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($probeProcess.HasExited) { throw 'Probe executable exited before becoming ready.' }
        try { $null = Invoke-WebRequest "$origin/" -WebSession $session -TimeoutSec 1; $ready = $true; break } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw 'Probe API did not become ready.' }
    $null = Invoke-RestMethod "$origin/api/summary" -WebSession $session
    # Allow the first native audio checkpoint and connection-pool setup to settle.
    Start-Sleep -Seconds 15
    $probeProcess.Refresh()
    $startCpu = $probeProcess.TotalProcessorTime.TotalSeconds
    $startHandles = $probeProcess.HandleCount
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $peakWorking = 0L; $peakPrivate = 0L
    for ($sample = 0; $sample -lt $SampleSeconds; $sample++) {
        Start-Sleep -Seconds 1
        $probeProcess.Refresh()
        if ($probeProcess.HasExited) { throw 'Probe executable exited during measurement.' }
        $peakWorking = [Math]::Max($peakWorking, $probeProcess.WorkingSet64)
        $peakPrivate = [Math]::Max($peakPrivate, $probeProcess.PrivateMemorySize64)
    }
    $watch.Stop()
    $cpuPercent = ($probeProcess.TotalProcessorTime.TotalSeconds - $startCpu) / $watch.Elapsed.TotalSeconds * 100
    [pscustomobject]@{
        Seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
        CpuPercentOfOneCore = [Math]::Round($cpuPercent, 3)
        PeakWorkingSetMiB = [Math]::Round($peakWorking / 1MB, 2)
        PeakPrivateMiB = [Math]::Round($peakPrivate / 1MB, 2)
        HandleDelta = $probeProcess.HandleCount - $startHandles
        ExecutableMiB = [Math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 2)
        DataDirectory = $dataDir
    } | Format-List
    $null = Invoke-RestMethod "$origin/api/app/exit" -Method Post -WebSession $session
    if (-not $probeProcess.WaitForExit(5000)) { throw 'Probe did not shut down cleanly.' }
} finally {
    if ($probeProcess -and -not $probeProcess.HasExited) { $probeProcess.Kill(); $probeProcess.WaitForExit() }
}
