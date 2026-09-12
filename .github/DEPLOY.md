# Deploy — OmsLoan Windows Services (Octopus-style)

Two phases: **first deploy** registers the Windows Service on COBBLER1 without RDP; **going forward** is bounce-only (Stop → publish overwrite → Start). The runner is `actions.runner.m-vaysman-omsloans.cobbler1` under `G:\actions-runner\`, logon `NT AUTHORITY\SYSTEM`. Prefer drive **G:** for service folders.

| Workflow | Runner | Trigger |
| --- | --- | --- |
| [CI](.github/workflows/ci.yml) | `windows-latest` | **Run workflow** only |
| [Deploy Worker](.github/workflows/deploy.yml) | `[self-hosted, Windows, cobbler1]` | **Run workflow** only |
| [Deploy Api](.github/workflows/deploy-api.yml) | `[self-hosted, Windows, cobbler1]` | **Run workflow** only |

No `pull_request` on cobbler1 workflows (public repo + SYSTEM on the target).

---

## Phase 1 — First deploy (service missing)

The workflow detects `Get-Service` missing, then: tests → Postgres ensure (Worker) → publish into the service folder → **headless Install** → migrate (Worker) → Start → gate Running (Api also HTTP).

### Account model

- **Preferred:** gMSA (`CONTOSO\gmsa_omsloan$`). No password. Installer treats accounts ending in `$` as empty-password credentials.
- **Fallback:** dedicated domain account. Put the password in repo Actions secret `OMSLOAN_WORKER_SERVICE_PASSWORD` / `OMSLOAN_API_SERVICE_PASSWORD` (read only on the first-ship step). Do not commit it.
- **Ruled out:** LocalSystem for headless first ship (`-NonInteractive` refuses it). SYSTEM runs the *deploy* only; the app services use the dedicated/gMSA account.

### What you configure once (outside git)

Do these with AD / GPO / Intune / a one-shot admin script — not necessarily an interactive RDP session to the app UI:

1. Create gMSA (or domain service account) for Worker and Api.
2. Install gMSA on COBBLER1; grant **Log on as a service**.
3. Create `G:\Services\OmsLoan` and `G:\Services\OmsLoanApi` (or accept workflow defaults).
4. Watched folder + `processed\` / `failed\` with Modify for the Worker account.
5. SQL/Postgres login for the service account; set machine or process env the installer can see:
   - `ConnectionStrings__OmsLoan` (or Actions secret `OMSLOAN_CONNECTION` for first ship only)
   - `Database__Provider` (`Postgres` or `SqlServer` / blank)
   - `Ingestion__WatchedFolder` (Worker)
   - Graph / LLM names as needed (`GRAPH_*`, `CLAUDE_API_KEY`, …)
6. Workflow input `service_account` (or machine env `OMSLOAN_WORKER_SERVICE_ACCOUNT` / `OMSLOAN_API_SERVICE_ACCOUNT`).
7. Docker Engine reachable if using compose Postgres (`Install-OmsLoanPostgres.ps1`).

### Installer behavior (headless)

`Install-OmsLoanService.ps1` / `Install-OmsLoanApiService.ps1` with `-NonInteractive`:

- No `Get-Credential` prompt.
- gMSA or password from `OMSLOAN_SERVICE_ACCOUNT_PASSWORD` (workflow maps the Actions secret into that name for the install step only).
- Captures prior service env before delete and **merges** it with new values so a re-install does not wipe Graph/LLM keys.
- Pulls connection / watched folder / provider from process env when parameters are empty.

### Verify first ship

- Job summary mode = `first`.
- Service **Running**; Api answers on `ASPNETCORE_URLS` port.
- Event Log source exists; service env names present (values never printed).

---

## Phase 2 — Going forward (service present)

Bounce only. The workflow **must not** re-run Install.

Order (Worker): tests → Postgres ensure → Stop → publish with `ApplyMigrations=true` → Start → Running gate.

Order (Api): tests → Stop → publish with `BuildSpaOnPublish=false` → Start → Running + HTTP gate.

Shared concurrency `omsloan-host-deploy`.

### Never re-run on bounce

- `Install-OmsLoanService.ps1` / `Install-OmsLoanApiService.ps1` (would delete/re-register and historically rewrote env).
- Interactive `Get-Credential`.
- Anything that registers the service as LocalSystem.

### Verify bounce

- Job summary mode = `bounce`.
- Service **Running** after Start; Api HTTP probe green.
- On failure the workflow attempts Start again so the box is not left stopped.

---

## Postgres and migrations

- Compose project `omsloan`, image `postgres:17`, `127.0.0.1:5432`, volume `omsloan-postgres`.
- Worker first ship: `Invoke-OmsLoanMigration.ps1` after Install. Bounce: `-p:ApplyMigrations=true` on publish.
- Api never migrates.

## Optional Actions secret names (values stay in Settings)

| Name | When |
| --- | --- |
| `OMSLOAN_WORKER_SERVICE_PASSWORD` | First ship Worker if account is not gMSA |
| `OMSLOAN_API_SERVICE_PASSWORD` | First ship Api if account is not gMSA |
| `OMSLOAN_CONNECTION` | First ship if connection string is not already on the host |

Prefer host/machine env + gMSA so these stay empty.

## Host environment names (app config)

| Purpose | Name |
| --- | --- |
| Database provider | `Database__Provider` |
| Database | `ConnectionStrings__OmsLoan` |
| Watched folder | `Ingestion__WatchedFolder` |
| Worker environment | `DOTNET_ENVIRONMENT` |
| Api environment | `ASPNETCORE_ENVIRONMENT` |
| Api URLs | `ASPNETCORE_URLS` |
| Graph / LLM | `GRAPH_*`, `CLAUDE_API_KEY`, `OPEN_API_KEY`, `GROQ_API_KEY` |

## Node.js in Actions logs

`actions/checkout` is a JavaScript action — not `npm` / `OmsLoan.Web`. Deploy Api uses `BuildSpaOnPublish=false`.
