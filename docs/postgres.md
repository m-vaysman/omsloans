# Running on Postgres

Optional. SQL Server stays the default. Installs that do not opt in are unchanged. See [#82](https://github.com/m-vaysman/omsloans/issues/82).

**Why Postgres:** portable image, no Express 10 GB ceiling, runs anywhere Docker runs. SQL Server Express is free too — this is portability, not price.

**What Docker does not solve:** secrets. LLM keys and Graph credentials still come from environment variables on the service. See [windows-service.md](windows-service.md).

## 1. Start the database

```bash
docker compose up -d
```

Postgres 17 on port 5432. Data lives in the `omsloan-postgres` volume and survives `docker compose down`.

**No password, this machine only.** The database holds only this app's fake notices, so it runs with trust auth and publishes its port on `127.0.0.1` alone. Never open 5432 in the firewall, and never widen the bind back to `5432:5432`: with no password, that gives anyone on the network a superuser login.

Postgres writes its login rules only when it creates the volume. A volume created while a password was still required keeps asking for one until it is wiped.

The volume is named explicitly, so it is `omsloan-postgres` whichever folder the repo is cloned into. To wipe it: `docker compose down -v`.

If something else on the host already uses port 5432, such as a native Postgres install, change the middle number of `127.0.0.1:5432:5432` in `docker-compose.yml` and use the same port in the connection strings below.

## 2. Create the schema

```bash
dotnet ef database update --project src/OmsLoan.Data.Postgres
```

With `OMSLOAN_POSTGRES_CONNECTION` unset, the design-time factory connects to `Host=localhost;Port=5432;Database=omsloan;Username=omsloan`, which is this container. Set the variable only to point at a different database.

To produce a script for a DBA instead:

```bash
dotnet ef migrations script --idempotent --project src/OmsLoan.Data.Postgres -o postgres.sql
```

## 3. Point the services at it

Two machine environment variables, read by both the Worker and the Api:

| Variable | Value |
| --- | --- |
| `Database__Provider` | `Postgres` |
| `ConnectionStrings__OmsLoan` | `Host=localhost;Port=5432;Database=omsloan;Username=omsloan` |

Unset `Database__Provider`, or set it to `SqlServer`, and the services use SQL Server as before. Any other value stops the process at startup with a message naming the setting. Under an installed Windows service that throw is a restart loop — `StartupValidation` does not catch it yet.

Package: `Npgsql.EntityFrameworkCore.PostgreSQL` **8.0.x**, matching EF Core 8. The 10.x line targets EF 10 and will not work here.

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

Same model. Four mappings change so the SQL is valid and matches SQL Server behaviour:

| | SQL Server | Postgres |
| --- | --- | --- |
| `Notice.Content` | `varbinary(max)` | `bytea` |
| `Extraction.RawJson` | `nvarchar(max)` | `text` |
| `EmailMessageId` index filter | `[EmailMessageId] IS NOT NULL` | `"EmailMessageId" IS NOT NULL` |
| `ExtractedField.DateValue` | `datetime2` | `date` |

Timestamps store as `timestamp with time zone`. Npgsql refuses a `DateTime` whose Kind is not UTC. The upload endpoint binds `sentAtUtc` from the caller, so every timestamp is stamped UTC on the way in without shifting the value — the same wall clock SQL Server stores today.

One behaviour difference: SQL Server's default collation is case-insensitive; Postgres compares strings case-sensitively.

## Services on Windows, database in Docker

The Worker and Api stay Windows Services. Only the database runs in a container.

**Startup order.** The `postgres` image is Linux, so on Windows it runs under Docker Desktop / WSL 2. Docker Desktop starts when a user signs in; the Worker starts at boot. After a reboot with nobody signed in, the Worker runs and the database is not there. For an unattended host, run Postgres where it starts at boot (a Linux host, or the native Windows installer as a service), or accept that someone signs in after a restart.
