# Deploy — OmsLoan.Worker

GitHub Actions **does not** touch the Windows host. It builds a Release publish folder and uploads it as an artifact. You download it and install with the scripts already in this repo.

## Triggers (by design)

| Workflow | When it runs |
| --- | --- |
| [CI](.github/workflows/ci.yml) | Pull requests that touch `src/` / `tests/` / `OmsLoan.sln`, or **Run workflow** |
| [Deploy Worker](.github/workflows/deploy.yml) | **Run workflow** only — never on push to `main` |

There is no rebuild-on-`main` polling.

## Ship a Worker build

1. Actions → **Deploy Worker** → **Run workflow** (pick the branch/tag in the UI).
2. Wait for the run; download the `OmsLoan.Worker-<run>` artifact zip.
3. On the host, expand it to a publish folder (example: `C:\Services\OmsLoan`).
4. Cutover with the existing installer (stops/removes the old service first, then registers the new path):

```powershell
cd <repo-or-scripts-copy>\scripts
.\Install-OmsLoanService.ps1 `
    -PublishPath C:\Services\OmsLoan `
    -Environment Production `
    -ServiceAccount 'CONTOSO\svc_omsloan' `
    -ConnectionString '***'
```

Lighter bounce after an in-place overwrite of the same folder: `Stop-OmsLoanService.ps1` → copy files → `Start-OmsLoanService.ps1`.

Full host setup: [`docs/windows-service.md`](../docs/windows-service.md).

## Actions secrets

**None required for this pipeline.** The workflow only restores, tests, publishes, and uploads an artifact. Do not put LLM or Graph keys in workflow YAML or Actions secrets for this path.

## Host configuration (names only — values stay on the machine)

Set these on the Windows host (machine-scope or the service environment block). Actions never receives them.

| Purpose | Name |
| --- | --- |
| Database | `ConnectionStrings__OmsLoan` |
| Watched folder | `Ingestion__WatchedFolder` |
| Graph tenant | `GRAPH_TENANT_ID` |
| Graph app id | `GRAPH_CLIENT_ID` |
| Graph secret | `GRAPH_CLIENT_SECRET` |
| Shared mailbox | `GRAPH_USER` |
| Claude (optional) | `CLAUDE_API_KEY` |
| OpenAI (optional) | `OPEN_API_KEY` |
| Groq (optional) | `GROQ_API_KEY` |

## Not in this PR

- `OmsLoan.Api` publish (separate artifact later)
- GitHub Environments / protection rules
- WinRM/SSH remote install
- GitHub Pages
- Creating or rotating any secret values
