# 0002 — Windows Service and web UI over a desktop application

Status: Accepted

## Context

This repository began as a WPF desktop OMS. For notice extraction, that shape does not fit the work.

Ingestion is continuous and unattended. Agent banks send notices on their schedule, not ours: overnight, over weekends, in bursts around rate reset dates. The system needs to be watching a folder and a shared mailbox at all times, calling LLM APIs as documents arrive. A desktop application only runs while someone has it open and is logged in, which makes ingestion depend on human presence and turns a closed laptop into a silent outage.

Review is a different activity with different requirements. It is interactive, occasional, done by a small operations team, and needs a PDF and an editable form side by side. It does not need to be co-located with ingestion, and it benefits from being reachable without an install.

The two halves also scale differently. Ingestion is one process that must not run twice concurrently against the same folder. Review is several people at once.

## Decision

Split them. A .NET 8 Worker Service running as a Windows Service owns ingestion and the LLM extraction calls. An ASP.NET Core API plus a React app owns human review. Both talk to the same SQL Server database through the shared `OmsLoan.Domain` project; neither references the other.

The Windows Service host means ingestion starts on boot, runs under a service account with no interactive session, and restarts automatically on failure. The web UI means reviewers get the current version by refreshing rather than by an install, and Windows Authentication supplies the reviewer identity that corrections are attributed to — with no user store to build or operate.

## Consequences

Notices are ingested and extracted whether or not anyone is at a desk, so by the time a reviewer opens the queue the work is already waiting for them rather than starting when they arrive. Deployment of the review UI is decoupled from deployment of the extraction pipeline; either can ship without the other.

The costs are the ones that come with any headless process. There is no window to look at, so operational visibility has to be built deliberately — structured logs, a health endpoint, and alerting on failed extractions, provider errors, and notices that were ingested but never extracted. Diagnosing a service that will not start is harder than diagnosing an application that will not launch. Installation and configuration are now scripted concerns involving service accounts, folder permissions, and a SQL login, rather than a user double-clicking an executable. There are two deployable units to keep in step, plus a React build in the pipeline.

The existing WPF application is not deleted or migrated by this decision. It remains the trading and operations front end; notice extraction is a separate system that happens to share a domain.
