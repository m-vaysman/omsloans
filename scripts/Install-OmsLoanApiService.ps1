<#
.SYNOPSIS
    Installs the OmsLoan notice review API as a self-hosted Windows Service.

.DESCRIPTION
    Registers the service, configures automatic restart on failure, creates the Event Log
    source, and sets the per-service environment variables the Api reads its configuration
    from — including the address Kestrel listens on.

    Kestrel self-hosts: there is no IIS, no application pool and no web.config. The SCM is
    what starts the process on boot and restarts it after a crash, exactly as it does for
    OmsLoanWorker. The two services are independent; installing one does not touch the other.

    Secrets are written to the service's own registry environment block rather than to any
    file in the repository. That block is readable by local administrators — an accepted
    trade-off documented in docs/api-windows-service.md, along with the DPAPI alternative for
    environments where it is not acceptable.

    Re-runnable: an existing service is stopped and removed first.

.PARAMETER PublishPath
    Folder containing OmsLoan.Api.exe, i.e. the output of `dotnet publish`. If the React
    review UI was published with it, wwwroot sits alongside and is served by this process.

.PARAMETER ServiceAccount
    Account the service logs on as. Omit to use LocalSystem, which is convenient for a first
    install and wrong for production — see docs/api-windows-service.md.

.PARAMETER Environment
    Value for ASPNETCORE_ENVIRONMENT. Selects which appsettings.{Environment}.json applies.
    Note the prefix: the Worker uses DOTNET_ENVIRONMENT, but a web host reads both and the
    ASPNETCORE_ one wins, so setting that is the unambiguous choice here.

.PARAMETER Urls
    Semicolon-separated addresses for Kestrel, stored as ASPNETCORE_URLS. Defaults to
    http://+:5080, which listens on every interface.

    Kestrel binds sockets directly and does not use HTTP.sys, so none of these forms needs a
    `netsh http add urlacl` reservation and none of them needs an elevated service account —
    verified on Windows 10 as a standard user with no reservations present. A URL ACL would
    only matter if this host were switched to UseHttpSys(). What does still gate reachability
    from another machine is the firewall.

.PARAMETER ConnectionString
    SQL Server connection string. Stored as the ConnectionStrings__OmsLoan service variable —
    the same variable name the Worker uses, because both services read one database.

.EXAMPLE
    .\Install-OmsLoanApiService.ps1 -PublishPath C:\Services\OmsLoanApi -Environment Production `
        -ServiceAccount 'CONTOSO\svc_omsloan_api' -Urls 'http://+:5080' `
        -ConnectionString 'Server=sql01;Database=OmsLoan;Integrated Security=true;Encrypt=true'

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

    [string]$Urls = 'http://+:5080',

    [string]$ConnectionString,

    [switch]$StartAfterInstall
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Service identity. Keep in step with src/OmsLoan.Api/ServiceMetadata.cs.
$serviceName = 'OmsLoanApi'
$displayName = 'OmsLoan Notice Review API'
$description = 'Self-hosted Kestrel process serving the notice review API and the React review UI. Reads notices and extractions produced by the OmsLoan Notice Extraction Worker and records reviewer corrections against them.'
$eventLogSource = 'OmsLoanApi'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script must be run from an elevated PowerShell session.'
    }
}

Assert-Administrator

$exePath = Join-Path $PublishPath 'OmsLoan.Api.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "OmsLoan.Api.exe not found at '$exePath'. Run: dotnet publish src/OmsLoan.Api -c Release -o '$PublishPath'"
}
$exePath = (Resolve-Path -LiteralPath $exePath).Path

# The React build is optional — an API-only deployment is supported — but its absence is
# almost always an accident, and the symptom in a browser is a blank page rather than an
# error. Say so here, where it is still cheap to fix.
$spaIndex = Join-Path $PublishPath 'wwwroot\index.html'
$spaPresent = Test-Path -LiteralPath $spaIndex
if (-not $spaPresent) {
    Write-Warning "No wwwroot\index.html under '$PublishPath'. The API will run, but this process will not serve the React review UI. Publish with the OmsLoan.Web build included — see docs/api-windows-service.md."
}

