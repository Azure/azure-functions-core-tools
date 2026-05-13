# `func start` JSON output schema

`func start --output=json` (alias: `--output json`) emits one self-contained
JSON object per line on stdout (newline-delimited JSON, NDJSON / JSON Lines).
Records are not wrapped in an array; there are no commas between them.
See [`func-start-output-modes.md`](func-start-output-modes.md) for how JSON
fits alongside the compact and plain renderers.

The schema is designed for programmatic consumers (CI logs, log shippers, AI
agents) and is versioned via the `schema_version` field. v1 is the contract
described in this document.

> Status: **prototype**. The current implementation streams from a scripted
> in-memory event source so the UX can be reviewed before host integration
> lands. The shape of the stream is the contract we commit to. Field values
> in this prototype come from a fake source; field semantics are stable.

## Top-level invariants

- **One JSON object per line.** Lines are terminated with `\n` (LF). Records
  never span multiple lines.
- **Every record contains** `schema_version` (number), `kind` (string),
  and `timestamp` (ISO 8601 with offset, e.g. `2026-05-11T07:15:31.012Z`).
- **`schema_version`** is `1` in this revision.
- **Records are self-contained.** No record references "the previous line".
- **No interactive output.** ANSI / spinners / prompts are never emitted
  regardless of the parent TTY state.
- **Final record is `kind: "summary"`** before stdout closes. Agents can use
  it as an end-of-stream sentinel.

### Causal ordering

When a single host log entry produces both a raw `log` record and one or
more synthetic records (`invocation_started`, `host_state_changed`, etc.),
the raw `log` record is always emitted **first**, followed by the synthetic
record(s). This preserves a "you can read top-to-bottom" property for any
agent that reconstructs state.

### Exit codes

| Exit code | Meaning |
|-----------|---------|
| `0`       | Graceful shutdown — final `summary` record emitted before stdout closes. |
| non-zero  | The CLI or host failed to start, or the pipeline aborted before the summary was emitted. |

## Record kinds

The following `kind` discriminator values are part of the v1 contract.
Unknown kinds may be added in later versions; consumers should ignore kinds
they don't recognise.

### `log`

A verbatim host log record. This is the lowest-level form and is emitted
for every entry the host produces.

```json
{
  "schema_version": 1,
  "kind": "log",
  "timestamp": "2026-05-11T07:15:33.403Z",
  "category": "Function.HttpTrigger1",
  "level": "information",
  "event_id": 1,
  "event_name": "InvocationStarted",
  "message": "Executing 'HttpTrigger1' (Reason='HTTP', Id=…)",
  "exception": null,
  "attributes": {
    "function.name": "HttpTrigger1",
    "function.invocation_id": "8f2a1c92-3d04-4e1f-9a55-2e4f7a9e8b01",
    "trace_id": "4a0c1d6e6f5a9b3c2d1e0f6a7b8c9d0e"
  }
}
```

| Field | Type | Notes |
|-------|------|-------|
| `category` | string | Logger category, typically `Function.<name>`, `Host.<area>`, or `Worker.<lang>`. |
| `level` | string | One of `trace`, `debug`, `information`, `warning`, `error`, `critical`. |
| `event_id` | number | Numeric EventId (often `0` if unspecified). |
| `event_name` | string \| absent | Friendly EventId name when present. |
| `message` | string | The rendered log message. |
| `exception` | object \| `null` | `{ type, message, stack? }` when the entry carried an exception. |
| `attributes` | object | Free-form key-value bag. Well-known keys listed in *Attribute conventions* below. |

### `host_state_changed`

The CLI host transitioned between lifecycle states (`starting`, `ready`,
`recycling`, `stopped`).

```json
{
  "schema_version": 1,
  "kind": "host_state_changed",
  "timestamp": "2026-05-11T07:16:02.103Z",
  "from": "ready",
  "to": "recycling",
  "duration_ms": 7901.773,
  "reason": "file_changed",
  "trigger": "HttpTrigger1.cs"
}
```

| Field | Type | Notes |
|-------|------|-------|
| `from` | string | Previous lifecycle state. `null`/absent for the very first transition. |
| `to` | string | New lifecycle state. One of `starting`, `ready`, `recycling`, `stopped`. |
| `duration_ms` | number \| absent | Milliseconds spent in `from` (or the host startup duration for `starting → ready`). |
| `reason` | string \| absent | Reason for the transition (e.g. `file_changed`, `manual`). |
| `trigger` | string \| absent | Path / item that triggered the transition. |

