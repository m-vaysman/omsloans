<#
.SYNOPSIS
    Installs the OmsLoan notice-extraction worker as a Windows Service.

.DESCRIPTION
    Registers the service, configures automatic restart on failure, creates the Event Log
    source, and sets the per-service environment variables the Worker reads its
    configuration from.

    Secrets are written to the service's own registry environment block rather than to any
    file in the repository. That block is readable by local administrators — an accepted
    trade-off documented in docs/windows-service.md, along with the DPAPI alternative for
    environments where it is not acceptable.

    Re-runnable: an existing service is stopped and removed first.

.PARAMETER PublishPath
    Folder containing OmsLoan.Worker.exe, i.e. the output of `dotnet publish`.

.PARAMETER ServiceAccount
    Account the service logs on as. Omit to use LocalSystem, which is convenient for a first
    install and wrong for production — see docs/windows-service.md.

.PARAMETER Environment
    Value for DOTNET_ENVIRONMENT. Selects which appsettings.{Environment}.json applies.

.PARAMETER ConnectionString
    SQL Server connection string. Stored as the ConnectionStrings__OmsLoan service variable.

.PARAMETER ApiKeys
    Optional hashtable of provider keys, e.g. @{ Claude = 'sk-...'; OpenAi = 'sk-...' }.
    Each becomes Extraction__<Provider>__ApiKey.

.EXAMPLE
    .\Install-OmsLoanService.ps1 -PublishPath C:\Services\OmsLoan -Environment Production `
        -ServiceAccount 'CONTOSO\svc_omsloan' -ConnectionString 'Server=sql01;Database=OmsLoan;Integrated Security=true;Encrypt=true'

.NOTES
    Run from an elevated PowerShell session.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishPath,

    [string]$ServiceAccount,

    [ValidateSet('Production', 'Staging', 'Development')]
    [string]$Environment = 'Production',

    [string]$ConnectionString,

    [hashtable]$ApiKeys = @{},

    [switch]$StartAfterInstall
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Service identity. Keep in step with src/OmsLoan.Worker/ServiceMetadata.cs.
$serviceName = 'OmsLoanWorker'
$displayName = 'OmsLoan Notice Extraction Worker'
$description = 'Ingests agent-bank loan notices from a watched folder and a shared mailbox, extracts their economic data with an LLM provider, and stores each attempt for human review.'
$eventLogSource = 'OmsLoanWorker'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script must be run from an elevated PowerShell session.'
    }
}

Assert-Administrator

$exePath = Join-Path $PublishPath 'OmsLoan.Worker.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "OmsLoan.Worker.exe not found at '$exePath'. Run: dotnet publish src/OmsLoan.Worker -c Release -o '$PublishPath'"
}
$exePath = (Resolve-Path -LiteralPath $exePath).Path

# --- Remove any existing installation so the script is re-runnable -----------------------
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    Write-Host "Existing service found. Stopping and removing it."
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    # Remove-Service needs PowerShell 6+; sc.exe covers Windows PowerShell 5.1 as well.
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

# --- Register the service ----------------------------------------------------------------
$newServiceArgs = @{
    Name           = $serviceName
    BinaryPathName = "`"$exePath`""
    DisplayName    = $displayName
    Description    = $description
    StartupType    = 'Automatic'
}

if ($ServiceAccount) {
    # Prompted rather than taken as a parameter so the password is never in a command line,
    # a script file, or PSReadLine history.
    $credential = Get-Credential -UserName $ServiceAccount -Message "Password for the $serviceName service account"
    $newServiceArgs['Credential'] = $credential
}
else {
    Write-Warning 'No -ServiceAccount given; installing as LocalSystem. Fine for a first install, wrong for production — see docs/windows-service.md.'
}

Write-Host "Registering service '$serviceName'."
New-Service @newServiceArgs | Out-Null

# Delayed start: the database and the network are usually not ready at the instant the
# machine reaches the desktop, and a failed first connection just burns a restart.
& sc.exe config $serviceName start= delayed-auto | Out-Null

# --- Restart on failure ------------------------------------------------------------------
# Not exposed by New-Service, so sc.exe it is. Back off 1m, 2m, then 5m for subsequent
# failures, and reset the counter after a day of running cleanly.
Write-Host 'Configuring automatic restart on failure.'
& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/120000/restart/300000 | Out-Null
& sc.exe failureflag $serviceName 1 | Out-Null

# --- Event Log source --------------------------------------------------------------------
# Created here because registering a source needs administrator rights that the service
# account is deliberately not granted; the Worker would fail its first write otherwise.
if (-not [System.Diagnostics.EventLog]::SourceExists($eventLogSource)) {
    Write-Host "Creating Event Log source '$eventLogSource'."
    New-EventLog -LogName 'Application' -Source $eventLogSource
}

# --- Per-service environment variables ---------------------------------------------------
# A service does not inherit variables set with setx. Its own block lives in the registry as
# a REG_MULTI_SZ, which is what makes DOTNET_ENVIRONMENT and the secrets visible to it and
# to nothing else on the machine.
$environmentEntries = @("DOTNET_ENVIRONMENT=$Environment")

if ($ConnectionString) {
    $environmentEntries += "ConnectionStrings__OmsLoan=$ConnectionString"
}

foreach ($provider in $ApiKeys.Keys) {
    $environmentEntries += "Extraction__${provider}__ApiKey=$($ApiKeys[$provider])"
}

$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
Set-ItemProperty -Path $serviceKey -Name 'Environment' -Value $environmentEntries -Type MultiString

# Names only. Printing the block would put the connection string and every API key on screen
# and into any transcript of this session.
Write-Host "Set $($environmentEntries.Count) service environment variable(s): $((($environmentEntries | ForEach-Object { ($_ -split '=', 2)[0] }) -join ', '))"

# --- Done ---------------------------------------------------------------------------------
Write-Host ''
Write-Host "Installed '$displayName' ($serviceName)." -ForegroundColor Green
Write-Host ''
Write-Host 'Still to do by hand — see docs/windows-service.md:' -ForegroundColor Yellow
Write-Host '  1. Grant the service account the "Log on as a service" right.'
Write-Host '  2. Grant it Modify on the watched folder and its processed/duplicates/failed subfolders.'
Write-Host '  3. Create its SQL Server login and map it to db_datareader, db_datawriter on the OmsLoan database.'
Write-Host ''

if ($StartAfterInstall) {
    & (Join-Path $PSScriptRoot 'Start-OmsLoanService.ps1')
}
else {
    Write-Host "Start it with: .\Start-OmsLoanService.ps1"
}
