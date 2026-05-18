# `func start` output modes

`func start` can render the same host event stream in three output modes:
`compact`, `plain`, and `json`.

> Status: **prototype**. The current implementation streams from a scripted
> in-memory event source so the UX can be reviewed before host integration
> lands. Mode selection and output shape are the intended contract.

## Choosing a mode

Use `--output=<mode>` to force a mode:

```powershell
func start --output=compact
func start --output=plain
func start --output=json
```

`--no-tui` is an alias for `--output=plain`.

When `--output` is omitted, `func start` auto-selects the mode:

| Condition | Selected mode |
|-----------|---------------|
| Interactive terminal | `compact` |
| Non-interactive stdout, redirected output, or CI | `plain` |

`json` is never selected automatically. Programmatic consumers and AI agents
should opt in with `--output=json`.

If `compact` is requested but stdout cannot host an interactive live display,
the command downgrades to `plain` and writes a one-line notice to stderr.

## `compact`

`compact` is the interactive local-development view. During start
initialization, it renders the Azure Functions CLI name and version, then initialization steps.
After initialization completes, it replaces those transient lines with the pinned header, adaptive function
summary, streaming log tail, and pinned footer with compact controls. Completed
initialization steps are replayed into the dashboard log tail before host logs
begin.

Use it when:

- You are running locally in an interactive terminal.
- You want to keep host state, function health, and recent logs visible.
- You want keyboard controls such as function search, log filtering, and help.

The visible log tail is intentionally bounded. Use `--log-file=<path>` when
you need a complete copy of every host event while staying in compact mode.

## `plain`

`plain` is the streaming text fallback. It writes initialization status lines
before host logs and avoids animation and interactive terminal features, so it
is safe for CI, redirected output, and log capture.

Use it when:

- You are piping or redirecting output.
- You want grep-friendly human-readable logs.
- You need stable line-oriented text without live redraws.

## `json`

`json` emits newline-delimited JSON (NDJSON): one self-contained JSON object
per line, with no surrounding array and no commas between records. Start
initialization is represented with structured `start_initialization_*` records
before host log records begin.

Use it when:

- An AI agent, test harness, log shipper, or other tool is consuming output.
- You need versioned records with stable `kind` discriminators.
- You need a final `summary` record as an end-of-stream sentinel.

The JSON schema is documented in
[`func-start-json-schema.md`](func-start-json-schema.md).
