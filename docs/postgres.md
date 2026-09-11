# Running on Postgres

Optional. SQL Server stays the default and nothing changes for an install that doesn't opt in. See [#82](https://github.com/m-vaysman/omsloans/issues/82).

**Why Postgres:** free, portable, standard. SQL Server Express is free too, so this is about portability: a small image, no licence question once Express's 10 GB cap is outgrown, and runnable anywhere Docker runs.

**What Docker doesn't solve:** secrets. LLM keys, Graph credentials and the database password still come from environment variables on the service, as described in [windows-service.md](windows-service.md).

## 1. Start the database

```bash
cp .env.example .env
```

Set `POSTGRES_PASSWORD` in `.env`. It's gitignored; only `.env.example` is committed. Then:

```bash
docker compose up -d
```

Postgres 17 on port 5432, data in the `omsloan-postgres` volume, which survives `docker compose down`. Compose refuses to start if `POSTGRES_PASSWORD` is empty.

## 2. Create the schema

```powershell
$env:OMSLOAN_POSTGRES_CONNECTION = "Host=localhost;Port=5432;Database=omsloan;Username=omsloan;Password=<POSTGRES_PASSWORD>"
dotnet ef database update --project src/OmsLoan.Data.Postgres
```

To produce a script for a DBA instead:

```bash
dotnet ef migrations script --idempotent --project src/OmsLoan.Data.Postgres -o postgres.sql
```

## 3. Point the services at it

Two machine environment variables, read by both the Worker and the Api:

| Variable | Value |
| --- | --- |
| `Database__Provider` | `Postgres` |
| `ConnectionStrings__OmsLoan` | `Host=localhost;Port=5432;Database=omsloan;Username=omsloan;Password=<POSTGRES_PASSWORD>` |

Unset `Database__Provider`, or set it to `SqlServer`, and the services use SQL Server as before. Any other value stops the service at startup with a message naming the setting.

Package: `Npgsql.EntityFrameworkCore.PostgreSQL` **8.0.x**, matching EF Core 8. The 10.x line targets EF 10 and won't work here.

## Migrations: one set per provider

| Provider | Migrations live in |
| --- | --- |
| SQL Server | `src/OmsLoan.Domain/Migrations` |
| Postgres | `src/OmsLoan.Data.Postgres/Migrations` |

Every model change needs both:

```bash
dotnet ef migrations add <Name> --project src/OmsLoan.Domain
dotnet ef migrations add <Name> --project src/OmsLoan.Data.Postgres
```

A test in `OmsLoan.Data.Postgres.Tests` fails when the Postgres set falls behind the model.

## What differs on Postgres

The model is the same; four things are mapped differently so the SQL is valid and behaves as it does on SQL Server:

| | SQL Server | Postgres |
| --- | --- | --- |
| `Notice.Content` | `varbinary(max)` | `bytea` |
| `Extraction.RawJson` | `nvarchar(max)` | `text` |
| `EmailMessageId` index filter | `[EmailMessageId] IS NOT NULL` | `"EmailMessageId" IS NOT NULL` |
| `ExtractedField.DateValue` | `datetime2` | `date` |

Timestamps are stored as `timestamp with time zone`. Npgsql refuses a `DateTime` whose Kind isn't UTC, and the upload endpoint takes `sentAtUtc` from the caller, so every timestamp is stamped UTC on the way in without shifting the value. That is what SQL Server stores today.

One behaviour difference to know: SQL Server's default collation is case-insensitive and Postgres compares strings case-sensitively.

## Services on Windows, database in Docker

The split is deliberate: the Worker and Api stay Windows Services, and only the database runs in a container.

**Startup order.** The `postgres` image is Linux, so on Windows it runs under Docker Desktop / WSL 2. Docker Desktop starts when a user signs in; the Worker starts at boot. After a reboot with nobody signed in, the Worker runs and the database isn't there. For an unattended host, run Postgres where it starts at boot (a Linux host, or the native Windows installer, which runs as a service), or accept that someone signs in after a restart.
