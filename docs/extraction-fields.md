# Extraction prompt and field names

The prompt and its JSON schema live in
[`src/OmsLoan.Domain/Extractors/Prompts`](../src/OmsLoan.Domain/Extractors/Prompts) and are
embedded in the assembly. There is one of each, versioned as a pair:

| File | |
| --- | --- |
| `extraction.v1.md` | The instructions sent with the PDF |
| `extraction.v1.schema.json` | The response shape the model is constrained to |

`PromptCatalog.Extraction` loads the current version; `PromptCatalog.Load("extraction.v1")`
loads a named one. Every `Extraction` row records the version that produced it, and the
loader throws on a version it cannot find rather than falling back to whatever is on disk
today — a row nobody can reproduce is a row nobody can check.

## One prompt, many events

There is no prompt per notice type, and this is deliberate. A single notice routinely states
more than one economic event — a principal paydown and the next period's rate reset are
commonly in the same document — and a per-type prompt has to either drop one of them or
merge them into a single rate and amount. Both are wrong in a way that is hard to see
downstream.

So the response has an `events` array and the model emits one element per distinct economic
event, each with its own `type` drawn from `NoticeTypes.AllowedNames`. The classifier's guess
becomes a hint the extractor is allowed to disagree with, rather than a router that can send a
whole document to the wrong prompt.

An event the model cannot type is emitted as `unknown` with the warning `untyped_event`.
Surfacing it to a reviewer is strictly better than dropping it or guessing a type.

## The field-name convention

`ExtractedField` is flat — one row per `FieldName` — and the response is nested. The bridge is
a path convention, implemented once in `ExtractedFieldFlattener` and shared by every provider.
Two providers inventing slightly different names for the same field is how an accuracy report
quietly stops comparing like with like.

**Dotted path from the root, array position in square brackets:**

```
notice_date
identifiers.borrower_name
events[0].type
events[0].economics.principal_amount
events[1].economics.all_in_rate
warnings[0]
```

The set of names is open rather than a fixed list, because event indices are unbounded. That
is a change from what issue #11 originally specified ("a documented list of names per type")
and the reason this file exists: the convention is pinned here instead.

`field_confidence` is keyed by these same paths, so a field's confidence is looked up by the
field's own name rather than carried in a parallel structure that can drift out of step with
it. The table itself is not stored as fields.

**Nulls are dropped.** Most fields are null on any given notice; storing them would bury the
dozen a notice states under the sixty it does not. The prompt reserves `null` for "not
stated", so a value the notice states as empty survives as an empty string.

## Verbatim against normalised

The prompt asks for rates as decimals (`5.32%` → `0.0532`), amounts without symbols or
separators, and dates as ISO `YYYY-MM-DD`. That is normalisation, not derivation, and
`ExtractedField` carries both halves: `RawValue` holds what the model returned as text and
`NumericValue` / `DateValue` hold the parsed projections.

A date the model wrote in prose parses to nothing — `DateValue` stays null and the text is
kept. A visible discrepancy on the review screen is better than a date silently parsed under
whatever culture the server happens to be running in.

## Two rules that cost money if they are dropped

**Never invent.** A field the notice does not state is null. A plausible-looking figure that
is not in the document is far more expensive than a null a reviewer fills in, because the
review screen can only catch the first if the model does not disguise guesses as data.

**Never compute.** If a notice states a base rate and a margin but no all-in rate,
`all_in_rate` is null. The arithmetic is obvious and that is exactly why it is tempting; a
derived figure presented as extracted is indistinguishable from one the notice stated.

Both are asserted in `ExtractionPromptTests` rather than trusted to review. The prompt is the
highest-leverage artifact in the system — a model swap is a configuration change, a bad prompt
is wrong output on every provider at once.

## What is never extracted

Payment instructions. Routing numbers, account numbers, SWIFT codes, and bank names —
remittance and agent alike. A bank routes the money; it does not describe the economics, and
the facility is what a notice has to be matched to.

The notices themselves contain this data and are stored verbatim as bytes, so it is in the
system either way. What this rule prevents is it becoming *queryable, indexed and rendered on
a review screen*, which is a materially different exposure from a blob nobody reads.

It is enforced three times over, because one layer failing must not be enough:

1. The prompt instructs the model not to extract it.
2. The schema has no field for it, and every object is `additionalProperties: false`.
3. `ExtractedFieldFlattener.ForbiddenSegments` drops the keys whatever a model returns.

The third exists for the case where a model ignores its schema. Each layer has a test against
it; a change that reintroduced bank details would have to defeat all three.

## Testing a prompt change

Prompt edits are regression-tested against generated notices rather than real ones.
[`tools/NoticeCorpusGenerator.linq`](../tools/NoticeCorpusGenerator.linq) builds a PDF and
the extraction that PDF should produce from the same `NoticeSpec`, so the ground truth is
correct by construction — nobody reads a PDF and types out what they saw, which is the step
that would otherwise put an error in the answer key.

The expected file is in the schema's own nested shape, so the same `ExtractedFieldFlattener`
flattens both sides of a comparison and a change to the path convention applies to both
without anyone maintaining a second copy of it.

Eight notices are frozen under
[`tests/OmsLoan.Domain.Tests/Extractors/Fixtures/corpus`](../tests/OmsLoan.Domain.Tests/Extractors/Fixtures/corpus)
as the baseline. Two of them exist for rules that nothing else can test:

- `003-rate-reset-no-all-in` states a base rate and a margin and not the total. The correct
  `all_in_rate` is null, and a model that does the arithmetic fails here and nowhere else.
- `005-combined-paydown-and-rate-reset` is two events in one document. A model that merges
  them, or that copies the new rate onto the paydown, fails here.

Every generated notice prints a full set of payment instructions and no expected file
contains any of them, so the prohibition is tested against documents that actually contain
what it refuses.

The comparison harness itself is not built — it needs a provider implementation to produce
the other half.

## Adding a version

Copy the pair to `extraction.v2.md` / `extraction.v2.schema.json`, add them as embedded
resources in `OmsLoan.Domain.csproj`, and move `PromptCatalog.CurrentVersion`. Leave v1 in
place — reprocessing compares an old extraction against a new one, and that needs the prompt
that produced the old row.
