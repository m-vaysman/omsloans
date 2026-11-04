# Setup — Local Configuration

This repository contains **no real credentials**. `LoanDbContext.OnConfiguring` reads
`OMSLOANS_CONNECTION` when the host has not already configured a provider.
**Never commit a connection string.**

## Option 1 — Environment variable (recommended)

Set `OMSLOANS_CONNECTION` on your machine, then start a new terminal so the process sees it:

```powershell
setx OMSLOANS_CONNECTION "Server=myserver;Database=Oms;User ID=me;Password=...;TrustServerCertificate=true;MultipleActiveResultSets=True;Max Pool Size=100;"
```

If the variable is missing, the trade-entry screen shows the database error when it loads counterparties. The source no longer contains a server name or password.

## Option 2 — .NET User Secrets

Secrets live outside the repo, under your Windows user profile
(`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`):

```bash
dotnet user-secrets init --project LoanDbModel
dotnet user-secrets set "ConnectionStrings:Oms" "Server=...;Database=Oms;..." --project LoanDbModel
```

## Option 3 — Gitignored local config file

Create `App.Local.config` or `appsettings.Local.json` next to the app. Both are
already listed in `.gitignore`, so git will not track them.

## Database

EF Core migrations live in `LoanDbModel/Migrations/`. The schema targets a
database named `Oms` with a `loans` schema. To create it:

```bash
dotnet ef database update --project LoanDbModel
```

## If you ever commit a credential by accident

Rotate it first — assume it is compromised the moment it is pushed. Removing it
in a later commit does **not** remove it from history; the history has to be
rewritten with `git filter-repo` and the credential changed at the source.
