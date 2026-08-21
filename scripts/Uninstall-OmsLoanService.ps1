<#
.SYNOPSIS
    Stops and removes the OmsLoan notice-extraction worker service.

.DESCRIPTION
    Removes the service registration and, with it, the per-service environment block holding
    the connection string and any API keys.

    The Event Log source is left in place by default: removing it discards the service's
    history in the Application log, which is usually the first thing you want to read after
    an uninstall. Pass -RemoveEventLogSource to drop it as well.

    Nothing in the database, the watched folder, or the published binaries is touched.

.EXAMPLE
    .\Uninstall-OmsLoanService.ps1

.NOTES
    Run from an elevated PowerShell session.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$RemoveEventLogSource
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serviceName = 'OmsLoanWorker'
$eventLogSource = 'OmsLoanWorker'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script must be run from an elevated PowerShell session.'
    }
}

Assert-Administrator

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Service '$serviceName' is not installed. Nothing to do."
}
elseif ($PSCmdlet.ShouldProcess($serviceName, 'Stop and remove Windows Service')) {
    if ($service.Status -ne 'Stopped') {
        Write-Host 'Stopping service.'
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }

    Write-Host "Removing service '$serviceName'."
    & sc.exe delete $serviceName | Out-Null

    Write-Host 'Service removed. Its environment block, including any stored secrets, went with it.' -ForegroundColor Green
}

if ($RemoveEventLogSource) {
    if ([System.Diagnostics.EventLog]::SourceExists($eventLogSource)) {
        if ($PSCmdlet.ShouldProcess($eventLogSource, 'Remove Event Log source')) {
            Remove-EventLog -Source $eventLogSource
            Write-Host "Removed Event Log source '$eventLogSource'."
        }
    }
}
else {
    Write-Host "Event Log source '$eventLogSource' left in place; pass -RemoveEventLogSource to drop it."
}
