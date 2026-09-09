# Frozen regression baseline

Eight generated notices — one per scenario — each paired with the extraction it should
produce. Produced by [`tools/NoticeCorpusGenerator.linq`](../../../../../tools/NoticeCorpusGenerator.linq)
at seed `20260909`.

| File | What it is there to catch |
| --- | --- |
| `001-interest-payment-t1` | The ordinary case: a full accrual with rates, days and an amount |
| `002-rate-reset-t2` | A reset with the all-in rate stated |
| `003-rate-reset-no-all-in-t3` | **Never compute.** Base rate and margin are stated, the total is not. The correct `all_in_rate` is null |
| `004-principal-payment-t4` | A paydown: before, amount, after — and the amount is the payment, not the balance |
| `005-combined-paydown-and-rate-reset-t5` | **Two events in one document.** Neither may carry the other's figures |
| `006-fee-t1` | A fee with a type and an unfunded commitment |
| `007-rollover-t2` | Rollover, the type that was missing from `NoticeType` |
| `008-revolver-draw-and-commitment-change-t3` | **Two events again, and a different pair.** A drawdown and a commitment reduction, with the fields nothing else exercises — `drawdown_amount`, `commitment_reduction_amount`, `lender_share_amount` |

Every one of them prints a full set of payment instructions on the page — bank name, ABA,
account number, SWIFT — and no expected file contains any of it. That is deliberate. A rule
that refuses bank details is only tested by a document that has bank details in it.

## The answer key is tested too

`NoticeCorpusTests` asserts that each event's type agrees with the economics under it — a
`drawdown` carries `drawdown_amount` and never `principal_amount`, a `principal_payment` the
reverse. That exists because 008 was originally typed `principal_payment` while carrying a
drawdown: money borrowed, filed as money repaid. There was no `drawdown` in the vocabulary to
put it under, so the answer key asserted something untrue.

It is worth the test rather than just the fix. A wrong answer key is the most expensive defect
this folder can hold: once a scorer exists, a model that reads the notice correctly fails
against our mistake, and the obvious response to a failing accuracy run is to change a prompt
that was right.

## These bytes are the point

Do not regenerate this folder to pick up an unrelated change to the generator. Comparing
prompt `v1` against `v2` only means something if both saw the same PDFs, and the seed does
not protect that — it reproduces a corpus only while the generator itself is unchanged, so
any edit to a template or a scenario produces different notices from the same seed.

Regenerate deliberately, when the corpus itself is what is being changed, and say so in the
commit. Accuracy figures from before a regeneration cannot be compared with figures from
after it.

The wider corpus — forty notices across all five layouts — is generated on demand into
`scripts/Notices/corpus/` and is gitignored. Use that one for volume; this one for
comparison.
