# Deploy — OmsLoan Windows Services (Octopus-style)

Two phases on the cobbler1 self-hosted runner (`actions.runner.m-vaysman-omsloans.cobbler1`, `G:\actions-runner\`, `NT AUTHORITY\SYSTEM`):

1. **First deploy** — if `OmsLoanWorker` / `OmsLoanApi` is **missing**, Actions **headless-runs** `Install-OmsLoanService.ps1` / `Install-OmsLoanApiService.ps1` (no RDP, no `Get-Credential` for keys).
2. **Going forward** — if the service **exists**, bounce only: Stop → publish overwrite → Start. Installers are **not** re-run.

Prefer drive **G:** for service folders (`G:\Services\OmsLoan`, `G:\Services\OmsLoanApi`).

| Workflow | Runner | Trigger |
| --- | --- | --- |
| [CI](.github/workflows/ci.yml) | `windows-latest` | **Run workflow** only |
| [Deploy Worker](.github/workflows/deploy.yml) | `[self-hosted, Windows, cobbler1]` | **Run workflow** only |
| [Deploy Api](.github/workflows/deploy-api.yml) | `[self-hosted, Windows, cobbler1]` | **Run workflow** only |

No `pull_request` on cobbler1 workflows (public repo + SYSTEM on the target).

---

## Secrets policy

**LLM / Graph / DB secrets are already on the machine** (machine env and/or the service `Environment` REG_MULTI_SZ). The pipeline does **not** require putting those into GitHub Actions secrets.

- Installer/workflow reads host env (`ConnectionStrings__OmsLoan`, `Database__Provider`, `Ingestion__WatchedFolder`, `GRAPH_*`, LLM keys, …).
- On install, prior service env is **merged** so keys are not wiped.
- Preferred account: **gMSA** (`…$`) — no password in Actions.
- Optional Actions secrets only if you refuse gMSA: `OMSLOAN_WORKER_SERVICE_PASSWORD` / `OMSLOAN_API_SERVICE_PASSWORD` for the service logon account. Not for Graph/LLM/DB.

---

## Phase 1 — First deploy (service missing)

Order (Worker): tests → Postgres ensure → publish → **Install-OmsLoanService.ps1 -NonInteractive** → `Invoke-OmsLoanMigration.ps1` → Start → Running gate.

Order (Api): tests → publish → **Install-OmsLoanApiService.ps1 -NonInteractive** → Start → Running + HTTP gate.

### Account model

- **Preferred:** gMSA. Pass workflow input `service_account` (or machine env `OMSLOAN_WORKER_SERVICE_ACCOUNT` / `OMSLOAN_API_SERVICE_ACCOUNT`).
- **Fallback:** domain account + optional Actions password secret (above).
- **Ruled out for headless:** LocalSystem (`-NonInteractive` refuses it). SYSTEM deploys; apps run as gMSA/dedicated account.

### Host prep once (AD / GPO / script — not an interactive RDP install of the app)

1. gMSA (or domain account) + **Log on as a service** on COBBLER1.
2. Folders under G:; watched-folder ACLs for Worker.
3. DB login for the service account.
4. Machine (or existing) env with connection string, provider, watched folder, Graph/LLM as needed.
5. Docker Engine if using compose Postgres.

### Verify

- Job summary `mode=first`.
- Service **Running**; Api answers on `ASPNETCORE_URLS`.
- Service env **names** present (values never printed in logs).

---

## Phase 2 — Going forward (service present)

Bounce only — **Actions must not call the Install scripts**.

Worker: tests → Postgres ensure → Stop → publish `ApplyMigrations=true` → Start → Running.

Api: tests → Stop → publish `BuildSpaOnPublish=false` → Start → Running + HTTP.

Shared concurrency `omsloan-host-deploy`. On failure the workflow tries Start again so the box is not left stopped.

### Never on bounce

- `Install-OmsLoanService.ps1` / `Install-OmsLoanApiService.ps1`
- `Get-Credential`
- Re-registering as LocalSystem

### Verify

- Job summary `mode=bounce`.
- Service Running; Api HTTP green.

---

## Postgres and migrations

- Compose project `omsloan`, `postgres:17`, `127.0.0.1:5432`, volume `omsloan-postgres`.
- Worker first ship: migrate after Install. Bounce: `-p:ApplyMigrations=true`.
- Api never migrates.

## Host environment names

| Purpose | Name |
| --- | --- |
| Database provider | `Database__Provider` |
| Database | `ConnectionStrings__OmsLoan` |
| Watched folder | `Ingestion__WatchedFolder` |
| Worker / Api env | `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` |
| Api URLs | `ASPNETCORE_URLS` |
| Graph / LLM | `GRAPH_*`, `CLAUDE_API_KEY`, `OPEN_API_KEY`, `GROQ_API_KEY` |

## Node.js in Actions logs

`actions/checkout` is a JavaScript action — not `npm` / `OmsLoan.Web`. Deploy Api uses `BuildSpaOnPublish=false`.