### `function_discovered`

A function is now part of the host's index. Emitted on startup and on every
re-discovery after a host recycle.

```json
{
  "schema_version": 1,
  "kind": "function_discovered",
  "timestamp": "2026-05-11T07:15:31.018Z",
  "name": "HttpTrigger1",
  "trigger_type": "http",
  "route": "/api/hello",
  "http_methods": ["GET", "POST"]
}
```

| Field | Type | Notes |
|-------|------|-------|
| `name` | string | Function display name. |
| `trigger_type` | string | `http`, `queue`, `timer`, `blob`, `eventhub`, `servicebus`, … |
| `route` | string \| absent | Route template (HTTP), source path (storage), or cron expression (timer). |
| `http_methods` | array of strings \| absent | HTTP verbs for HTTP-triggered functions. |

### `function_removed`

A previously-discovered function is no longer indexed (typically emitted
during a host recycle, before `function_discovered` repopulates the index).

```json
{
  "schema_version": 1,
  "kind": "function_removed",
  "timestamp": "2026-05-11T07:16:02.105Z",
  "name": "HttpTrigger1"
}
```

### `invocation_started`

A function invocation began executing.

```json
{
  "schema_version": 1,
  "kind": "invocation_started",
  "timestamp": "2026-05-11T07:15:33.401Z",
  "function": "HttpTrigger1",
  "invocation_id": "8f2a1c92-3d04-4e1f-9a55-2e4f7a9e8b01",
  "trace_id": "4a0c1d6e6f5a9b3c2d1e0f6a7b8c9d0e",
  "attributes": {
    "http.method": "GET",
    "http.target": "/api/hello"
  }
}
```

| Field | Type | Notes |
|-------|------|-------|
| `function` | string | Function name. |
| `invocation_id` | string | Stable id; pair with `invocation_completed`. |
| `trace_id` | string \| absent | OTel trace id, when the host provided one. |
| `attributes` | object | Free-form bag — typically HTTP fields, queue message ids, etc. |

### `invocation_completed`

The matching end record for an `invocation_started`. Carries the result and
duration; carries `error` details if the invocation failed.

```json
{
  "schema_version": 1,
  "kind": "invocation_completed",
  "timestamp": "2026-05-11T07:15:48.260Z",
  "function": "HttpTrigger1",
  "invocation_id": "2b441e84-9b21-4cf3-b6e8-8a1d3c4e5f60",
  "trace_id": "a3f5e9d8c7b6a5948372615049382716",
  "result": "failed",
  "duration_ms": 38,
  "error": {
    "type": "System.IO.IOException",
    "message": "Access denied"
  }
}
```

| Field | Type | Notes |
|-------|------|-------|
| `result` | string | `succeeded` or `failed`. |
| `duration_ms` | number \| absent | Wall-clock execution time. |
| `error` | object \| absent | `{ type, message }` when `result == "failed"`. |

### `cli_diagnostic`

Surfaces conditions detected by the CLI itself rather than reported by the
host. Examples: orphan invocations after the tracker timeout, source-stream
disconnects, fallbacks (e.g. a forced renderer downgrade). Includes an
actionable `recommendation` when one applies.

```json
{
  "schema_version": 1,
  "kind": "cli_diagnostic",
  "timestamp": "2026-05-11T07:20:14.000Z",
  "code": "renderer_downgrade",
  "level": "warning",
  "message": "stdout is not an interactive terminal; falling back to --output=plain.",
  "recommendation": "Re-run with --output=plain to silence this notice, or with --output=json for programmatic use."
}
```

| Field | Type | Notes |
|-------|------|-------|
| `code` | string | Stable identifier for the condition (e.g. `orphan_invocation`, `source_disconnected`, `renderer_downgrade`). |
| `level` | string | `information`, `warning`, or `error`. |
| `message` | string | Human-readable description. |
| `recommendation` | string \| absent | Next-step hint when one applies (often references a `func` command or doc URL). |
| Other fields | varies | Diagnostic-specific context (e.g. `file`, `command`). |

### `summary`

Always the last record before stdout closes. Captures the aggregate
counters and the reason the host exited.

