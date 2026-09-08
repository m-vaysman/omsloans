# Running the Worker as a Windows Service

`OmsLoan.Worker` polls a watched folder and a shared mailbox and calls LLM providers as
notices arrive. It runs unattended, which is why it is a service rather than a desktop
application — see [ADR 0002](decisions/0002-windows-service-over-desktop.md).

> **There are two services.** `OmsLoan.Api` self-hosts the review API and the React UI on
> Kestrel and runs as `OmsLoanApi`, installed and recovered the same way —
> see [`api-windows-service.md`](api-windows-service.md). The two share `OmsLoan.Domain` and
> the database; neither project references the other, and neither service depends on the
> other at the SCM level. Everything below is about the Worker.

## Install

```powershell
dotnet publish src/OmsLoan.Worker -c Release -o C:\Services\OmsLoan

cd scripts
.\Install-OmsLoanService.ps1 `
    -PublishPath C:\Services\OmsLoan `
    -Environment Production `
    -ServiceAccount 'CONTOSO\svc_omsloan' `
    -ConnectionString 'Server=sql01;Database=OmsLoan;Integrated Security=true;Encrypt=true'
```

No secrets in that command beyond the connection string: when `CLAUDE_API_KEY`,
`GRAPH_TENANT_ID` and the rest are already set at machine scope, the service inherits them.
Pass `-ApiKeys` / `-GraphCredential` only to pin values to this service alone —
see [Configuration](#configuration).

Run elevated. The script prompts for the account password rather than taking it as a
parameter, so it never reaches a command line, a script file, or PSReadLine history.

| Script | Purpose |
| --- | --- |
| `Install-OmsLoanService.ps1` | Register, configure recovery, create Event Log source, set environment |
| `Uninstall-OmsLoanService.ps1` | Stop and remove; keeps the Event Log source unless `-RemoveEventLogSource` |
| `Start-OmsLoanService.ps1` | Start, wait for Running, print the startup banner |
| `Stop-OmsLoanService.ps1` | Stop and wait, distinguishing a clean stop from a kill |

`Install` is re-runnable — an existing service is stopped and removed first.

## Service account

The installer defaults to **LocalSystem**, which is fine for a first install and wrong for
production: LocalSystem is a full machine administrator and authenticates to SQL Server as
the computer account. Use a dedicated account.

**A group Managed Service Account (gMSA) is the better choice** where the domain supports
one — no password to store, rotate, or leak, and it cannot be used for an interactive
logon. Failing that, a normal domain account with a non-expiring password.

Three grants the installer cannot make for you:

**1. Log on as a service.** `secpol.msc` → Local Policies → User Rights Assignment → *Log
on as a service* → add the account. Without it the service fails to start with error 1069
and nothing appears in the Application log, because the process never runs.

**2. Watched-folder rights.** Ingestion moves files between subfolders, so read alone is not
enough:

| Path | Right |
| --- | --- |
| the watched folder | Modify |
| `processed\` | Modify |
| `duplicates\` | Modify |
| `failed\` | Modify |

```powershell
$account = 'CONTOSO\svc_omsloan'
foreach ($path in @('C:\OmsLoan\Notices', 'C:\OmsLoan\Notices\processed', 'C:\OmsLoan\Notices\duplicates', 'C:\OmsLoan\Notices\failed')) {
    $acl = Get-Acl $path
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $account, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -Path $path -AclObject $acl
}
```

**3. SQL Server login.**

```sql
CREATE LOGIN [CONTOSO\svc_omsloan] FROM WINDOWS;
USE OmsLoan;
CREATE USER [CONTOSO\svc_omsloan] FOR LOGIN [CONTOSO\svc_omsloan];
ALTER ROLE db_datareader ADD MEMBER [CONTOSO\svc_omsloan];
ALTER ROLE db_datawriter ADD MEMBER [CONTOSO\svc_omsloan];
```

Reader and writer only. The Worker inserts notices, extractions and extracted fields; it
never applies migrations. Schema changes are a deployment step run under a separate account
with `db_ddladmin`, so a compromised service account cannot alter the schema — and cannot
delete rows either, which matters given the append-only design in
[ADR 0003](decisions/0003-append-only-extractions-and-eav-fields.md).

## Configuration

Sources, **lowest precedence first** — a later source overrides an earlier one:

1. `appsettings.json` — committed, no secrets
2. `appsettings.{DOTNET_ENVIRONMENT}.json` — `Production` committed; `Development` is gitignored
3. **user-secrets** — Development only, stored under `%APPDATA%\Microsoft\UserSecrets\`
4. **environment variables** — how Production supplies secrets
5. command line

`DOTNET_ENVIRONMENT` selects which `appsettings.{Environment}.json` applies. The installer
sets it on the service; if it is missing the host defaults to `Production`.

### Key names

**Secrets are set as flat variables.** These names are the source of truth — they are what
the machines already carry, set for other tooling, and the Worker reads them directly:

| Setting | Environment variable | Configuration key in code |
| --- | --- | --- |
| Claude key | `CLAUDE_API_KEY` | `Extraction:Claude:ApiKey` |
| OpenAI key | `OPEN_API_KEY` | `Extraction:OpenAi:ApiKey` |
| Groq key | `GROQ_API_KEY` | `Extraction:Groq:ApiKey` |
| Graph tenant | `GRAPH_TENANT_ID` | `Graph:TenantId` |
| Graph app id | `GRAPH_CLIENT_ID` | `Graph:ClientId` |
| Graph secret | `GRAPH_CLIENT_SECRET` | `Graph:ClientSecret` |

`OPEN_API_KEY`, not `OPENAI_API_KEY`. It looks like a typo and is not — it is what is set on
the machines, so it is what is read.

Application code binds against the hierarchical keys in the right-hand column, which keeps
`appsettings.json` readable and options binding conventional.
[`FlatEnvironmentSecrets.cs`](../src/OmsLoan.Worker/FlatEnvironmentSecrets.cs) projects the
flat variables onto them and is registered as the **highest-precedence** configuration
source. So a flat variable beats appsettings, user-secrets, and anything else.

Why this exists at all: .NET's environment-variable provider only understands its own
`Section__Key` convention, so `CLAUDE_API_KEY` reached the Worker as *nothing*. A host with
every secret correctly configured was indistinguishable from a bare one.

**The database keeps the .NET convention**, because it is not one of these pre-existing
variables and the Api reads the same one:

| Setting | Configuration key | Environment variable |
| --- | --- | --- |
| Database | `ConnectionStrings:OmsLoan` | `ConnectionStrings__OmsLoan` |

<details>
<summary>The old <code>Extraction__Claude__ApiKey</code> spelling</summary>

Still resolves, because the double-underscore mapping is built into the environment-variable
provider and cannot be switched off. It is no longer written by the install script or
documented anywhere else, and **it loses to the flat name** when both are set — deliberately,
so a stale variable left on a host cannot shadow the real one. Treat it as deprecated and
delete it where you find it.

</details>

### Development

Machine variables work here too — if `CLAUDE_API_KEY` is already set, `dotnet run` picks it
up with no further setup. For per-project values, user-secrets take the hierarchical key:

```powershell
dotnet user-secrets set "ConnectionStrings:OmsLoan" "Server=(localdb)\MSSQLLocalDB;Database=OmsLoan;Trusted_Connection=true" --project src/OmsLoan.Worker
dotnet user-secrets set "Extraction:Claude:ApiKey" "..." --project src/OmsLoan.Worker
```

Note that a flat variable outranks user-secrets. If a machine-level `CLAUDE_API_KEY` is set
and you want a different one locally, unset the machine variable for that shell rather than
wondering why the secret is ignored — the startup banner names which source won.

User-secrets live outside the repository entirely, so there is no file to accidentally
commit. Copy `appsettings.Development.json.example` to `appsettings.Development.json` for
non-secret local overrides — that filename is gitignored.

### Production

There are two places a secret can live, and they are not alternatives so much as different
scopes.

**Machine-level variables reach the service already.** The SCM hands every service the
system environment block, so `CLAUDE_API_KEY` and the rest set with `setx /M` (or System
Properties → Environment Variables → System variables) are visible to the Worker with
nothing else to configure. That is the normal case on these hosts. **User-scope variables
are not** — a plain `setx` sets the current user's environment, which a service running as
another account never sees. That distinction is the usual reason a variable "is set" and the
Worker still reports it absent.

The system block is cached by the service control manager, so a newly added machine variable
is picked up on the next service start, and sometimes only after a reboot.

**The per-service block pins a value to this service alone.** When passed `-ApiKeys` or
`-GraphCredential`, the installer writes them to
`HKLM\SYSTEM\CurrentControlSet\Services\OmsLoanWorker\Environment` under the same flat
names. Use it when the Worker needs a different key from the rest of the machine, or when
the deployment should be self-describing rather than depending on host state. Otherwise omit
them and let the machine variables do the work.

**The trade-off, stated plainly:** that registry key is readable by local administrators.
For most internal deployments that is acceptable — anyone with local admin on the host can
read the process memory anyway. Where it is not acceptable, the alternative is a
DPAPI-protected file encrypted to the service account:

```powershell
# As the service account, on the target machine:
$secure = Read-Host -AsSecureString
ConvertFrom-SecureString $secure | Set-Content C:\Services\OmsLoan\secrets\claude.key
```

DPAPI ties the ciphertext to that account on that machine, so the file is useless if copied
elsewhere. It also means the file has to be regenerated on each host and after an account
change — a real operational cost, which is why environment variables are the default.

Either way: **no API key or connection string ever belongs in a committed file.** The
placeholders in `appsettings.json` are empty strings and are treated as absent.

## Startup banner

On every start the Worker logs its environment, content root, whether it is running as a
service, the configuration sources in precedence order, and — for the connection string,
each API key and each Graph credential — whether it was found and **which source supplied
it**.

For the flat-named secrets it names the variable rather than the provider, so the line is
directly actionable:

```
  Resolved settings:
    - ConnectionStrings:OmsLoan : present (from EnvironmentVariablesConfigurationProvider)
    - Extraction:Claude:ApiKey  : present (from CLAUDE_API_KEY)
    - Extraction:OpenAi:ApiKey  : absent (set OPEN_API_KEY)
    - Extraction:Groq:ApiKey    : present (from GROQ_API_KEY)
    - Graph:TenantId            : present (from GRAPH_TENANT_ID)
    - Graph:ClientId            : present (from GRAPH_CLIENT_ID)
    - Graph:ClientSecret        : present (from GRAPH_CLIENT_SECRET)
```

Values are never logged. Only presence and origin.

Graph credentials are reported all-or-nothing: a tenant with no secret is not a partially
working credential, it is one somebody stopped halfway through configuring, and it would
otherwise fail at the first mailbox poll rather than at startup. A partial set is a warning.

That last column is what makes a misconfiguration visible immediately. A service that comes
up cleanly against the wrong database looks exactly like a correct one until you read which
source won; the usual culprit is a stale machine-wide environment variable outranking
`appsettings.Production.json`, or `DOTNET_ENVIRONMENT` never being set so Production was
never selected at all.

Read it with `.\Start-OmsLoanService.ps1`, or in Event Viewer under Application, source
`OmsLoanWorker`.

## Recovery

The installer configures the SCM to restart the service after a failure, backing off
**1 minute, then 2, then 5**, with the failure count resetting after a day of clean running.
The staged back-off matters because the common failure is the database or the network being
unavailable, and retrying every few seconds neither helps nor leaves a readable log.

Start type is **Automatic (Delayed Start)**: SQL Server and the network are frequently not
ready at the moment the machine reaches the desktop, and a failed first connection would
otherwise burn a restart attempt before anything could work.

Shutdown timeout is 20 seconds, enough to finish the notice in hand. The SCM logs a service
that overruns its stop as a crash, which is why `Stop-OmsLoanService.ps1` waits and reports
the elapsed time.

`OmsLoanApi` is configured identically and starts independently, so a reboot brings both
back with no ordering between them. Confirm with
`Get-Service OmsLoanWorker, OmsLoanApi | Format-Table Name, Status, StartType`.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| Error 1069, nothing in Application log | Account lacks *Log on as a service*; the process never started |
| Starts, then stops immediately | Read the banner — usually a missing or wrong connection string |
| `appsettings.json` seems ignored | Content root wrong. `AddWindowsService()` fixes this; without it the SCM gives the process `C:\Windows\System32` |
| Wrong database, no error | A machine-wide environment variable is outranking the file. The banner names the winning source |
| Nothing in the Event Log at all | Source not registered — re-run the installer, which creates it |
| Extractions never run | No provider API key configured; the banner warns about this at startup |
| Variable "is set" but reported absent | It is user-scope. A service only sees machine-scope variables — use `setx /M`, then restart the service |
| Mailbox ingestion fails on first poll | Graph credential partially configured; the banner warns when some of the three are missing |
