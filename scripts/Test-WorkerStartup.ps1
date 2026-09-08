<#
.SYNOPSIS
    Checks that the Worker starts, or refuses to, for each configuration.

.DESCRIPTION
    Runs the published Worker once per case with a different set of environment variables and
    checks the exit code and the log. No service, no admin, no database — the refusal happens
    before anything connects to anything.

    The success case is the awkward one: a Worker that starts never exits, so that case is
    given a few seconds and judged on whether it got as far as logging that it started.

.EXAMPLE
    .\Test-WorkerStartup.ps1
#>
[CmdletBinding()]
param(
    [string]$PublishPath = (Join-Path $env:TEMP 'OmsLoanWorkerStartupCheck'),
    [int]$StartSeconds = 8
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path $PSScriptRoot -Parent
$goodDb = 'Server=(localdb)\MSSQLLocalDB;Database=OmsLoan;Trusted_Connection=true'

Write-Host 'Publishing...' -ForegroundColor Cyan
& dotnet publish (Join-Path $repoRoot 'src\OmsLoan.Worker') -c Release -o $PublishPath --nologo | Out-Null
$exe = Join-Path $PublishPath 'OmsLoan.Worker.exe'

# Every variable the Worker looks at, cleared. Each case then sets only what it needs, so a
# case cannot accidentally pass on a value inherited from this machine.
$allVariables = @(
    'ConnectionStrings__OmsLoan'
    'GRAPH_TENANT_ID', 'GRAPH_CLIENT_ID', 'GRAPH_CLIENT_SECRET'
    'CLAUDE_API_KEY', 'OPEN_API_KEY', 'GROQ_API_KEY'
)

function Invoke-Worker([hashtable]$Variables) {
    $saved = @{}
    foreach ($name in $allVariables) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    foreach ($name in $Variables.Keys) {
        [Environment]::SetEnvironmentVariable($name, $Variables[$name], 'Process')
    }
    $env:DOTNET_ENVIRONMENT = 'Production'

    try {
        # System.Diagnostics.Process rather than Start-Process -PassThru: the latter returns
        # an object whose ExitCode reads back empty on Windows PowerShell 5.1 even once the
        # process has exited, which silently turns every "it refused" check into a pass.
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exe
        $psi.WorkingDirectory = $PublishPath
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true

        $process = [System.Diagnostics.Process]::Start($psi)

        # Read asynchronously: a synchronous ReadToEnd would block until exit, and the
        # success case never exits.
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()

        if ($process.WaitForExit($StartSeconds * 1000)) {
            $exitCode = $process.ExitCode
        }
        else {
            # Still running after the grace period, which for this Worker means it started.
            $exitCode = $null
            $process.Kill()
            [void]$process.WaitForExit(5000)
        }

        return [pscustomobject]@{
            ExitCode = $exitCode
            Log      = $stdout.Result + "`n" + $stderr.Result
        }
    }
    finally {
        foreach ($name in $allVariables) {
            [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process')
        }
    }
}

$failures = 0

function Test-Case([string]$Name, [hashtable]$Variables, [scriptblock]$Check) {
    $result = Invoke-Worker $Variables
    $problem = & $Check $result

    if ($null -eq $problem) {
        Write-Host ("  PASS  {0}" -f $Name) -ForegroundColor Green
    }
    else {
        Write-Host ("  FAIL  {0}" -f $Name) -ForegroundColor Red
        Write-Host ("        {0}" -f $problem)
        Write-Host ("        exit={0}" -f $result.ExitCode)
        $script:failures++
    }
}

$complete = @{
    ConnectionStrings__OmsLoan = $goodDb
    GRAPH_TENANT_ID = 'tenant'; GRAPH_CLIENT_ID = 'client'; GRAPH_CLIENT_SECRET = 'secret'
}

Write-Host ''
Write-Host 'Startup checks' -ForegroundColor Cyan

Test-Case 'complete configuration starts' $complete {
    param($r)
    if ($null -ne $r.ExitCode) { return "expected it to keep running, but it exited $($r.ExitCode)" }
    if ($r.Log -notmatch 'worker started') { return 'never logged that it started' }
    $null
}

Test-Case 'no database refuses, naming the variable' ($complete.Clone() | ForEach-Object { $_.Remove('ConnectionStrings__OmsLoan'); $_ }) {
    param($r)
    if ($r.ExitCode -ne 78) { return "expected exit 78, got $($r.ExitCode)" }
    if ($r.Log -notmatch 'ConnectionStrings__OmsLoan') { return 'did not name the missing variable' }
    $null
}

Test-Case 'no Graph secret refuses, naming the variable' ($complete.Clone() | ForEach-Object { $_.Remove('GRAPH_CLIENT_SECRET'); $_ }) {
    param($r)
    if ($r.ExitCode -ne 78) { return "expected exit 78, got $($r.ExitCode)" }
    if ($r.Log -notmatch 'GRAPH_CLIENT_SECRET') { return 'did not name the missing variable' }
    $null
}

Test-Case 'blank counts as missing' ($complete.Clone() | ForEach-Object { $_['GRAPH_TENANT_ID'] = '   '; $_ }) {
    param($r)
    if ($r.ExitCode -ne 78) { return "a blank value was accepted (exit $($r.ExitCode))" }
    $null
}

Test-Case 'nothing set names all four at once' @{} {
    param($r)
    if ($r.ExitCode -ne 78) { return "expected exit 78, got $($r.ExitCode)" }
    foreach ($v in 'ConnectionStrings__OmsLoan', 'GRAPH_TENANT_ID', 'GRAPH_CLIENT_ID', 'GRAPH_CLIENT_SECRET') {
        if ($r.Log -notmatch $v) { return "did not name $v" }
    }
    $null
}

Test-Case 'missing provider API keys still start' $complete {
    param($r)
    if ($null -ne $r.ExitCode) { return "refused to start over an optional key (exit $($r.ExitCode))" }
    if ($r.Log -notmatch 'OPEN_API_KEY') { return 'did not warn about the unset provider key' }
    $null
}

Test-Case 'no secret value is ever logged' ($complete.Clone() | ForEach-Object { $_['CLAUDE_API_KEY'] = 'sk-should-never-appear'; $_ }) {
    param($r)
    if ($r.Log -match 'sk-should-never-appear') { return 'a secret value appeared in the log' }
    if ($r.Log -notmatch 'present \(from CLAUDE_API_KEY\)') { return 'did not report the key as present' }
    $null
}

Write-Host ''
if ($failures -eq 0) {
    Write-Host 'All startup checks passed.' -ForegroundColor Green
}
else {
    Write-Host "$failures check(s) failed." -ForegroundColor Red
    exit 1
}
