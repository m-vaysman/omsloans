# Running the Api as a Windows Service

`OmsLoan.Api` serves the notice review API and, in Production, the React review UI out of the
same process. It self-hosts on **Kestrel** and runs under the SCM as `OmsLoanApi`.

This is the sibling of [`windows-service.md`](windows-service.md), which covers the Worker.
Read that one first if you are deploying both; everything here is deliberately the same shape.

## Two services, one database

[ADR 0002](decisions/0002-windows-service-over-desktop.md) splits ingestion from review into
two processes. They share `OmsLoan.Domain` and the SQL Server database behind it, and neither
project references the other.

| | `OmsLoanWorker` | `OmsLoanApi` |
| --- | --- | --- |
| Does | Ingests notices, calls the LLM providers | Serves the review API and the React UI |
| Host | Worker Service | Kestrel, self-hosted |
| Event Log source | `OmsLoanWorker` | `OmsLoanApi` |
| Environment variable | `DOTNET_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` |
| Listens on | nothing | `ASPNETCORE_URLS` |
| Database access | reader + writer | reader + writer |
| Install script | `Install-OmsLoanService.ps1` | `Install-OmsLoanApiService.ps1` |

They are installed, started, stopped and recovered independently. Stopping review does not
stop ingestion; the queue keeps filling. Stopping ingestion does not stop review; reviewers
keep working through what is already there.

**No IIS.** Kestrel binds the port itself and the SCM owns the process lifetime — the same
recovery model as the Worker, rather than a second one built out of application pools and
`web.config`. **No Topshelf**: `Microsoft.Extensions.Hosting.WindowsServices` is in the box
and is what the Worker already uses.

## Install

```powershell
# The React build is run by the publish target and lands in wwwroot. Node is needed on the
# machine doing the publish, not on the server.
dotnet publish src/OmsLoan.Api -c Release -o C:\Services\OmsLoanApi

cd scripts
.\Install-OmsLoanApiService.ps1 `
    -PublishPath C:\Services\OmsLoanApi `
    -Environment Production `
    -Urls 'http://+:5080' `
    -ServiceAccount 'CONTOSO\svc_omsloan_api' `
    -ConnectionString 'Server=sql01;Database=OmsLoan;Integrated Security=true;Encrypt=true'
```

Run elevated. The script prompts for the account password rather than taking it as a
parameter, so it never reaches a command line, a script file, or PSReadLine history.

| Script | Purpose |
| --- | --- |
| `Install-OmsLoanApiService.ps1` | Register, configure recovery, create Event Log source, set environment |
| `Uninstall-OmsLoanApiService.ps1` | Stop and remove; keeps the Event Log source unless `-RemoveEventLogSource` |
| `Start-OmsLoanApiService.ps1` | Start, wait for Running, print the banner, then check it answers |
| `Stop-OmsLoanApiService.ps1` | Stop and wait, distinguishing a clean stop from a kill |

`Install` is re-runnable — an existing service is stopped and removed first.

### Publishing without a web build

`dotnet publish` runs `npm ci` (first time) and `npm run build` in `src/OmsLoan.Web` and copies
`dist/` into `wwwroot`. To skip it — because a pipeline builds the UI in its own step, or
because this deployment is API-only:

```powershell
dotnet publish src/OmsLoan.Api -c Release -o C:\Services\OmsLoanApi -p:BuildSpaOnPublish=false
```

Whatever is already in `src/OmsLoan.Web/dist` is still published. Nothing there means an
API-only deployment, which is supported: the installer warns, the startup banner says so, and
the static file middleware is not registered at all.

`dotnet build` and `dotnet test` never touch Node. The target hangs off the publish pipeline
only, so a clean clone still builds and tests without a JavaScript toolchain.

## Service account

The installer defaults to **LocalSystem**, which is fine for a first install and wrong for
production, for the same reasons set out in
[`windows-service.md`](windows-service.md#service-account). A **gMSA** is the better choice
where the domain supports one.

Use a *different* account from the Worker's if you want the two services to have different
rights — the Worker needs Modify on the watched folder and the Api does not need it at all.
One account for both is simpler and is a reasonable choice for a small internal deployment.

Four grants the installer cannot make for you:

**1. Log on as a service.** `secpol.msc` → Local Policies → User Rights Assignment → *Log on
as a service* → add the account. Without it the service fails to start with error 1069 and
nothing appears in the Application log, because the process never runs.

**2. A URL reservation.** This one is specific to the Api and is the most common way a
correct-looking install ends up unreachable. HTTP.sys will not let a non-administrator bind a
wildcard host name, so `http://+:5080` from a service account fails at startup with an
`HttpSysException` — the SCM reports the service as failed and the reason is in the
Application log rather than anywhere obvious.

```powershell
netsh http add urlacl url=http://+:5080/ user='CONTOSO\svc_omsloan_api'
```

The reservation survives an uninstall. `Uninstall-OmsLoanApiService.ps1 -ShowUrlReservations`
prints the ones the service was configured with before it deletes the environment block, so
you still know what to clean up:

