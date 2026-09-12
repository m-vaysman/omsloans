# Deploy — OmsLoan Windows Services (Octopus-style)

Bootstrap once on the host. Every later ship is a bounce from the self-hosted runner — Stop → publish over the service folder → Start. Fail unless Running.

## What runs where

| Workflow | Runner | Trigger |
| --- | --- | --- |
| [CI](.github/workflows/ci.yml) | `windows-latest` (GitHub-hosted) | **Run workflow** only |
| [Deploy Worker](.github/workflows/deploy.yml) | `[self-hosted, Windows, cobbler1]` | **Run workflow** only |
| [Deploy Api](.github/workflows/deploy-api.yml) | `[self-hosted, Windows, cobbler1]` | **Run workflow** only |

Runner service on COBBLER1: `actions.runner.m-vaysman-omsloans.cobbler1` under `G:\actions-runner\`, logon `NT AUTHORITY\SYSTEM`. Prefer drive **G:** for service folders and large files — not C:.

No `pull_request` trigger on the cobbler1 workflows (public repo + SYSTEM on the target box).

## First ship (by hand, once)

An admin on the host:

1. Install Docker Desktop (WSL2) or native Postgres, or point at a separate DB host.
2. For Docker Postgres: engine reachable, then from a repo checkout run `scripts/Install-OmsLoanPostgres.ps1` after setting `Database__Provider=Postgres` and `ConnectionStrings__OmsLoan` (service env or machine scope).
3. Publish folders under G: (example `G:\Services\OmsLoan`, `G:\Services\OmsLoanApi`).
4. Run `scripts/Install-OmsLoanService.ps1` with `-ServiceAccount` and connection string / secrets.
5. Run `scripts/Install-OmsLoanApiService.ps1` with `-ServiceAccount`, `-Urls`, and the same database settings.

The runner **never** runs those installers. They delete/re-register the service and can rewrite the environment block (which would wipe `Database__Provider`, Graph, and LLM keys if re-run under SYSTEM without `-ServiceAccount`).

## Later deploys (runner bounce)

`SYSTEM` only deploys. App services keep their own account and registry environment block.

**Worker:** Actions → Deploy Worker → Run workflow (default service folder `G:\Services\OmsLoan`).

Order: tests → refuse if service missing → Postgres ensure → Stop → publish with `ApplyMigrations=true` → Start → gate Running.

**Api:** Actions → Deploy Api → Run workflow (default `G:\Services\OmsLoanApi`).

Order: tests → refuse if service missing → Stop → publish with `BuildSpaOnPublish=false` → Start → gate Running and HTTP on the port from `ASPNETCORE_URLS`.

Shared concurrency group `omsloan-host-deploy` so the two deploys do not race.

## Postgres

- Compose project name is always `omsloan` (`docker compose -p omsloan`).
- Image `postgres:17`, loopback `127.0.0.1:5432`, volume `omsloan-postgres`, trust auth.
- Connection string name: `ConnectionStrings__OmsLoan` shaped like `Host=localhost;Port=5432;Database=omsloan;Username=omsloan`.
- `Install-OmsLoanPostgres.ps1` leaves a healthy compose container or an occupied port alone. It never `down`s or removes volumes.
- If `docker info` fails, the script fails; that is not the same as a stopped container.
- Docker Desktop often runs only while a user is signed in; Worker/Api start at boot. For unattended hosts prefer Engine access that starts at boot, native Postgres as a Windows service, or a separate DB host.

## Migrations

- Applied on Worker publish when `-p:ApplyMigrations=true` (Deploy Worker passes this).
- `scripts/Invoke-OmsLoanMigration.ps1` reads `Database__Provider` and `ConnectionStrings__OmsLoan` from the service registry block, then machine scope, then process env.
- Postgres → `src/OmsLoan.Data.Postgres`; blank or SqlServer → `src/OmsLoan.Domain`. Always passes `--connection`.
- Hand path for migrate without compose changes: `scripts/Install-OmsLoanPostgres.ps1 -MigrateOnly`.

## Host environment (names only)

| Purpose | Name |
| --- | --- |
| Database provider | `Database__Provider` (`SqlServer`, `Postgres`, or blank) |
| Database | `ConnectionStrings__OmsLoan` |
| Watched folder | `Ingestion__WatchedFolder` |
| Worker environment | `DOTNET_ENVIRONMENT` |
| Api environment | `ASPNETCORE_ENVIRONMENT` |
| Api URLs | `ASPNETCORE_URLS` |
| Graph tenant | `GRAPH_TENANT_ID` |
| Graph app id | `GRAPH_CLIENT_ID` |
| Graph secret | `GRAPH_CLIENT_SECRET` |
| Shared mailbox | `GRAPH_USER` |
| Claude | `CLAUDE_API_KEY` |
| OpenAI | `OPEN_API_KEY` |
| Groq | `GROQ_API_KEY` |

No Actions secrets for this pipeline. Values stay on the host / service env.

## Node.js in Actions logs

`actions/checkout` is a JavaScript action. That is not `npm` / `OmsLoan.Web`. Deploy Api passes `BuildSpaOnPublish=false`.
