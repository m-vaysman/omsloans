# 0003 — Append-only extractions, corrections stored alongside, and EAV extracted fields

Status: Accepted

## Context

LLM extraction is probabilistic. Some notices will be read wrong, and the only reliable check is a human comparing the extracted values against the source document before anything is approved. That review step is going to happen thousands of times, and each occurrence is a labeled example: here is what the model said, here is what was actually true.

The obvious schema throws that away. If a reviewer's correction updates the extracted value in place, the label is destroyed at the moment it is created, and the system can never answer whether a model, a prompt version, or a particular field is getting better or worse.

There is a second pressure. New notice types are expected — the initial set is rate reset, interest payment, principal payment, fee, and rollover, and that list will grow. If each type's fields are columns, every new type is a schema migration and a deployment before a single notice can be processed.

## Decision

Three related rules.

**Extractions are append-only.** An `Extraction` row is never updated. Reprocessing a notice through a different model or a newer prompt inserts a new row and flips `IsCurrent` on the previous one within the same transaction. The full LLM response is persisted verbatim to `RawJson` before any parsing is attempted, along with the model name and prompt version that produced it.

**Corrections are stored alongside, never over.** A reviewer's edit writes `CorrectedValue`, `CorrectedBy`, and `CorrectedAtUtc` on `ExtractedField`. `RawValue` — what the model actually said — is immutable.

**Extracted fields are EAV.** `ExtractedField` holds `FieldName` and `RawValue` as strings, with typed `NumericValue decimal(18,6)` and `DateValue` projections alongside. A new notice type needs a new prompt and schema, not a migration.

## Consequences

The accuracy report is a query rather than a project. Correction rates by model, by field name, by notice type, and by prompt version all fall out of data the review workflow produces as a byproduct, including whether the model's stated confidence actually predicts correctness. Prompt and model changes become measurable against the same notices instead of being judged by impression.

Provenance survives. When someone asks in eighteen months why a payment posted at a particular amount, the answer is the original PDF bytes, the exact model response, the prompt version, the reviewer's name, and the timestamp — not a value with no history.

The costs are storage and query complexity. Raw JSON for every attempt, retained indefinitely, plus a new row per reprocessing run, means the tables grow monotonically and a retention policy will eventually be needed. Reading current values requires filtering on `IsCurrent`, and getting that wrong silently returns superseded data — so exactly one current extraction per notice is enforced transactionally rather than by convention. EAV gives up the type safety and check constraints that real columns would provide: field names are only as consistent as the prompts that emit them, which is why the schemas in the prompt issue pin the field-name vocabulary per notice type. Reporting queries pivot rather than select, and the typed projection columns exist so that numeric and date comparisons do not degrade into string comparison.

These are accepted deliberately. The dataset this produces is the long-term asset; the schema convenience it costs is recoverable, and the discarded labels would not have been.
