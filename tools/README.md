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

**No credentials in a committed file.** Read the connection string from the
`OMSLOAN_CONNECTION` environment variable and API keys from `Extraction__<Provider>__ApiKey`,
the same names the Worker uses — see [docs/windows-service.md](../docs/windows-service.md).

**Write to a scratch folder, not into the repo.** Mock PDFs and other generated output
should land somewhere temporary or in a gitignored path, so a stray run does not show up in
`git status`.