# The bind failures that actually happen with Kestrel are a port already in use and a port
# inside a Windows excluded range (Hyper-V and WSL reserve blocks of them). Both surface as a
# service that registers fine and then stops on its first start, so they are worth catching
# while there is still a console to print to. A URL reservation is not among the failure
# modes: Kestrel does not go through HTTP.sys.
foreach ($url in @($Urls -split ';' | Where-Object { $_ })) {
    $port = [int](($url.Trim().TrimEnd('/') -split ':')[-1])

    if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) {
        Write-Warning "Port $port is already in use. The service will register but fail to start until whatever holds it is stopped."
    }

    $excluded = (netsh interface ipv4 show excludedportrange protocol=tcp) |
        Select-String -Pattern '^\s*(\d+)\s+(\d+)' |
        ForEach-Object { [pscustomobject]@{ Start = [int]$_.Matches[0].Groups[1].Value; End = [int]$_.Matches[0].Groups[2].Value } } |
        Where-Object { $port -ge $_.Start -and $port -le $_.End }

    if ($excluded) {
        Write-Warning "Port $port falls inside a Windows excluded port range ($($excluded[0].Start)-$($excluded[0].End)). Kestrel cannot bind it. Pick a port outside that range."
    }
}

# --- Remove any existing installation so the script is re-runnable -----------------------
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    Write-Host 'Existing service found. Stopping and removing it.'
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
    Write-Warning 'No -ServiceAccount given; installing as LocalSystem. Fine for a first install, wrong for production — see docs/api-windows-service.md.'
}

Write-Host "Registering service '$serviceName'."
New-Service @newServiceArgs | Out-Null

# Delayed start: the database and the network are usually not ready at the instant the
# machine reaches the desktop, and a failed first bind or connection just burns a restart.
& sc.exe config $serviceName start= delayed-auto | Out-Null

# --- Restart on failure ------------------------------------------------------------------
# Not exposed by New-Service, so sc.exe it is. Back off 1m, 2m, then 5m for subsequent
# failures, and reset the counter after a day of running cleanly. Same shape as the Worker:
# the failures that actually happen here are a port already in use and SQL Server not yet
# accepting connections, and neither is helped by retrying every few seconds.
Write-Host 'Configuring automatic restart on failure.'
& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/120000/restart/300000 | Out-Null
& sc.exe failureflag $serviceName 1 | Out-Null

# --- Event Log source --------------------------------------------------------------------
# Created here because registering a source needs administrator rights that the service
# account is deliberately not granted; the Api would fail its first write otherwise. A source
# distinct from the Worker's is what lets Event Viewer separate review problems from
# ingestion problems.
if (-not [System.Diagnostics.EventLog]::SourceExists($eventLogSource)) {
    Write-Host "Creating Event Log source '$eventLogSource'."
    New-EventLog -LogName 'Application' -Source $eventLogSource
}

# --- Per-service environment variables ---------------------------------------------------
# A service does not inherit variables set with setx. Its own block lives in the registry as
# a REG_MULTI_SZ, which is what makes the environment name, the listening address and the
# connection string visible to it and to nothing else on the machine.
$environmentEntries = @(
    "ASPNETCORE_ENVIRONMENT=$Environment"
    "ASPNETCORE_URLS=$Urls"
)

if ($ConnectionString) {
    $environmentEntries += "ConnectionStrings__OmsLoan=$ConnectionString"
}

$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
Set-ItemProperty -Path $serviceKey -Name 'Environment' -Value $environmentEntries -Type MultiString

# Names only. Printing the block would put the connection string on screen and into any
# transcript of this session.
Write-Host "Set $($environmentEntries.Count) service environment variable(s): $((($environmentEntries | ForEach-Object { ($_ -split '=', 2)[0] }) -join ', '))"

# --- Done ---------------------------------------------------------------------------------
$spaStatus = if ($spaPresent) { 'served from wwwroot by this process' } else { 'NOT deployed' }
$ports = @($Urls -split ';' | Where-Object { $_ } | ForEach-Object { ($_.Trim().TrimEnd('/') -split ':')[-1] })

Write-Host ''
Write-Host "Installed '$displayName' ($serviceName)." -ForegroundColor Green
Write-Host "  Listening on : $Urls"
Write-Host "  Review UI    : $spaStatus"
Write-Host ''
Write-Host 'Still to do by hand — see docs/api-windows-service.md:' -ForegroundColor Yellow
Write-Host '  1. Grant the service account the "Log on as a service" right.'
Write-Host '  2. Open the port on the firewall if reviewers are on other machines. Kestrel'
Write-Host '     needs no netsh URL reservation — it binds sockets directly, not through'
Write-Host '     HTTP.sys — so the firewall is the only thing between it and the network:'
foreach ($port in $ports) {
    Write-Host "       New-NetFirewallRule -DisplayName 'OmsLoan Review API' -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow"
}
Write-Host '  3. Create its SQL Server login and map it to db_datareader, db_datawriter on the'
Write-Host '     OmsLoan database.'
Write-Host '  4. If any URL is https, bind a certificate — see docs/api-windows-service.md.'
Write-Host ''

if ($StartAfterInstall) {
    & (Join-Path $PSScriptRoot 'Start-OmsLoanApiService.ps1')
}
else {
    Write-Host 'Start it with: .\Start-OmsLoanApiService.ps1'
}