```powershell
netsh http delete urlacl url=http://+:5080/
```

Running as LocalSystem needs no reservation, which is exactly why a first install appears to
work and the move to a real service account appears to break it.

**3. A firewall rule**, if reviewers are on other machines:

```powershell
New-NetFirewallRule -DisplayName 'OmsLoan Review API' -Direction Inbound `
    -Protocol TCP -LocalPort 5080 -Action Allow
```

**4. SQL Server login.**

```sql
CREATE LOGIN [CONTOSO\svc_omsloan_api] FROM WINDOWS;
USE OmsLoan;
CREATE USER [CONTOSO\svc_omsloan_api] FOR LOGIN [CONTOSO\svc_omsloan_api];
ALTER ROLE db_datareader ADD MEMBER [CONTOSO\svc_omsloan_api];
ALTER ROLE db_datawriter ADD MEMBER [CONTOSO\svc_omsloan_api];
```

Reader and writer only, and for the same reason as the Worker: the Api writes reviewer
corrections as new rows and never applies migrations or deletes. Schema changes are a
deployment step run under a separate account with `db_ddladmin` — see
[ADR 0003](decisions/0003-append-only-extractions-and-eav-fields.md).

## Configuration

Sources, **lowest precedence first** — a later source overrides an earlier one:

1. `appsettings.json` — committed, no secrets
2. `appsettings.{ASPNETCORE_ENVIRONMENT}.json` — `Production` committed; `Development` is gitignored
3. **user-secrets** — Development only, stored under `%APPDATA%\Microsoft\UserSecrets\`
4. **environment variables** — how Production supplies secrets
5. command line

A web host reads both `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT`, and the
`ASPNETCORE_` one wins. The installer sets that one; if neither is present the host defaults
to `Production`.

### Key names

A colon in a configuration key becomes a double underscore in an environment variable:

| Setting | Configuration key | Environment variable |
| --- | --- | --- |
| Database | `ConnectionStrings:OmsLoan` | `ConnectionStrings__OmsLoan` |
| Listening addresses | `Urls` | `ASPNETCORE_URLS` |
| Environment | — | `ASPNETCORE_ENVIRONMENT` |

`ConnectionStrings__OmsLoan` is deliberately the same variable name the Worker uses. One
database, one spelling, and a value that can be copied between the two install commands
without editing.

### Listening addresses

`ASPNETCORE_URLS` takes a semicolon-separated list and is what the installer sets:

```
http://+:5080
http://+:5080;https://+:5443
```

| Form | Binds | Needs a reservation |
| --- | --- | --- |
| `http://localhost:5080` | this machine only | no |
| `http://+:5080` | every interface, any host name | yes |
| `http://oms-review:5080` | that host name only | yes |

**If nothing is configured, Kestrel binds `http://localhost:5000`** — a service that starts
perfectly and that nobody else can reach. The startup banner calls this out explicitly, and
`Start-OmsLoanApiService.ps1` warns about it.

For anything beyond a URL — a certificate, an HTTP/2 override — use the `Kestrel:Endpoints`
section in `appsettings.Production.json` instead. Both forms are read, and the banner reports
whichever you used.

### HTTPS

An https URL needs a certificate. Two ways:

**Terminate TLS in front.** A reverse proxy or load balancer holds the certificate and this
service stays http-only. Simplest, and usually what an internal deployment already has.

