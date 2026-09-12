[CmdletBinding()]
param(
    [string]$ServiceName = 'OmsLoanWorker',
    [switch]$MigrateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-RegistryEnvironmentMap {
    param([string]$Path)
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $map }
    $block = (Get-ItemProperty -Path $Path -Name Environment -ErrorAction SilentlyContinue).Environment
    if (-not $block) { return $map }
    foreach ($line in $block) {
        $idx = $line.IndexOf('=')
        if ($idx -lt 1) { continue }
        $map[$line.Substring(0, $idx)] = $line.Substring($idx + 1)
    }
    return $map
}

function Get-MachineEnvironmentMap {
    $path = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
    $map = @{}
    if (-not (Test-Path -LiteralPath $path)) { return $map }
    $props = Get-ItemProperty -Path $path
    foreach ($p in $props.PSObject.Properties) {
        if ($p.Name -in @('PSPath', 'PSParentPath', 'PSChildName', 'PSDrive', 'PSProvider')) { continue }
        $map[$p.Name] = [string]$p.Value
    }
    return $map
}

function Resolve-Setting {
    param(
        [string]$Name,
        [hashtable]$ServiceMap,
        [hashtable]$MachineMap
    )
    if ($ServiceMap.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($ServiceMap[$Name])) {
        return $ServiceMap[$Name]
    }
    if ($MachineMap.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($MachineMap[$Name])) {
        return $MachineMap[$Name]
    }
    $fromProcess = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if (-not [string]::IsNullOrWhiteSpace($fromProcess)) { return $fromProcess }
    $fromMachine = [Environment]::GetEnvironmentVariable($Name, 'Machine')
    if (-not [string]::IsNullOrWhiteSpace($fromMachine)) { return $fromMachine }
    return $null
}

if ($MigrateOnly) {
    & (Join-Path $PSScriptRoot 'Invoke-OmsLoanMigration.ps1') -ServiceName $ServiceName
    if ($LASTEXITCODE -ne 0) { throw "Invoke-OmsLoanMigration.ps1 failed with $LASTEXITCODE" }
    return
}

$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$serviceMap = Get-RegistryEnvironmentMap -Path $serviceKey
$machineMap = Get-MachineEnvironmentMap
$provider = Resolve-Setting -Name 'Database__Provider' -ServiceMap $serviceMap -MachineMap $machineMap
$normalized = if ([string]::IsNullOrWhiteSpace($provider)) { 'SqlServer' } else { $provider.Trim() }

if ($normalized -ne 'Postgres') {
    Write-Host "Database__Provider is '$normalized' (not Postgres). Leaving Docker Postgres alone."
    return
}

$connection = Resolve-Setting -Name 'ConnectionStrings__OmsLoan' -ServiceMap $serviceMap -MachineMap $machineMap
$port = 5432
if (-not [string]::IsNullOrWhiteSpace($connection) -and $connection -match '(?i)(?:^|;)Port=(\d+)') {
    $port = [int]$Matches[1]
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$composeFile = Join-Path $repoRoot 'docker-compose.yml'
if (-not (Test-Path -LiteralPath $composeFile)) {
    throw "docker-compose.yml not found at $composeFile"
}

$ps = Get-Command docker -ErrorAction SilentlyContinue
if ($null -eq $ps) {
    throw "Docker CLI not found. Install Docker so postgres:17 can run, or point ConnectionStrings__OmsLoan at an existing Postgres."
}

$composeRunning = $false
try {
    $ids = & docker compose -p omsloan -f $composeFile ps -q postgres 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($ids | Out-String).Trim())) {
        $status = & docker compose -p omsloan -f $composeFile ps --format json postgres 2>$null
        $composeRunning = $true
        Write-Host "Compose project omsloan postgres is present; leaving it alone."
        return
    }
} catch {
}

$listener = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -ne $listener) {
    $proc = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    $procName = if ($null -ne $proc) { $proc.ProcessName } else { "pid $($listener.OwningProcess)" }
    Write-Host "Port $port already listening ($procName). Leaving it alone."
    return
}

& docker info 1>$null 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Docker engine is not reachable (docker info failed). Do not confuse this with a stopped container. Fix Docker access for this account, or use a native Postgres that already listens on the port."
}

Write-Host "Starting compose project omsloan."
& docker compose -p omsloan -f $composeFile up -d --wait
if ($LASTEXITCODE -ne 0) { throw "docker compose up failed with $LASTEXITCODE" }
Write-Host "Compose project omsloan is up."
