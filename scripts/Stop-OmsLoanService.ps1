<#
.SYNOPSIS
    Stops the OmsLoan worker service.

.DESCRIPTION
    Stops the service and waits for it to report Stopped.

    The wait matters: the host is configured with a 20-second shutdown timeout so it can
    finish the notice in hand, and the SCM logs a service that overruns as a crash. Waiting
    here distinguishes a clean stop from one that was killed.

.EXAMPLE
    .\Stop-OmsLoanService.ps1
#>
[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serviceName = 'OmsLoanWorker'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    throw "Service '$serviceName' is not installed."
}

if ($service.Status -eq 'Stopped') {
    Write-Host 'Service is already stopped.'
    return
}

Write-Host "Stopping '$serviceName'."
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
Stop-Service -Name $serviceName

try {
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds($TimeoutSeconds))
    $stopwatch.Stop()
    Write-Host ("Service stopped cleanly in {0:N1}s." -f $stopwatch.Elapsed.TotalSeconds) -ForegroundColor Green
}
catch [System.ServiceProcess.TimeoutException] {
    throw "Service did not stop within $TimeoutSeconds seconds. Check the Application log for a hung shutdown."
}
