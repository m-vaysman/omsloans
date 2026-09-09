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
| `failed\` | Modify |

```powershell
$account = 'CONTOSO\svc_omsloan'
foreach ($path in @('C:\OmsLoan\Notices', 'C:\OmsLoan\Notices\processed', 'C:\OmsLoan\Notices\failed')) {
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

### Required, and what happens when they are not set

The Worker **will not start** without these six:

| Setting | Variable |
| --- | --- |
| Database | `ConnectionStrings__OmsLoan` |
| Watched folder | `Ingestion__WatchedFolder` |
| Graph tenant | `GRAPH_TENANT_ID` |
| Graph app id | `GRAPH_CLIENT_ID` |
| Graph secret | `GRAPH_CLIENT_SECRET` |
| Shared mailbox | `GRAPH_USER` |

Graph is all-or-nothing: a tenant with no client secret is a half-finished credential, not a
partially working one, so a partial set is refused exactly as an empty one is. The mailbox
address counts too — a complete credential pointed at nothing has nowhere to look. A blank value
counts as absent — the committed placeholders are empty strings.

Without a database the Worker cannot record a notice; without the Graph credential it cannot
collect one. Starting anyway would give you a service the SCM reports as Running, healthy in
every monitor, silently ingesting nothing — found out when somebody asks why the review
queue is empty.

**It stops rather than restarting.** The refusal is a clean stop, not a crash, so the failure
actions in [Recovery](#recovery) do not fire. Retrying a missing environment variable at one,
two and five minutes fails identically three times and buries the reason. Set the variable
and start the service again.

The provider API keys are **not** required. Three providers sit behind one interface so that
any one will do, and a notice ingested but not yet extracted is a state reprocessing fixes.
A missing key is a warning. The watched folder is not a secret, so it may also be set in
`appsettings.json`; the environment variable overrides it.

#### The watched folder is created, and its permissions are proved

On every start the Worker creates the watched folder and its `processed\` and `failed\`
subfolders if they are missing, then checks it can **read and write** each one. An
existing folder is left exactly as it is — no ACL change, no content change.

The permission check is the part that earns its place. `Directory.CreateDirectory` is a no-op
on a folder that already exists, and returns happily for one the account cannot write a
single byte into. On a real host the drop folder is normally created by whoever set up the
share, long before the service account existed — so an existence-only check would pass on
every host where it mattered. Each folder is therefore enumerated (proving read) and a probe
file is written and then deleted (proving write *and* delete — ingestion moves files between
these folders, so it needs all three). The probe is removed, leaving nothing for ingestion to
mistake for a notice.

A failure names the folder and what specifically could not be done:

```
crit: OmsLoan worker is not starting: cannot write to 'D:\OmsLoan\Notices':
      Access to the path '...\omsloan-write-probe-a5f465ac.tmp' is denied.
```

"could not create", "cannot read" and "cannot write to" are kept distinct because they are
different fixes. As with a missing variable, the service stops and is not retried.

**This changes the order of the ACL work below.** The folders now come into existence on the
first start, so either start the service once and then apply the ACLs, or create the folders
by hand first — applying ACLs to paths that do not exist yet fails.

What it looks like:

```
crit: OmsLoan.Worker.Startup[0]
      OmsLoan worker is not starting: 1 required setting(s) missing.
          GRAPH_CLIENT_SECRET  (Graph secret)
        Set them as machine environment variables, on the service's own environment block,
        or in user-secrets for Development, then start the service again. ...
```

Every missing variable is listed at once, so configuring a host takes one pass rather than
one restart per problem.

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

**The database and the watched folder keep the .NET convention.** The flat names exist only
because those particular variables were already set on the machines for other tooling.
Nothing was already called anything here, so there is no pre-existing spelling to honour —
and the Api reads the same connection-string variable:

| Setting | Configuration key | Environment variable |
| --- | --- | --- |
| Database | `ConnectionStrings:OmsLoan` | `ConnectionStrings__OmsLoan` |
| Watched folder | `Ingestion:WatchedFolder` | `Ingestion__WatchedFolder` |

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

## Ingestion

The Worker scans the watched folder every `Ingestion:PollIntervalSeconds` (default 30) and,
for each file: reads it, records a `Notice`, and only then moves it.

### One rule: nothing moves until it is recorded

The order is **read → insert → commit → move**, and never any other way round.

That makes the database the commit point and the watched folder the queue. If the database is
unavailable, files simply accumulate where they were dropped and are picked up when it comes
back — no manual replay, and an outage shows up as a folder filling rather than as notices
that quietly never existed. It is also why the Worker does not refuse to start when the
database is unreachable: refusing would not protect anything that this does not already
protect.

The cost is that recording and moving are not atomic. If the process dies, or the move fails,
after the row is committed, the file is read again next poll and recorded a second time. That
is at-least-once, and it is the right way round: **a duplicate row is recoverable, a lost
notice is not.**

It is also why `Notices.Sha256` is indexed but **not unique**. With a unique index the retry
would throw on insert, the file would never move, and it would be retried for ever — a
permanent stuck loop over a notice that was in fact ingested successfully.

No deduplication happens during ingestion. Deciding that two arrivals are the same document is
review's work.

### What happens to each file

| Outcome | Where the file goes |
| --- | --- |
| Recorded | `processed\` |
| Recorded, but the move failed | stays — re-ingested next poll, producing a second row |
| Could not be recorded (database down) | stays, indefinitely, retried every poll |
| Could not be read | stays and is retried, up to `Ingestion:MaxReadAttempts` (default 10) |
| Still unreadable after that many attempts | `failed\` |
| Read fine but is not a PDF | `failed\` immediately — permanent, so no retries |

Files are opened with no sharing, so one still being copied fails to open rather than being
read half-written and stored truncated. That is the common read failure, and why the attempt
limit is generous: ten attempts at the default interval is five minutes, enough for a slow
copy of a large PDF.

**The attempt limit deliberately does not apply to database failures.** A file that read fine
but could not be recorded stays for ever if need be — moving those aside would turn an outage
into notices filed under `failed\`.

`SentAtUtc` is left null. The filesystem timestamp is when the file was dropped here, not when
the agent bank sent it, and inventing a value would be worse than admitting we do not know.

An archived file is never overwritten by a later one of the same name — agent banks reuse
filenames, and overwriting would destroy the evidence for a notice already recorded. The
second gets a timestamp suffix.

### Settings

| Key | Default | |
| --- | --- | --- |
| `Ingestion:WatchedFolder` | — | Required. The service will not start without it |
| `Ingestion:ArchiveFolder` | the watched folder | Where `processed\` and `failed\` live |
| `Ingestion:PollIntervalSeconds` | 30 | |
| `Ingestion:MaxReadAttempts` | 10 | Consecutive read failures before a file goes to `failed\` |

Polling rather than `FileSystemWatcher`: the watcher misses events when its buffer overflows
during a bulk drop, does not fire reliably on network shares — which is where these folders
usually live — and offers no way to retry a file that was locked when the event arrived. A
scan re-examines whatever is still present, so a missed notice is self-correcting.

## Mailbox ingestion

The production channel: agent banks email notices to a shared mailbox and the Worker pulls
the PDF attachments out of it via Microsoft Graph, app-only.

### The same rule as the folder, with the read flag standing in for the move

**A message is never marked read until its notices are committed.** Unread is what "not yet
ingested" means, so the mailbox is the queue exactly as the watched folder is. If the
database is unavailable the messages stay unread and are picked up when it returns; nothing
needs replaying by hand.

As there, the two steps are not atomic. A crash or a failed mark-read after the commit means
the message is read again next poll and recorded again — at-least-once, and the right way
round. It is why `Notices.EmailMessageId` is indexed but **not unique**: with a unique index
that retry would throw for ever and the message could never leave the mailbox.

A message whose attachments were only *partly* recorded is not marked read either. Marking it
would lose the rest with nothing left to say they existed.

### What it does with a message

- **One notice per PDF attachment.** A mail carrying three notices is three documents to
  review, not one with the other two hidden inside it.
- **Attachments are judged on their bytes**, not on `contentType` or the file name — the same
  reason the upload endpoint stopped trusting a declared type. Inline images and signature
  logos fail that check and are ignored.
- **A message with no PDF attachment is marked read**, so it is not re-examined on every poll
  for ever. `hasAttachments` is true for signature images too.
- **Sender and send time come from the envelope.** This is the only ingestion path where
  `SentAtUtc` is genuinely known; the folder and upload paths leave it null rather than invent
  it from an arrival time.

### The heartbeat

The requirement is to know *the moment* the mailbox becomes unreachable and *the moment* it
comes back. That is a statement about transitions, so the log reports transitions:

| Event | Level | |
| --- | --- | --- |
| First successful contact | Information | said once, so the log shows contact was made at all |
| Contact lost | **Warning** | once, naming the error |
| Still out | Warning | only every 15 minutes |
| Contact restored | Information | naming how long it was out |

Nothing is logged while it stays up, and nothing while it stays down except the 15-minute
reminder. A mailbox unreachable overnight at a one-minute poll would otherwise produce around
five hundred identical errors and bury the first one — which is the only entry anybody needs.
The reminder exists so an outage that started on Friday is not represented in Monday's log by
a single line three days back.

The poll itself is the probe. There is no separate ping: a fetch that returns — even empty —
proves the tenant, the credential and the mailbox all work, and a fetch that throws is the
moment that stopped being true.

Folder and mailbox ingestion run on **separate loops**. The folder is a local scan; the
mailbox is a network round trip that can hang or be throttled. Sharing a timer would let an
unreachable mailbox stall folder ingestion, which has nothing to do with it.

### Settings

| Key | Variable | Default | |
| --- | --- | --- | --- |
| `Graph:TenantId` | `GRAPH_TENANT_ID` | — | required |
| `Graph:ClientId` | `GRAPH_CLIENT_ID` | — | required |
| `Graph:ClientSecret` | `GRAPH_CLIENT_SECRET` | — | required |
| `Graph:Mailbox` | `GRAPH_USER` | — | **required**; the shared mailbox address |
| `Graph:PollIntervalSeconds` | | 60 | |
| `Graph:MessagesPerPoll` | | 25 | unread is the queue, so the rest waits |

There is no switch to run the Worker without mailbox ingestion. The four Graph settings are
required, so a host that cannot poll a mailbox is one the Worker refuses to start on — a flag
to disable it would only contradict that.

**Watch the scope on `GRAPH_USER`.** It is commonly set at *user* scope on a development
machine, and a Windows Service never sees a user-scope variable — use `setx /M` on any host
running the service, or the Worker will report it missing and refuse to start.

Auth is client credentials, not delegated: the Worker runs unattended and a delegated token
tied to somebody's account stops working the moment their password rotates. `Mail.Read`
should be scoped to this one mailbox with an application access policy, since the grant is
otherwise tenant-wide. See [exchange-test-environment.md](exchange-test-environment.md).

Graph throttling is handled by the SDK's own retry handler, which honours `Retry-After` on
`429` and `503`. Nothing here second-guesses it: a hand-rolled backoff on top would multiply
the wait and hide the header Graph actually sent.

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

**Restarts are for faults, not for misconfiguration.** The two are separated deliberately:

| Situation | Process ends | SCM restarts it? |
| --- | --- | --- |
| Database unreachable, network down, unhandled exception while running | unexpectedly | **yes** — 1m, 2m, 5m |
| A required environment variable is missing | cleanly, exit code 0 | **no** — stays stopped |

A missing variable retried three times fails three times and pushes the one useful log entry
further up the Application log. So the Worker reports the problem at Critical and stops
normally, which the SCM reads as an ordinary stop rather than an error termination and
therefore leaves alone. Set the variable, then start the service again.

From a console the same refusal exits with code **78** instead, because there is no SCM to
mislead and a developer or CI step wants a failed exit status.

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
| Service stops at once and never retries | A required variable is missing. That is by design; the Critical entry names which one |
| `appsettings.json` seems ignored | Content root wrong. `AddWindowsService()` fixes this; without it the SCM gives the process `C:\Windows\System32` |
| Wrong database, no error | A machine-wide environment variable is outranking the file. The banner names the winning source |
| Nothing in the Event Log at all | Source not registered — re-run the installer, which creates it |
| Extractions never run | No provider API key configured; the banner warns about this at startup |
| Variable "is set" but reported absent | It is user-scope. A service only sees machine-scope variables — use `setx /M`, then restart the service |
| Mailbox ingestion fails on first poll | Graph credential partially configured; the banner warns when some of the three are missing |
