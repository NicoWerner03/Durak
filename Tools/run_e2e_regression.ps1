param(
    [string]$ExePath = "C:\_dev\DurakGameCodex\Builds\ScenarioRunner\DurakGameCodex.exe",
    [int]$StabilityCycles = 10,
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = "Stop"

function Stop-RunningDurakProcesses {
    Get-Process | Where-Object {
        $_.ProcessName -like 'DurakGameCodex*' -or
        ($_.Path -and $_.Path -like '*DurakGameCodex.exe')
    } | ForEach-Object {
        try { Stop-Process -Id $_.Id -Force } catch {}
    }
}

function Wait-ProcessWithTimeout {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds,
        [string]$Name
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not $Process.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }

    if (-not $Process.HasExited) {
        try { $Process.Kill() } catch {}
        throw "$Name timed out after $TimeoutSeconds seconds."
    }

    return $Process.ExitCode
}

function Start-DurakProcess {
    param(
        [string]$ExePath,
        [string[]]$CliArgs
    )

    return Start-Process -FilePath $ExePath -ArgumentList $CliArgs -PassThru -WindowStyle Hidden
}

function Assert-LogContains {
    param(
        [string]$Path,
        [string]$Needle
    )

    if (-not (Test-Path $Path)) {
        throw "Missing log: $Path"
    }

    $content = Get-Content $Path -Raw
    if ($content -notlike "*$Needle*") {
        throw "Log '$Path' does not contain expected marker '$Needle'."
    }
}

if (-not (Test-Path $ExePath)) {
    throw "Scenario runner executable not found: $ExePath"
}

Stop-RunningDurakProcesses

$runRoot = "C:\_dev\DurakGameCodex\TestResults\ScenarioRuns"
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$commonArgs = @(
    "-batchmode", "-nographics",
    "-durak-force-direct", "-durak-direct-port", "7777",
    "-durak-auto"
)

Write-Host "Running second-lobby regression (2 cycles)..."
$secondLobbyDir = Join-Path $runRoot "second-lobby"
New-Item -ItemType Directory -Path $secondLobbyDir -Force | Out-Null
$hostLog = Join-Path $secondLobbyDir "host.log"
$clientLog = Join-Path $secondLobbyDir "client.log"
$hostPlayerLog = Join-Path $secondLobbyDir "host-player.log"
$clientPlayerLog = Join-Path $secondLobbyDir "client-player.log"
Remove-Item -ErrorAction SilentlyContinue $hostLog, $clientLog, $hostPlayerLog, $clientPlayerLog

$hostArgs = $commonArgs + @(
    "-durak-role", "host",
    "-durak-cycles", "2",
    "-durak-log", $hostLog,
    "-logFile", $hostPlayerLog
)

$clientArgs = $commonArgs + @(
    "-durak-role", "client",
    "-durak-join", "DIRECT:127.0.0.1:7777",
    "-durak-cycles", "2",
    "-durak-log", $clientLog,
    "-logFile", $clientPlayerLog
)

$hostProcess = Start-DurakProcess -ExePath $ExePath -CliArgs $hostArgs
Start-Sleep -Seconds 2
$clientProcess = Start-DurakProcess -ExePath $ExePath -CliArgs $clientArgs

$hostExit = Wait-ProcessWithTimeout -Process $hostProcess -TimeoutSeconds $TimeoutSeconds -Name "Second-lobby host"
$clientExit = Wait-ProcessWithTimeout -Process $clientProcess -TimeoutSeconds $TimeoutSeconds -Name "Second-lobby client"

if ($hostExit -ne 0 -or $clientExit -ne 0) {
    throw "Second-lobby regression failed. HostExit=$hostExit ClientExit=$clientExit"
}

Assert-LogContains -Path $hostLog -Needle "Match completed. Count=2"
Assert-LogContains -Path $clientLog -Needle "Match completed. Count=2"
Assert-LogContains -Path $hostLog -Needle "Automation completed successfully."
Assert-LogContains -Path $clientLog -Needle "Automation completed successfully."

Stop-RunningDurakProcesses

Write-Host "Running stability regression ($StabilityCycles cycles)..."
$stabilityDir = Join-Path $runRoot "stability"
New-Item -ItemType Directory -Path $stabilityDir -Force | Out-Null
$stabilityHostLog = Join-Path $stabilityDir "host.log"
$stabilityClientLog = Join-Path $stabilityDir "client.log"
$stabilityHostPlayerLog = Join-Path $stabilityDir "host-player.log"
$stabilityClientPlayerLog = Join-Path $stabilityDir "client-player.log"
Remove-Item -ErrorAction SilentlyContinue $stabilityHostLog, $stabilityClientLog, $stabilityHostPlayerLog, $stabilityClientPlayerLog

$hostArgs = $commonArgs + @(
    "-durak-role", "host",
    "-durak-cycles", $StabilityCycles.ToString(),
    "-durak-log", $stabilityHostLog,
    "-logFile", $stabilityHostPlayerLog
)

