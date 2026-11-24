[CmdletBinding()]
param([string]$OutputRoot = 'artifacts/release')

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputRoot))
$version = (Get-Content (Join-Path $outputPath 'VERSION.txt') -Raw).Trim()
$testRunner = Join-Path $repositoryRoot 'tests/NekoPcbEmulator.ProtocolTests/bin/Release/net10.0/NekoPcbEmulator.ProtocolTests.dll'

function Get-AvailablePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port }
    finally { $listener.Stop() }
}

function Wait-ForBoard([int]$Port, [Diagnostics.Process]$Process) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 30) {
        if ($Process.HasExited) { throw 'The published application exited before opening its ports.' }
        $probe = [Net.Sockets.TcpClient]::new()
        try {
            if ($probe.ConnectAsync('127.0.0.1', $Port).Wait(200) -and $probe.Connected) { return }
        }
        catch { }
        finally { $probe.Dispose() }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for board port $Port."
}

foreach ($flavor in @('self-contained', 'framework-dependent')) {
    $archive = Join-Path $outputPath "NekoPcbEmulator-$version-win-x64-$flavor.zip"
    $testRoot = Join-Path $outputPath ('verification/' + $flavor + '-' + [Guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $archive -DestinationPath $testRoot
    $portA = Get-AvailablePort
    do { $portB = Get-AvailablePort } while ($portB -eq $portA)
    $application = Join-Path $testRoot 'NekoPcbEmulator.App.exe'
    $applicationProcess = Start-Process -FilePath $application -ArgumentList @('--power', 'all', '--port-a', $portA, '--port-b', $portB) -WindowStyle Hidden -PassThru
    try {
        Wait-ForBoard $portA $applicationProcess
        Wait-ForBoard $portB $applicationProcess
        $report = Join-Path $repositoryRoot "artifacts/protocol-tests/published-$flavor.json"
        & dotnet $testRunner --external-tcp --port-a $portA --port-b $portB --report $report
        if ($LASTEXITCODE -ne 0) { throw "Protocol verification failed for $flavor." }
        $client = Join-Path $testRoot 'NekoPcbEmulator.TestClient.exe'
        $usage = (& $client --help 2>&1) -join "`n"
        if ($LASTEXITCODE -ne 1 -or $usage -notmatch 'usage: NekoPcbEmulator.TestClient') {
            throw "The reference client failed its startup check ($flavor)."
        }
    }
    finally {
        if (-not $applicationProcess.HasExited) {
            $applicationProcess.Kill($true)
            $applicationProcess.WaitForExit()
        }
        $applicationProcess.Dispose()
    }
    Write-Output "Verified published package: $flavor"
}
