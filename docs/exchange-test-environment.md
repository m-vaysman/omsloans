# Test Exchange Online environment (for loan_notices development)

We have a live Microsoft 365 test tenant with Exchange Online for integration testing.
This is NOT an on-prem Exchange server — it is Exchange Online, and the only supported
API surface we intend to use is **Microsoft Graph** (`graph.microsoft.com`) with
**app-only (client credentials) auth**. There is no signed-in user; this is a daemon scenario.

## Environment details

| Item | Value |
|---|---|
| Tenant | test M365 tenant (see `GRAPH_TENANT_ID` env var) |
| Tenant ID | see `GRAPH_TENANT_ID` env var |
| App registration | loan_notices |
| Client ID | see `GRAPH_CLIENT_ID` env var |
| Auth | ClientSecretCredential (client secret, 90-day expiry — test only) |
| Granted Graph permissions | Mail.Read, Mail.Send — **Application** type, admin-consented |
| Test mailbox | see `GRAPH_TEST_MAILBOX` env var |
| License | Exchange Online Plan 1 (mail only — no Teams/SharePoint/OneDrive endpoints available) |

Credentials are NEVER committed. They come from environment variables / user secrets:

- `GRAPH_TENANT_ID`
- `GRAPH_CLIENT_ID`
- `GRAPH_CLIENT_SECRET`

> **Note:** `GRAPH_USER` and `GRAPH_TEST_MAILBOX` are the same value — the test mailbox.
> `GraphDaemonSmokeTest.linq` reads it as `GRAPH_USER`.

The Worker reads the same three variables. They map onto `Graph:TenantId`, `Graph:ClientId`
and `Graph:ClientSecret` in configuration, and the startup banner reports whether each was
found — see [windows-service.md](windows-service.md#key-names). Configuration and presence
reporting only at this point; the Worker does not call Graph yet.

The mailbox address is not part of that set. It is a setting rather than a secret, and which
mailbox to poll belongs with mailbox ingestion rather than with credentials.

A working smoke test exists in LINQPad (`GraphDaemonSmokeTest.linq`) proving
send + read work end-to-end with these credentials.

## Task: audit this project for Graph readiness

Go through the codebase and report (do not change code yet):

1. **What email/Exchange API is the project currently using?**
   Look for: `Microsoft.Graph` (Graph SDK), `Microsoft.Exchange.WebServices` (EWS Managed
   API), `System.Net.Mail` / `SmtpClient` / `MailKit` (SMTP), or raw HttpClient calls to
   `graph.microsoft.com` or `/EWS/Exchange.asmx`. List every package reference and call site.

2. **If EWS or SMTP is in use:** identify everything that would need to change to move to
   Graph app-only. Map each EWS/SMTP operation to its Graph equivalent
   (e.g. `FindItems` → `GET /users/{id}/messages`, `SendEmail`/`SmtpClient.Send` →
   `POST /users/{id}/sendMail`).

3. **If Graph is already in use:** verify it is compatible with app-only auth:
   - No `/me` calls (invalid in client-credentials context — must be `/users/{id}`)
   - Uses `Azure.Identity` `ClientSecretCredential`/`ClientCertificateCredential`, not
     interactive/device-code flows, for the daemon paths
   - Scope requested is `https://graph.microsoft.com/.default` (SDK default), not
     named delegated scopes
   - Only uses endpoints covered by Mail.Read / Mail.Send; flag anything needing
     additional application permissions so we can grant them in Entra

4. **Configuration:** how are mail-server settings currently configured
   (appsettings, env vars, hardcoded)? Propose where the three GRAPH_* values plug in,
   following the existing configuration pattern of the project.

5. **Resilience gaps for a Graph daemon:** note whether the project has (or lacks)
   handling for 429 throttling, paging (`@odata.nextLink` / PageIterator), and
   token/secret expiry.

Output: a short written report with file/line references, then wait for direction
before modifying anything.

## Known environment quirks (relevant to tests)

- Self-sent/internal mail can surface the sender as a legacy Exchange DN
  (`/O=EXCHANGELABS/...`) rather than an SMTP address — sender matching must handle both.
- Brand-new tenant: aggressive junk filtering on inbound external mail is possible;
  check Junk folder in tests that assert delivery.
- App permissions are currently tenant-wide (all mailboxes). Production deployments
  should scope via `New-ApplicationAccessPolicy`; the test tenant has one mailbox so
  it does not matter here.