$clientArgs = $commonArgs + @(
    "-durak-role", "client",
    "-durak-join", "DIRECT:127.0.0.1:7777",
    "-durak-cycles", $StabilityCycles.ToString(),
    "-durak-log", $stabilityClientLog,
    "-logFile", $stabilityClientPlayerLog
)

$hostProcess = Start-DurakProcess -ExePath $ExePath -CliArgs $hostArgs
Start-Sleep -Seconds 2
$clientProcess = Start-DurakProcess -ExePath $ExePath -CliArgs $clientArgs

$hostExit = Wait-ProcessWithTimeout -Process $hostProcess -TimeoutSeconds $TimeoutSeconds -Name "Stability host"
$clientExit = Wait-ProcessWithTimeout -Process $clientProcess -TimeoutSeconds $TimeoutSeconds -Name "Stability client"

if ($hostExit -ne 0 -or $clientExit -ne 0) {
    throw "Stability regression failed. HostExit=$hostExit ClientExit=$clientExit"
}

Assert-LogContains -Path $stabilityHostLog -Needle ("Match completed. Count=" + $StabilityCycles)
Assert-LogContains -Path $stabilityClientLog -Needle ("Match completed. Count=" + $StabilityCycles)
Assert-LogContains -Path $stabilityHostLog -Needle "Automation completed successfully."
Assert-LogContains -Path $stabilityClientLog -Needle "Automation completed successfully."

Stop-RunningDurakProcesses

Write-Host "Running rejoin regression..."
$rejoinDir = Join-Path $runRoot "rejoin"
New-Item -ItemType Directory -Path $rejoinDir -Force | Out-Null
$rejoinHostLog = Join-Path $rejoinDir "host.log"
$rejoinDropClientLog = Join-Path $rejoinDir "client_drop.log"
$rejoinClientLog = Join-Path $rejoinDir "client_rejoin.log"
$rejoinHostPlayerLog = Join-Path $rejoinDir "host-player.log"
$rejoinDropPlayerLog = Join-Path $rejoinDir "client_drop-player.log"
$rejoinClientPlayerLog = Join-Path $rejoinDir "client_rejoin-player.log"
Remove-Item -ErrorAction SilentlyContinue $rejoinHostLog, $rejoinDropClientLog, $rejoinClientLog, $rejoinHostPlayerLog, $rejoinDropPlayerLog, $rejoinClientPlayerLog

$hostArgs = $commonArgs + @(
    "-durak-role", "host",
    "-durak-cycles", "1",
    "-durak-log", $rejoinHostLog,
    "-logFile", $rejoinHostPlayerLog
)

$dropClientArgs = $commonArgs + @(
    "-durak-role", "client",
    "-durak-join", "DIRECT:127.0.0.1:7777",
    "-durak-cycles", "1",
    "-durak-drop-after-seconds", "0.5",
    "-durak-log", $rejoinDropClientLog,
    "-logFile", $rejoinDropPlayerLog
)

$rejoinClientArgs = $commonArgs + @(
    "-durak-role", "client",
    "-durak-join", "DIRECT:127.0.0.1:7777",
    "-durak-cycles", "1",
    "-durak-log", $rejoinClientLog,
    "-logFile", $rejoinClientPlayerLog
)

$hostProcess = Start-DurakProcess -ExePath $ExePath -CliArgs $hostArgs
Start-Sleep -Seconds 2
$dropClient = Start-DurakProcess -ExePath $ExePath -CliArgs $dropClientArgs
$dropExit = Wait-ProcessWithTimeout -Process $dropClient -TimeoutSeconds $TimeoutSeconds -Name "Drop client"

if ($dropExit -ne 42) {
    throw "Rejoin precondition failed. Drop client should exit with 42, got $dropExit"
}

Start-Sleep -Seconds 2
$rejoinClient = Start-DurakProcess -ExePath $ExePath -CliArgs $rejoinClientArgs
$rejoinExit = Wait-ProcessWithTimeout -Process $rejoinClient -TimeoutSeconds $TimeoutSeconds -Name "Rejoin client"
$hostExit = Wait-ProcessWithTimeout -Process $hostProcess -TimeoutSeconds $TimeoutSeconds -Name "Rejoin host"

if ($hostExit -ne 0 -or $rejoinExit -ne 0) {
    throw "Rejoin regression failed. HostExit=$hostExit RejoinExit=$rejoinExit"
}

Assert-LogContains -Path $rejoinHostLog -Needle "Match completed. Count=1"
Assert-LogContains -Path $rejoinClientLog -Needle "Entered match."
Assert-LogContains -Path $rejoinClientLog -Needle "Automation completed successfully."

Stop-RunningDurakProcesses

Write-Host "All automated E2E regressions passed."
Write-Host "Logs:"
Write-Host " - $secondLobbyDir"
Write-Host " - $stabilityDir"
Write-Host " - $rejoinDir"
