<#
.SYNOPSIS
    Starts the OmsLoan review API service, shows its startup banner, and checks it answers.

.DESCRIPTION
    Starts the service, waits for it to report Running, prints the recent Application log
    entries from it, and then makes one request to confirm it is actually serving.

    The banner is the point. It names the environment that was selected, the addresses
    Kestrel bound, whether a React build was found, and which source supplied the connection
    string — which is what tells you whether the service came up against the configuration
    you intended. A service that starts cleanly against the wrong database looks identical to
    one that started correctly until you read that line.

    The request afterwards catches the failure mode the banner cannot: Running is a statement
    about the process, not about the socket. A URL reservation that was never made, or a port
    taken by something else, produces a service the SCM is content with and a page nobody can
    open.

.EXAMPLE
    .\Start-OmsLoanApiService.ps1
#>
[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 60,
    [int]$LogEntries = 15,
    [switch]$SkipRequest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serviceName = 'OmsLoanApi'
$eventLogSource = 'OmsLoanApi'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    throw "Service '$serviceName' is not installed. Run .\Install-OmsLoanApiService.ps1 first."
}

$startedAt = Get-Date

if ($service.Status -eq 'Running') {
    Write-Host 'Service is already running.'
}
else {
    Write-Host "Starting '$serviceName'."
    Start-Service -Name $serviceName
    try {
        $service.WaitForStatus('Running', [TimeSpan]::FromSeconds($TimeoutSeconds))
        Write-Host 'Service is running.' -ForegroundColor Green
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
        Write-Host ('[{0:HH:mm:ss}] {1}' -f $_.TimeCreated, $_.LevelDisplayName)
        Write-Host $_.Message
        Write-Host ''
    }
}

if ($SkipRequest) {
    return
}

# --- Does it actually answer? -------------------------------------------------------------
$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
$block = (Get-ItemProperty -Path $serviceKey -Name 'Environment' -ErrorAction SilentlyContinue).Environment
$configured = $block | Where-Object { $_ -like 'ASPNETCORE_URLS=*' } | ForEach-Object { ($_ -split '=', 2)[1] }

if (-not $configured) {
    Write-Host '--- Reachability ------------------------------------------------------------'
    Write-Host 'No ASPNETCORE_URLS on the service, so Kestrel is on its own default of'
    Write-Host 'http://localhost:5000 — which no other machine can reach. Re-run the installer'
    Write-Host 'with -Urls. See docs/api-windows-service.md.'
    return
}

Write-Host '--- Reachability ------------------------------------------------------------'
foreach ($url in @($configured -split ';' | Where-Object { $_ })) {
    # A wildcard or + binds every interface but is not a host name a client can resolve, so
    # the probe goes to localhost on the same port and scheme.
    $probe = $url.Trim().TrimEnd('/') -replace '://(\+|\*|0\.0\.0\.0)', '://localhost'
    try {
        $response = Invoke-WebRequest -Uri $probe -UseBasicParsing -TimeoutSec 10
        Write-Host ('  {0} -> HTTP {1}' -f $probe, [int]$response.StatusCode) -ForegroundColor Green
    }
    catch [System.Net.WebException] {
        # An HTTP status is a success for this check: something answered. A 404 is exactly
        # what an API-only deployment returns for /, and says the socket is live.
        # -SkipHttpErrorCheck would say this more directly but is PowerShell 7 only, and the
        # rest of these scripts run on Windows PowerShell 5.1.
        if ($null -ne $_.Exception.Response) {
            Write-Host ('  {0} -> HTTP {1}' -f $probe, [int]$_.Exception.Response.StatusCode) -ForegroundColor Green
        }
        else {
            Write-Warning ('  {0} -> no response: {1}' -f $probe, $_.Exception.Message)
            Write-Host '    Running but not listening usually means the URL reservation is missing:'
            Write-Host "      netsh http add urlacl url=$($url.Trim().TrimEnd('/'))/ user='<service account>'"
        }
    }
    catch {
        Write-Warning ('  {0} -> no response: {1}' -f $probe, $_.Exception.Message)
    }
}
