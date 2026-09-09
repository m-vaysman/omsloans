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
    'ConnectionStrings__OmsLoan', 'Ingestion__WatchedFolder'
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

# A folder that exists and is readable but denies writes. An existence-only check would pass
# here, which is the point: on a real host the drop folder almost always already exists.
$lockedFolder = Join-Path $env:TEMP ('OmsLoanLocked-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $lockedFolder -Force | Out-Null
$lockedAcl = Get-Acl $lockedFolder
$lockedAcl.SetAccessRuleProtection($true, $false)
$whoami = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$lockedAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($whoami,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')))
$lockedAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($whoami,'Write','ContainerInherit,ObjectInherit','None','Deny')))
Set-Acl -Path $lockedFolder -AclObject $lockedAcl

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

# A fresh folder per run, so the "created when missing" case is genuinely missing.
$watchRoot = Join-Path $env:TEMP ('OmsLoanWatch-' + [guid]::NewGuid().ToString('N'))

$complete = @{
    ConnectionStrings__OmsLoan = $goodDb
    Ingestion__WatchedFolder = $watchRoot
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

Test-Case 'nothing set names every required variable at once' @{} {
    param($r)
    if ($r.ExitCode -ne 78) { return "expected exit 78, got $($r.ExitCode)" }
    foreach ($v in 'ConnectionStrings__OmsLoan', 'Ingestion__WatchedFolder', 'GRAPH_TENANT_ID', 'GRAPH_CLIENT_ID', 'GRAPH_CLIENT_SECRET') {
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

Test-Case 'no watched folder refuses, naming the variable' ($complete.Clone() | ForEach-Object { $_.Remove('Ingestion__WatchedFolder'); $_ }) {
    param($r)
    if ($r.ExitCode -ne 78) { return "expected exit 78, got $($r.ExitCode)" }
    if ($r.Log -notmatch 'Ingestion__WatchedFolder') { return 'did not name the missing variable' }
    $null
}

Test-Case 'watched folder and subfolders are created when missing' $complete {
    param($r)
    foreach ($sub in '', 'processed', 'failed') {
        $path = if ($sub) { Join-Path $watchRoot $sub } else { $watchRoot }
        if (-not (Test-Path -LiteralPath $path)) { return "did not create $path" }
    }
    # The write probe must not survive: a stray file in a folder ingestion scans would
    # eventually be picked up as a notice.
    $strays = Get-ChildItem -LiteralPath $watchRoot -Recurse -File -ErrorAction SilentlyContinue
    if ($strays) { return "left files behind: $($strays.Name -join ', ')" }
    $null
}

Test-Case 'existing folder and its contents are left alone' $complete {
    param($r)
    # Seeded in processed\ rather than the watch root: startup must not disturb existing
    # content, but a file left in the root is a notice, and ingestion is supposed to consume
    # it. Only the archive folder can distinguish "left alone" from "ingested".
    $seeded = Join-Path $watchRoot 'processed\already-here.pdf'
    Set-Content -LiteralPath $seeded -Value 'notice' -Encoding utf8
    $again = Invoke-Worker $complete
    if (-not (Test-Path -LiteralPath $seeded)) { return 'an existing file was removed' }
    if ((Get-Content -LiteralPath $seeded -Raw).Trim() -ne 'notice') { return 'an existing file was modified' }
    Remove-Item -LiteralPath $seeded -Force
    $null
}

Test-Case 'unwritable watched folder refuses, naming it' ($complete.Clone() | ForEach-Object { $_['Ingestion__WatchedFolder'] = $script:lockedFolder; $_ }) {
    param($r)
    if ($r.ExitCode -ne 78) { return "expected exit 78, got $($r.ExitCode)" }
    if ($r.Log -notmatch 'cannot write to') { return 'did not report a write failure' }
    if ($r.Log -notmatch [regex]::Escape($script:lockedFolder)) { return 'did not name the folder' }
    $null
}

Test-Case 'no secret value is ever logged' ($complete.Clone() | ForEach-Object { $_['CLAUDE_API_KEY'] = 'sk-should-never-appear'; $_ }) {
    param($r)
    if ($r.Log -match 'sk-should-never-appear') { return 'a secret value appeared in the log' }
    if ($r.Log -notmatch 'present \(from CLAUDE_API_KEY\)') { return 'did not report the key as present' }
    $null
}

# Drop the deny rule, then remove. icacls rather than Set-Acl: writing back a descriptor read
# from a protection-enabled folder asks for SeSecurityPrivilege, which an unelevated session
# does not hold — and this script has to run unelevated.
#
# Wrapped, because cleanup must never turn a passing run into a failure.
try {
    # Grant, not just un-deny. Inheritance was switched off when the folder was locked, so
    # removing the deny leaves only the ReadAndExecute rule — which cannot delete.
    & icacls $lockedFolder /remove:d $whoami /t /c | Out-Null
    & icacls $lockedFolder /grant "${whoami}:(OI)(CI)F" /t /c | Out-Null
    Remove-Item -LiteralPath $lockedFolder -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $watchRoot -Recurse -Force -ErrorAction SilentlyContinue
}
catch {
    Write-Host "  (cleanup left $lockedFolder behind: $($_.Exception.Message))"
}

Write-Host ''
if ($failures -eq 0) {
    Write-Host 'All startup checks passed.' -ForegroundColor Green
}
else {
    Write-Host "$failures check(s) failed." -ForegroundColor Red
    exit 1
}
