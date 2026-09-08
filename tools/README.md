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
