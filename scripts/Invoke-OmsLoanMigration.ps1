[CmdletBinding()]
param(
    [string]$ServiceName = 'OmsLoanWorker'
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
        $name = $line.Substring(0, $idx)
        $value = $line.Substring($idx + 1)
        $map[$name] = $value
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

$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$serviceMap = Get-RegistryEnvironmentMap -Path $serviceKey
$machineMap = Get-MachineEnvironmentMap

$provider = Resolve-Setting -Name 'Database__Provider' -ServiceMap $serviceMap -MachineMap $machineMap
$connection = Resolve-Setting -Name 'ConnectionStrings__OmsLoan' -ServiceMap $serviceMap -MachineMap $machineMap

if ([string]::IsNullOrWhiteSpace($connection)) {
    throw "No ConnectionStrings__OmsLoan found in service '$ServiceName' registry block, machine environment, or process environment."
}

$normalized = if ([string]::IsNullOrWhiteSpace($provider)) { 'SqlServer' } else { $provider.Trim() }

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    & dotnet tool restore | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with $LASTEXITCODE" }

    if ($normalized -eq 'Postgres') {
        $project = 'src/OmsLoan.Data.Postgres/OmsLoan.Data.Postgres.csproj'
        $startup = 'src/OmsLoan.Data.Postgres/OmsLoan.Data.Postgres.csproj'
    }
    elseif ($normalized -eq 'SqlServer') {
        $project = 'src/OmsLoan.Domain/OmsLoan.Domain.csproj'
        $startup = 'src/OmsLoan.Domain/OmsLoan.Domain.csproj'
    }
    else {
        throw "Database__Provider must be blank, SqlServer, or Postgres. Got: $normalized"
    }

    Write-Host "Provider: $normalized"
    Write-Host "Project: $project"

    & dotnet build $project -c Release --verbosity quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with $LASTEXITCODE" }

    & dotnet ef database update --project $project --startup-project $startup --connection $connection
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef database update failed with $LASTEXITCODE" }

    $after = & dotnet ef migrations list --project $project --startup-project $startup --connection $connection
    Write-Host "Migrations (list after update):"
    $after | ForEach-Object { Write-Host $_ }
}
finally {
    Pop-Location
}