```json
{
  "schema_version": 1,
  "kind": "summary",
  "timestamp": "2026-05-11T07:17:08.412Z",
  "exit_reason": "sigint",
  "uptime_seconds": 97,
  "function_count": 3,
  "invocations": {
    "total": 2,
    "succeeded": 1,
    "failed": 1
  },
  "errors": 1
}
```

| Field | Type | Notes |
|-------|------|-------|
| `exit_reason` | string | `sigint` (Ctrl+C), `source_completed` (event source ended), or `host_failed`. |
| `uptime_seconds` | number | Host uptime in seconds, with sub-second precision. |
| `function_count` | number | Functions known at exit time. |
| `invocations.total` | number | Total invocations the CLI observed. |
| `invocations.succeeded` | number | |
| `invocations.failed` | number | |
| `errors` | number | Same as `invocations.failed`. Surface name kept for compatibility with the compact/plain status lines. |

## Attribute conventions

`log` and `invocation_*` records carry an `attributes` bag of free-form
key-value data. The CLI consumes the following well-known keys; producers
are encouraged to align with these names so the dashboard renders correctly.
Names align with OpenTelemetry FaaS semantic conventions where applicable.

| Key | Type | Meaning |
|-----|------|---------|
| `function.name` | string | Function display name. |
| `function.invocation_id` | string | Stable id of an in-flight invocation. |
| `function.trigger_type` | string | `http`, `queue`, `timer`, `blob`, … |
| `function.route` | string | Route template, source path, or cron expression. |
| `function.http_methods` | array of strings | HTTP verbs (HTTP triggers). |
| `function.result` | string | `succeeded` \| `failed` (on `invocation_completed`). |
| `duration_ms` | number | Numeric milliseconds. |
| `trace_id` | string | OTel trace id (when known). |
| `span_id`, `parent_span_id` | string | OTel span ids (when known). |
| `host.state` | string | `starting` \| `ready` \| `recycling` \| `stopped`. |
| `host.version` | string | Host runtime version. |
| `host.listen_uri` | string | Bind URI (e.g. `http://localhost:7071`). |
| `host.startup_duration_ms` | number | Time from `starting` to `ready`. |
| `host.recycle_reason` | string | Reason for a `ready → recycling` transition. |
| `host.recycle_trigger` | string | Path / item that caused the recycle. |
| `http.method` | string | HTTP verb. |
| `http.target` | string | HTTP route the invocation served. |
| `cli.event_kind` | string | Producer-side discriminator (`log`, `host_state_changed`, `function_discovered`, `invocation_started`, `invocation_completed`, …) that lets the consumer skip heuristics and use the explicit kind. |

Producers that emit `cli.event_kind` save the consumer from message-pattern
fallbacks. When the attribute is absent, the consumer derives the synthetic
event from attribute presence (e.g. `function.invocation_id` +
`function.result` ⇒ `invocation_completed`).

## Versioning

- `schema_version: 1` is the current contract.
- Additive changes (new optional fields, new `kind` values, new attribute
  keys) will **not** bump `schema_version`. Consumers must tolerate unknown
  fields and kinds.
- Breaking changes (renames, semantics changes, required-field removals)
  bump `schema_version`. The new schema will be documented alongside this
  one; both major versions will be supported for at least one CLI release
  before the older one is removed.

## Stability notes

The v1 prototype:

- Emits `kind: "log"` once per host log entry, regardless of whether a
  synthetic record will also be emitted from the same entry.
- Includes both a top-level discriminated field (e.g. `result`,
  `duration_ms`) and the underlying attribute (`function.result`,
  `duration_ms`) on synthetic records. Top-level fields are stable; the
  duplicated attributes mirror what the host produced and may be omitted in
  later revisions if the host transport changes.
- Does not yet emit `cli_diagnostic` records; the kind is reserved.

## Worked example

The canonical sequence used by the demo source and by the mockups in the
design plan produces:

```text
log
host_state_changed
log               function_discovered  (×3)
log invocation_started
log               (Executing '…' envelope from the worker)
log invocation_completed
log invocation_started
log               (failed-execution log)
log invocation_completed
log host_state_changed (ready → recycling)
log function_removed (×3)
log host_state_changed (recycling → ready)
log function_discovered (×3)
summary
```

The `summary` record is the agreed termination signal. After it is written,
stdout is flushed and the process exits with code `0`.
