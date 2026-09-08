<#
.SYNOPSIS
    Stops and removes the OmsLoan notice review API service.

.DESCRIPTION
    Removes the service registration and, with it, the per-service environment block holding
    the listening address and the connection string.

    The Event Log source is left in place by default: removing it discards the service's
    history in the Application log, which is usually the first thing you want to read after
    an uninstall. Pass -RemoveEventLogSource to drop it as well.

    Nothing in the database, the published binaries, or the URL reservations is touched. A
    reservation made with `netsh http add urlacl` outlives the service and has to be removed
    separately if the port is being retired — pass -ShowUrlReservations to be reminded of
    which ones this service was configured with before the environment block goes.

    The Worker service is not affected. The two are independent registrations.

.EXAMPLE
    .\Uninstall-OmsLoanApiService.ps1

.NOTES
    Run from an elevated PowerShell session.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$RemoveEventLogSource,

    [switch]$ShowUrlReservations
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serviceName = 'OmsLoanApi'
$eventLogSource = 'OmsLoanApi'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script must be run from an elevated PowerShell session.'
    }
}

Assert-Administrator

# Read before the registration is deleted — afterwards there is nothing left to read it from.
if ($ShowUrlReservations) {
    $serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
    $block = (Get-ItemProperty -Path $serviceKey -Name 'Environment' -ErrorAction SilentlyContinue).Environment
    $configuredUrls = $block | Where-Object { $_ -like 'ASPNETCORE_URLS=*' } | ForEach-Object { ($_ -split '=', 2)[1] }
    if ($configuredUrls) {
        Write-Host "This service was configured to listen on: $configuredUrls"
        Write-Host 'Any matching URL reservation survives this uninstall. Remove one with:'
        Write-Host '  netsh http delete urlacl url=<url>/'
        Write-Host ''
    }
}

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

    Write-Host 'Service removed. Its environment block, including the stored connection string, went with it.' -ForegroundColor Green
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