**Terminate in Kestrel.** Put the certificate in the machine's `LocalMachine\My` store, grant
the service account read access to its private key (`certlm.msc` → the certificate → All Tasks
→ Manage Private Keys), and point Kestrel at it:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://+:5443",
        "Certificate": { "Subject": "oms-review.contoso.com", "Store": "My", "Location": "LocalMachine" }
      }
    }
  }
}
```

HTTPS redirection is registered **only when an https endpoint is configured**. On an
http-only host `UseHttpsRedirection` cannot work out a port to redirect to, so it would log a
warning on every request and then do nothing.

### Development

```powershell
dotnet user-secrets set "ConnectionStrings:OmsLoan" "Server=(localdb)\MSSQLLocalDB;Database=OmsLoan;Trusted_Connection=true" --project src/OmsLoan.Api
```

User-secrets live outside the repository entirely, so there is no file to accidentally commit.
Copy `appsettings.Development.json.example` to `appsettings.Development.json` for non-secret
local overrides — that filename is gitignored.

### Production

The installer writes the environment name, the URLs and the connection string to the service's
own environment block in the registry, at
`HKLM\SYSTEM\CurrentControlSet\Services\OmsLoanApi\Environment`. A service does not inherit
variables set with `setx`, so this per-service block is the mechanism; it is also why the
values are visible to this service and to nothing else on the machine.

**The trade-off, stated plainly:** that registry key is readable by local administrators. The
reasoning, and the DPAPI alternative for environments where that is not acceptable, are the
same as for the Worker — see
[`windows-service.md`](windows-service.md#production).

**No connection string ever belongs in a committed file.** The placeholder in
`appsettings.json` is an empty string and is treated as absent.

## Serving the React UI

### Production — one process

`dotnet publish` copies `src/OmsLoan.Web/dist` into the published `wwwroot`. The Api then
serves it:

- `UseDefaultFiles` + `UseStaticFiles` for the build output, before routing.
- `MapControllers` for the API.
- A fallback to `index.html` for everything else, so a deep link such as `/notices/42`
  survives a refresh instead of 404ing.

Precedence works out as: **controllers and Swagger win, then real files, then the SPA shell.**
Fallback endpoints sort behind every other endpoint, so no controller route can be shadowed.

Two details that are easy to leave out:

**A request under `/api` that matched no controller returns 404**, not the SPA shell. Without
that explicit fallback the catch-all would answer with `200 text/html`, and the caller would
fail deserialising an HTML document a long way from the cause.

**`index.html` is served `no-store`; everything else is cached for a year.** Vite gives every
asset a content hash in its filename, so `assets/` is immutable by construction. `index.html`
is the one file whose name never changes and whose contents name the current bundles — a
cached copy points at bundles a deploy has already deleted, and the app fails to boot with
nothing in any server log.

One process means one port to reserve, one service to recover, and no cross-origin
configuration that exists only because the UI is hosted somewhere else.

### Development — two processes

Development does not use any of that. Run both:

```powershell
dotnet run --project src/OmsLoan.Api      # http://localhost:5023, Swagger at /swagger
```

```powershell
cd src/OmsLoan.Web
npm run dev                               # http://localhost:5173
```

Vite proxies `/api` to `http://localhost:5023` (see `vite.config.ts`), so the browser talks to
one origin in Development exactly as it does in Production. Fetches in the React app are
relative paths in both, and there is no CORS configuration that exists only for Development.

`dotnet run` serves no static files at all — there is no `wwwroot` in a developer checkout,
because `dist/` is gitignored. The banner reports `React UI: not deployed`, which is the
expected state there.

## Startup banner

On every start the Api logs its environment, content root, **web root**, whether it is running
as a service, **the addresses it will listen on**, **whether a React build was found**, the
configuration sources in precedence order, and — for the connection string — whether it was
found and **which source supplied it**.

Values are never logged. Only presence and origin.

Read it with `.\Start-OmsLoanApiService.ps1`, or in Event Viewer under Application, source
`OmsLoanApi`.

The two Api-specific lines earn their place. A service on Kestrel's default
`http://localhost:5000` is indistinguishable in every other log line from one the whole office
can reach. And an API-only deployment and a broken web build produce the same symptom in a
browser — a blank page — but only one of them is a mistake.

## Recovery

Identical to the Worker, deliberately:

- **Automatic (Delayed Start)**, so SQL Server and the network have come up before the first
  connection is attempted.
- **Restart on failure**, backing off **1 minute, then 2, then 5**, with the count resetting
  after a day of clean running.
- **20-second shutdown timeout**, enough to drain in-flight review requests. The SCM logs a
  service that overruns its stop as a crash, which is why `Stop-OmsLoanApiService.ps1` waits
  and reports the elapsed time.

### After a reboot

Both services are Automatic (Delayed Start), so the SCM starts each of them a short while
after the machine reaches the desktop, in no particular order and with no dependency between
them. That independence is the point: whichever comes up first is useful on its own.
Ingestion resumes and review comes back, without anybody logging on.

To confirm:

```powershell
Get-Service OmsLoanWorker, OmsLoanApi | Format-Table Name, Status, StartType
```

Neither service is registered as depending on the other, and neither should be. A dependency
would mean a failure in ingestion also takes review offline, which is precisely the coupling
ADR 0002 removed.

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| Error 1069, nothing in Application log | Account lacks *Log on as a service*; the process never started |
| Starts, then stops immediately | Read the banner — a missing connection string, or a bind that failed |
| Service is Running but nothing answers | No URL reservation for the account, or the port is taken. `Start-OmsLoanApiService.ps1` makes a request and says so |
| `HttpSysException` / "Access is denied" at startup | `netsh http add urlacl` was never run for this account and URL |
| Reachable from the server, not from anywhere else | Bound to `localhost`, or the firewall rule is missing |
| `appsettings.json` seems ignored | Content root wrong — the SCM gives a service `C:\Windows\System32`. Program.cs sets it explicitly when running as a service |
| API works, browser shows a blank page | No `wwwroot\index.html`. The banner says `React UI: not deployed` |
| A deploy does not take effect in the browser | A cached `index.html` — check that it is being served `no-store` |
| Wrong database, no error | A machine-wide environment variable is outranking the file. The banner names the winning source |
| Nothing in the Event Log at all | Source not registered — re-run the installer, which creates it |
| Entries appear under the wrong source | Both services log to Application; filter on `OmsLoanApi` to exclude ingestion |
