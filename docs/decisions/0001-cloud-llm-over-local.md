# 0001 — Cloud LLM APIs over locally hosted models

Status: Accepted

## Context

omsloan extracts economic data — rates, spreads, payment amounts, effective dates — from PDF notices sent by agent banks. These documents are dense, tabular, and inconsistently formatted across counterparties, and the extracted values feed loan operations. A misread rate or a transposed payment amount is a real operational error, so extraction quality is the dominant concern.

The alternative was hosting an open-weights model on internal hardware. That would keep notice contents inside the network and remove per-token cost, but it requires GPU capacity, model serving infrastructure, and ongoing operational ownership, and the accuracy available from self-hosted document models is meaningfully below the frontier hosted models — particularly on native PDF understanding, where layout and table structure carry the meaning.

Volume is low. This is tens to hundreds of notices a day, not millions, so per-token cost is not the constraint it would be at scale.

## Decision

Use cloud LLM APIs — Claude, OpenAI, and Groq — behind an `INoticeExtractor` interface, with the provider selectable per extraction run.

The interface is the important half of the decision. No calling code knows which vendor produced a given extraction; the provider is a configuration value, and every extraction records the model name and prompt version that produced it. Claude is the expected primary provider because it accepts PDFs natively, so document layout reaches the model intact rather than being flattened by a text-extraction step first. Groq is the cheap and fast fallback for bulk reprocessing and experimentation.

## Consequences

Extraction quality is as good as the current frontier, and it improves when the providers improve rather than when we find time to retrain something. Provider comparison becomes an actual measurement: because the same notice can be run through multiple models and every raw output is retained, the accuracy report can answer which model to trust for which notice type.

The costs are real and accepted. Notice contents leave the network, which requires that data handling terms with each provider be reviewed and that no notice content ever appear in logs. Extraction depends on third-party availability, so provider outages and rate limits are operational failure modes that need retry, backoff, and alerting rather than being treated as impossible. There is a per-token cost that scales with volume and with reprocessing. Models are deprecated and replaced on the vendors' schedule, not ours, so model ids are pinned in configuration and a model change is treated as an event to measure — which is exactly what storing model name and prompt version per extraction is for.

Revisit this if notice volume grows by an order of magnitude, if data residency requirements harden, or if self-hosted document models close the accuracy gap. The `INoticeExtractor` seam means a local provider would be a new implementation, not a rewrite.
