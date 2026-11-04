# Setup — Local Configuration

This repository contains **no real credentials**. The connection string in
`LoanDbModel/LoanDbContext.cs` is a placeholder:

```
Server=YOUR_SERVER;Database=YOUR_DB;User ID=YOUR_USER;Password=YOUR_PASSWORD;
```

Supply the real value locally using one of the options below. **Never commit it.**

## Option 1 — Environment variable (recommended)

Set `OMSLOANS_CONNECTION` on your machine:

```powershell
setx OMSLOANS_CONNECTION "Server=myserver;Database=Oms;User ID=me;Password=...;TrustServerCertificate=true;MultipleActiveResultSets=True;Max Pool Size=100;"
```

Then read it in `OnConfiguring`:

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder options)
{
    var cs = Environment.GetEnvironmentVariable("OMSLOANS_CONNECTION")
             ?? throw new InvalidOperationException(
                    "Set OMSLOANS_CONNECTION — see SETUP.md.");
    options.UseSqlServer(cs);
}
```

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
database named `Oms` with a `loans` schema. The chain is the April 5 initial
migration through `3rdMigration`. The older March 31 chain used a second class
named `InitialMigration`, which did not compile, and it targeted a `loan`
schema. That chain is gone.

`dotnet ef database update` refuses a database that still has schema `loan`
or history rows `20250331*` / `20250401*`. It does not create an empty
`loans` schema beside that data. The app runs the same check on the first
connection. Move or drop the old schema before updating.

```bash
dotnet ef database update --project LoanDbModel
```

## If you ever commit a credential by accident

Rotate it first — assume it is compromised the moment it is pushed. Removing it
in a later commit does **not** remove it from history; the history has to be
rewritten with `git filter-repo` and the credential changed at the source.
