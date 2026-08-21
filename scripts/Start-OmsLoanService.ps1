<#
.SYNOPSIS
    Starts the OmsLoan worker service and shows its startup banner.

.DESCRIPTION
    Starts the service, waits for it to report Running, then prints the recent Application
    log entries from it.

    The banner is the point. It names the environment that was selected and which source
    supplied the connection string and each API key, which is what tells you whether the
    service came up against the configuration you intended. A service that starts cleanly
    against the wrong database looks identical to one that started correctly until you read
    that line.

.EXAMPLE
    .\Start-OmsLoanService.ps1
#>
[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 60,
    [int]$LogEntries = 15
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serviceName = 'OmsLoanWorker'
$eventLogSource = 'OmsLoanWorker'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    throw "Service '$serviceName' is not installed. Run .\Install-OmsLoanService.ps1 first."
}

$startedAt = Get-Date

if ($service.Status -eq 'Running') {
    Write-Host "Service is already running."
}
else {
    Write-Host "Starting '$serviceName'."
    Start-Service -Name $serviceName
    try {
        $service.WaitForStatus('Running', [TimeSpan]::FromSeconds($TimeoutSeconds))
        Write-Host "Service is running." -ForegroundColor Green
    }
    catch [System.ServiceProcess.TimeoutException] {
        Write-Warning "Service did not reach Running within $TimeoutSeconds seconds. Recent log entries follow."
    }
}

Write-Host ''
Write-Host '--- Application log ---------------------------------------------------------'

# -ErrorAction SilentlyContinue: a service that has never started successfully has written
# nothing, and "no entries found" is a worse message here than an empty section.
$entries = Get-WinEvent -FilterHashtable @{
    LogName      = 'Application'
    ProviderName = $eventLogSource
    StartTime    = $startedAt.AddMinutes(-1)
} -MaxEvents $LogEntries -ErrorAction SilentlyContinue

if ($null -eq $entries) {
    Write-Host '(nothing logged yet — if the service failed to start, check the System log for SCM errors)'
}
else {
    $entries | Sort-Object TimeCreated | ForEach-Object {
        Write-Host ("[{0:HH:mm:ss}] {1}" -f $_.TimeCreated, $_.LevelDisplayName)
        Write-Host $_.Message
        Write-Host ''
    }
}
