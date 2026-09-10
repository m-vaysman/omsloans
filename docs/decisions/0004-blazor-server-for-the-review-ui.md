# 0004 — Blazor Server for the review UI

Status: Accepted

## Context

[ADR 0002](0002-windows-service-over-desktop.md) chose a web UI over a desktop application for review. It did not choose a web framework. A Vite + React + TypeScript scaffold arrived with the solution skeleton and nothing recorded why.

The review UI is a small operational tool: a PDF beside its extracted fields, a reviewer corrects values and approves. A handful of internal users on a LAN, in a codebase that is otherwise entirely C#. The priority is the least tech debt, not the richest client.

Options compared on [#75](https://github.com/m-vaysman/omsloans/issues/75).

## Decision

**Blazor Server**, in a new project, `src/OmsLoan.Review`, with every page interactive.

## Consequences

One language. The UI can bind to Domain types directly rather than to a TypeScript copy of them that drifts, and there is no Node toolchain to install or keep current for this app.

Blazor Server holds a live connection per user. That is its main weakness and it does not apply at this size; it would if review were ever opened to many users or run over a poor network.

The project has no package references — everything ships with the framework.

`src/OmsLoan.Web` (React) stays in the repository, by explicit decision, but is no longer the target. Two UI projects coexisting is accepted debt until one is retired on purpose.

Hosting is not settled here. The new app runs standalone; running it as a Windows Service the way `OmsLoan.Api` runs is a follow-up.
