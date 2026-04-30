# `func start` Design (Draft)

This document sketches the proposed design for `func start` in the Azure Functions CLI (`func`). It is a working draft — open questions are called out inline.

## Goals

- Launch the Azure Functions host runtime with the correct host version, worker, and script root.
- Resolve those inputs from a layered configuration model (profile → config → environment).
- Keep the host runtime and language workers out of the base CLI; deliver them as workloads that the CLI loads at runtime (consistent with the existing workload model — see [building-a-workload.md](./building-a-workload.md)).

## High-Level Flow

```
func start [<path>] [--profile <name>] [--host-version <v>] [--port ...] ...
        │
        ▼
1. Build FuncRunContext from CLI args + .func/config.json + .env + appsettings.json
        │   (fallback: local.settings.json)
        ▼
2. Resolve host version
        ├── from --host-version, else
        └── from profile (--profile or default)
        ▼
3. Select an IHostRunner that supports the resolved host version
        ▼
4. Resolve stack + worker
        ├── Stack comes from config
        ├── Ask the IHostRunner's WorkerStackVersionMap which worker versions it supports
        └── Resolve stack handler → worker path
        ▼
5. Resolve script root path
        ├── default: stack handler's output dir
        └── overridden when <path> is provided (workingPathDefined = true on context)
        ▼
6. IHostRunner.RunAsync(context)
```

## Configuration Layering

In precedence order (highest first):

1. CLI options (`--host-version`, `--port`, `--functions`, …)
2. `.func/config.json` — profiles, host version resolution, stack
3. `.env`
4. `appsettings.json`
5. `local.settings.json` (fallback for back-compat)

## Host Version Resolution

- Resolve from the active profile (`--profile`, else default).
- Look through installed host workloads and pick the latest version that matches the profile's range.
- If multiple match, take the latest in range.
- `--force-latest`: install the newest available host workload even if it falls outside the profile's range.

### Open questions

- Do we warn when a newer host exists outside the profile's range? After how many days stale?
- Stale-host check: background process? On every `func start`? How often?
- Where does the "last checked" timestamp live — user profile?
- Do we rely on the workload manifest as the source of truth for available versions?

## Host Workload Shape

A host workload is a special workload type that contributes:

1. A **harness** — the `func start` implementation (a console app) that wraps `WebHost`.
2. A **WorkerStackVersionMap** — declares which stacks and worker versions the host supports, so workers do not need to be packaged inside the host workload.

Workers themselves ship in **worker workloads**. Each worker workload knows where its workers live; the host workload locates them via the stack/version map.

### Open design choices

1. Harness wraps `WebHost` in-proc.
2. Fully out-of-process host.
3. _(third option TBD)_

## Key Types

```csharp
internal interface IHostRunner
{
    HostVersion HostVersion { get; }

    // Stacks + worker versions this host runner supports.
    // Keyed by stack (e.g. "node", "python") → supported worker version range.
    // Avoids packaging workers inside the host workload.
    IReadOnlyDictionary<string, WorkerVersionRange> WorkerStackVersionMap { get; }

    Task RunAsync(FuncRunContext context);
}
```

```csharp
internal sealed class FuncRunContext
{
    // Set when `func start <path>` is invoked.
    // When true, the path overrides the stack runner's computed output dir.
    public bool WorkingPathDefined { get; init; }

    public string? Path { get; init; }
    public HostVersion HostVersion { get; init; }
    public Stack Stack { get; init; }
    public string WorkerPath { get; init; }
    public string ScriptRootPath { get; init; }
    // …host options (port, cors, functions filter, enable-auth, …)
}
```

```csharp
internal interface IStackRunHandler
{
    bool CanHandle(FuncRunContext context);

    // Pre-run: produce worker path, decide where to run the host from.
    Task PreRunAsync(FuncRunContext context, CancellationToken ct);

    // Post-run: cleanup, telemetry, etc.
    Task PostRunAsync(FuncRunContext context, CancellationToken ct);
}
```

## CLI Surface (initial)

| Option                | Description                                              |
| --------------------- | -------------------------------------------------------- |
| `<path>`              | Optional script root override                            |
| `--port`, `-p`        | Port to listen on (default 7071)                         |
| `--cors`              | Comma-separated CORS origins                             |
| `--cors-credentials`  | Allow cross-origin authenticated requests                |
| `--functions`         | Space-separated list of functions to load                |
| `--no-build`          | Skip build before running                                |
| `--enable-auth`       | Enable full auth handling                                |
| `--host-version`      | Explicit host runtime version (e.g. `4.1049.0`)          |
| `--profile`           | Profile to drive host version resolution                 |
| `--force-latest`      | _(proposed)_ Install newest host even if outside profile |

## Related / Follow-up Work

- **Workload pruning** — separate PRD; remove unused host/worker workloads.
- **Stale host version checks** — background detection + user-profile persistence.
- **Profile schema** in `.func/config.json` — to be specified.
