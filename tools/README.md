# tools

Scratch scripts for working on the system — mostly LINQPad (`.linq`) files.

Things that belong here:

- Generating mock notice PDFs to feed the watched folder
- Calling a service or an extractor by hand to see what comes back
- Poking the database, seeding rows, clearing a test run
- Anything else useful during development that is not part of the product

Not to be confused with [`scripts/`](../scripts), which holds the deployment and
service-lifecycle scripts that are part of running the system for real.

## Conventions

Nothing here is on a build or test path, so these can be rough. Two things still matter:

**No credentials in a committed file.** Read secrets from the flat environment variables the
Worker reads, so a script and the service agree about what a machine is called on to have
set — see [docs/windows-service.md](../docs/windows-service.md#key-names).

| Purpose | Variable |
| --- | --- |
| Claude | `CLAUDE_API_KEY` |
| OpenAI | `OPEN_API_KEY` (not `OPENAI_API_KEY`) |
| Groq | `GROQ_API_KEY` |
| Graph tenant / app / secret | `GRAPH_TENANT_ID`, `GRAPH_CLIENT_ID`, `GRAPH_CLIENT_SECRET` |

`GraphDaemonSmokeTest.linq` already reads the Graph three, plus `GRAPH_USER` for the mailbox
address. The connection string is the exception: design-time tooling reads
`OMSLOAN_CONNECTION`, while the hosts read `ConnectionStrings__OmsLoan`.

**Write to a scratch folder, not into the repo.** Mock PDFs and other generated output
should land somewhere temporary or in a gitignored path, so a stray run does not show up in
`git status`.

## `NoticeCorpusGenerator.linq`

Generates mock agent-bank notices as PDFs, each paired with a `.expected.json` holding the
extraction that notice should produce. Both halves are built from the same spec, so the
ground truth is right by construction rather than by somebody reading a PDF and typing out
what they saw.

Output goes to `scripts/Notices/corpus/`, which is gitignored. Knobs are at the top of
`Main()`: `Seed`, `NoticeCount`, and the output path. Eight scenarios cycle across five
layouts — interest payment, rate reset, rate reset with the all-in rate withheld, principal
payment, a combined paydown and reset in one document, fee, rollover, and a revolver draw with
its commitment reduction.

Run it headless rather than opening LINQPad:

```bash
"/c/Program Files/LINQPad8/LPRun8.exe" tools/NoticeCorpusGenerator.linq
```

Two things to know before changing it:

- **Every notice carries payment instructions on the page and none in the expected JSON.**
  That is the test. A corpus with no bank details in it proves nothing about a rule whose
  job is to refuse bank details that are present.
- **A template may only render what the spec holds.** Both the page and the expected JSON
  are projections of the same `NoticeSpec`, which is what stops them disagreeing. Hardcoding
  a value into a template breaks the guarantee silently — the fixture would then claim an
  answer the notice does not state.

A frozen eight-notice subset is committed under
[`tests/OmsLoan.Domain.Tests/Extractors/Fixtures/corpus`](../tests/OmsLoan.Domain.Tests/Extractors/Fixtures/corpus)
as the regression baseline. Editing this generator changes what a given seed produces, so
those bytes are deliberately not regenerated on every run — see the README there.
