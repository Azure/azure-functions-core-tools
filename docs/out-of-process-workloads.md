# Out-of-Process Workload Model

> **Status:** Prototype on `feature/workloads-oop` — parallel design to the
> in-process model on `feature/workloads`. Built so the team can compare both
> approaches and pick a direction.

## Why

`feature/workloads` loads each workload into the host process via
`AssemblyLoadContext`. That's incompatible with NativeAOT's closed-world
assumption — which means the host can't ship as an AOT binary as long as
workloads are loaded dynamically.

This branch keeps the same UX (`func init`, `func new`, `func pack`,
`func workload …`) but moves each workload into its **own process**. The host
talks to it over **JSON-RPC 2.0 on stdio**. That makes the host AOT-publishable
and gives every workload its own (also AOT-publishable) executable, with the
side benefit of process isolation and language-agnostic workloads.

## Architecture

```
┌───────────────────────────┐         ┌──────────────────────────┐
│ func (host, AOT-ready)    │ stdio   │ func-workload-<id>       │
│  ─ WorkloadHost           │◀───────▶│  ─ WorkloadServer (SDK)  │
│  ─ WorkloadInstaller      │ JSON-RPC│  ─ Domain logic          │
│  ─ Init/New/Pack commands │  + LSP  │                          │
└───────────────────────────┘ frames  └──────────────────────────┘
```

### Discovery

Workloads live under `~/.azure-functions/workloads-oop/<id>/<version>/` and
ship a `workload.json` manifest plus their executable. The host scans for
`workload.json` files; spawning a process is deferred until a request actually
needs that workload.

`workload.json` schema (see `WorkloadManifestFile`):

```json
{
  "schemaVersion": 1,
  "id": "sample",
  "version": "1.0.0",
  "displayName": "Sample workload",
  "description": "...",
  "workerRuntimes": [ "sample" ],
  "languages": [ "Demo" ],
  "protocolVersion": "1.0",
  "executable": "func-workload-sample",
  "executableArgs": [],
  "capabilities": [ "project.detect", "project.init", "templates", "pack" ],
  "projectMarkers": [ "*.sampleproj" ]
}
```

`projectMarkers` lets the host pre-filter candidates for `project.detect`
without spawning every installed workload.

### Wire format

LSP/DAP-style framing — one `Content-Length` header, blank line, exact-byte
payload. See `FrameCodec`. The payload is a JSON-RPC 2.0 envelope.

```
Content-Length: 89\r\n
\r\n
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"hostVersion":"vnext", ...}}
```

### Methods (v1)

| Method            | Direction      | Purpose                                                   |
|-------------------|---------------|-----------------------------------------------------------|
| `initialize`      | host→workload | Handshake, returns capabilities + supported runtimes      |
| `shutdown`        | host→workload | Graceful exit (workload also exits when stdin closes)     |
| `project.detect`  | host→workload | "Does this directory look like your kind of project?"     |
| `project.init`    | host→workload | Scaffold a new project                                    |
| `templates.list`  | host→workload | List available function templates                         |
| `templates.create`| host→workload | Materialize a function from a template                    |
| `pack.run`        | host→workload | Build & package the project for deployment                |

Workloads write **only** framed JSON-RPC responses to stdout. Diagnostics go
to stderr; the host forwards them with a `[workload:<id>]` prefix.

### Errors

Standard JSON-RPC 2.0 codes plus three workload-specific extensions in the
implementation-defined range:

| Code     | Meaning                       |
|---------:|--------------------------------|
| -32001   | Capability not supported       |
| -32002   | Protocol version mismatch      |
| -32010   | User error (surface verbatim)  |

## AOT story

- **Host** (`Func.Cli`): no dynamic assembly loading, all JSON via
  source-generated `WorkloadJsonContext` / `HostJsonContext`. Ready for
  `PublishAot` once existing dependencies (System.CommandLine, Spectre.Console)
  are AOT-validated.
- **Workloads**: each is a stand-alone executable referencing only
  `Func.Cli.Workload.Sdk` + `Func.Cli.Abstractions`. Same AOT story per
  workload; can be shipped as a per-RID NativeAOT binary.
- **Sample** (`Func.Workload.Sample`): demonstrates the full surface. It
  ships as a `.dll` for inner-loop dev (run via `dotnet`), but the source is
  AOT-clean.

## Future extensions

These are intentionally **not** in v1 to keep the demo small:

- **Bidirectional callbacks** — workload → host requests for `log`, `progress`,
  `prompt.confirm`, `prompt.select`. v1 routes workload diagnostics through
  stderr; the host owns rendering of result data only.
- **Real NuGet acquisition** — `func workload install <id>` currently uses the
  built-in catalog and a `--from <path>` escape hatch (`feature/workloads` has
  the same gap). Both branches will inherit a real installer when one lands.
- **Cancellation mid-request** — currently a request runs to completion or
  process death. Adding `$/cancelRequest` (LSP-style) is straightforward.
- **Per-session process reuse** — currently each command spawns a fresh
  workload process. For batch operations the host could keep a workload alive
  for the duration of one CLI invocation.

## Comparison to `feature/workloads`

| Concern                           | `feature/workloads` (in-proc)        | `feature/workloads-oop` (this branch)         |
|-----------------------------------|--------------------------------------|------------------------------------------------|
| Host AOT-publishable              | ❌ blocked by `AssemblyLoadContext`  | ✅ no dynamic loading                           |
| Workload language                 | .NET only (must load IWorkload)      | Any (just speak JSON-RPC over stdio)           |
| Workload AOT-publishable          | ❌ same issue, plus shared deps      | ✅ each workload independent                    |
| Workload bug crashes host         | ⚠️ yes                                | ✅ isolated process                             |
| Per-invocation startup cost       | None                                 | One process spawn (~10-30ms native, ~80ms JIT) |
| Cross-workload shared state       | Easy (in-proc)                       | None — pass through host                       |
| Versioning of shared dependencies | "DLL hell" risk                      | None — each workload self-contained            |
| API surface complexity            | C# interfaces (`IWorkload`, …)       | JSON-RPC method contract                       |
| Extensibility ceiling             | Limited to .NET assemblies            | Workloads in Python/Node/Go all viable         |
