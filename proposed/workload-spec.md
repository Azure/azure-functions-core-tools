# Workload Spec

> **Status:** Draft. This spec defines the contract between the Func CLI
> and workload packages. v1 uses an **in-process** model: workloads are
> .NET assemblies loaded into the `func` process inside isolated
> collectible `AssemblyLoadContext`s.

## 1. Goals

- Allow the v5 `func` CLI to be extended with language- and feature-
  specific functionality without rebuilding or shipping a new CLI.
- Keep the CLI binary small and stable; defer language-specific work to
  workloads acquired on demand.
- Provide a uniform UX across all languages: a user runs the same
  `func init`, `func new`, `func pack` regardless of which workload backs
  the project.
- Allow the Functions team and external authors to ship workloads on their
  own cadence, independent of the Func CLI's release schedule.
- Make workload acquisition, update, and removal a first-class CLI
  experience (`func workload …`).

## 2. Non-goals (v1)

- **Sandboxing.** Workloads run with the same OS privileges as `func`. We
  rely on package provenance (signed NuGets) for trust, not runtime
  containment.
- **Cross-workload coordination.** Workloads do not call each other.
- **Authoring telemetry framework.** Workloads may emit logs/diagnostics;
  shared telemetry primitives are out of scope for v1.
- **GUI / IDE integration.** This spec covers the CLI only. IDE
  integrations consume `func` as a black box.
- **Workload-supplied UI prompts.** v1 keeps interactive prompts on the
  CLI side; the workload returns data, the CLI renders.

## 3. Definitions

| Term                  | Meaning                                                                 |
|-----------------------|-------------------------------------------------------------------------|
| **Func CLI**          | The `func` binary itself. Owns command parsing, UX, workload lifecycle. Distinct from the Functions host runtime, which is itself delivered as a workload (see §4.6). |
| **Host runtime**      | `Microsoft.Azure.WebJobs.Script.WebHost` — the process that actually executes Functions. Acquired and versioned as a workload, launched by `func start`. |
| **Workload**          | An installable extension contributing init/new/pack support and/or commands. |
| **Workload package**  | The distributable unit. NuGet in v1; format may evolve.                 |
| **Global manifest**   | The single CLI-owned JSON file at `~/.azure-functions/workloads.json` recording which workload versions are installed and how to load them. |
| **Worker runtime**    | The Functions runtime language identifier (`dotnet`, `node`, `python`, `java`, `powershell`, `custom`). |
| **Contribution point** | An interface in `Func.Cli.Abstractions` (e.g. `IProjectInitializer`) that a workload registers to participate in a built-in command. |
| **Project marker**    | A glob/file pattern (`*.csproj`, `package.json`, …) that hints which workload owns a given directory. |

## 4. User-facing requirements

### 4.1 `func workload` — package management

| Command                                | Behavior                                                          |
|----------------------------------------|-------------------------------------------------------------------|
| `func workload list`                   | List installed workloads with id, version, capabilities, install path. |
| `func workload search [<query>]`       | Search the configured workload catalog (NuGet feed). Returns id, latest version, description. |
| `func workload install <id> [--version <v>] [--from <path>]` | Acquire and install a workload. `--from` installs from a local path/feed for development. |
| `func workload uninstall <id> [--all-versions]` | Remove an installed workload. By default removes only the active version. |
| `func workload update [<id>] [--all]` | Update one or all workloads to the latest compatible version.     |

**Required behaviors:**

- All commands must be **non-interactive** when given complete arguments and
  must exit with a non-zero code on failure.
- `install`, `uninstall`, `update` must be **idempotent**: re-running with
  the same effective state must succeed without error.
- A failed install must leave no partial state on disk (atomic move into
  the install root, or rollback).
- Concurrent invocations must not corrupt the workload store. (Cross-
  process locking on the install root.)

### 4.2 `func init` — scaffold a project

- If no workload is installed for the requested runtime, the Func CLI **must**
  print an actionable hint with the exact command to install one (e.g.
  `func workload install python`) and exit with a clear error.
- If a workload is installed, the Func CLI delegates project scaffolding to it.
- If `--stack` is omitted, the Func CLI **may** prompt or auto-select
  based on installed workloads. Behavior must be consistent across
  workloads.

### 4.3 `func new` — create a function from a template

- Templates come **only** from installed workloads. The Func CLI has no
  built-in templates.
- `func new` (no args) lists templates from every installed workload that
  matches the **current project's stack**, resolved in this order:
  1. Explicit `--stack <name>` flag.
  2. `FUNCTIONS_WORKER_RUNTIME` in `local.settings.json`.
  3. A future `.func/config.json` (see §12 open questions).
  4. Inferred from project markers via `IProjectDetector`.

  If no stack can be resolved, `func new` lists templates from every
  installed workload and prompts the user (or errors in non-interactive
  mode) to disambiguate via `--stack`.
- `func new --template <name> --name <fn>` materializes the template
  identified by name. If multiple workloads expose the same template name,
  the Func CLI disambiguates by `--stack` or via an error listing the
  conflicts.

### 4.4 `func pack` — build & package for deployment

- The Func CLI inspects the current directory and asks each candidate workload
  (filtered by project markers, then by `IProjectDetector`) whether it owns
  the directory.
- Exactly one workload must claim the directory. Zero is an error with an
  install hint. More than one is an error listing the contenders, suggest
  `--stack` to disambiguate.
- Pack output paths and naming are workload-defined; the Func CLI surfaces the
  resulting artifact path back to the user.

### 4.5 `func start` and other built-in commands

- `func start` launches the Functions **host runtime**. The Func CLI **must
  not** embed the host runtime in its own binary; the runtime is acquired
  and updated as a workload (see §4.6) so it can ship and version
  independently of the CLI.
- In v1, `func start` resolves the host-runtime workload, ensures it is
  installed (offering to install if missing), and delegates process
  startup to it. Argument forwarding, port selection, and log streaming
  remain CLI responsibilities.
- Workloads **may** register additional command trees (e.g., `func durable
  …`). The Func CLI must surface these in `--help` output alongside built-in
  commands and label the source workload in verbose help.

### 4.6 Host runtime as a workload

- The Functions host runtime is distributed as a first-class workload
  (e.g. `Azure.Functions.Cli.Workload.Host`). It is **not** bundled
  with the CLI binary.
- The host-runtime workload contributes a `host.run` capability (analogous
  to the language workload capabilities in §5) that `func start` invokes.
- Multiple host-runtime versions **may** coexist; the CLI selects one per
  invocation based on (in order): `--host-version` flag, project pin
  (e.g. `host.json` / project file metadata), latest installed.
- All other workload contracts (manifest, lifecycle, update, telemetry)
  apply unchanged. There is no special-case install path for the host
  runtime.
- This separation lets host hotfixes ship without a CLI release and lets
  the CLI itself be smaller and AOT-friendly.

## 5. Workload contract — what a workload provides

A workload is a NuGet package containing a .NET assembly that implements
`IWorkload`. The assembly declares its entry point with a single
assembly-level attribute:

```csharp
[assembly: ExportCliWorkload<MyWorkload>]
```

The `IWorkload` implementation receives a `FunctionsCliBuilder` during
host startup and registers concrete services into DI. The CLI consumes
those services via `IEnumerable<T>` collections and dispatches based on
which workload contributed which service. There is **no static
capability list** — what a workload can do is whatever it has registered.

### 5.1 Contribution points (DI)

A workload **may** register any subset of these services. Each service
the workload registers means it participates in the corresponding command:

| Service registered                | The workload can…                                          | Used by                          |
|-----------------------------------|-----------------------------------------------------------|----------------------------------|
| `IProjectInitializer`             | Scaffold a new project for a given worker runtime.        | `func init`                      |
| `IProjectDetector`                | Decide whether a directory is "its" project.              | `func pack` directory routing    |
| `ITemplateProvider`               | Enumerate and materialize templates.                      | `func new`                       |
| `IPackProvider`                   | Build & package the project.                              | `func pack`                      |
| `IExternalCommand`                | Register additional CLI commands (e.g. `func durable …`). | Feature workloads                |
| `IHostRuntimeLauncher`            | Launch the Functions host runtime process.                | `func start` (host workload)     |

The exact interface names and shapes are documented in
[`building-a-workload.md`](./building-a-workload.md). Adding a new
contribution point is an additive change to `Func.Cli.Abstractions`
(no manifest field to coordinate, no schema bump).

### 5.2 Package metadata

Workload packages do **not** ship a per-package manifest file. The
metadata the CLI needs is split between two sources:

1. **NuGet metadata (`.nuspec`)** — owned by the package author, captured
   at install time:
    - `id` → package id (NuGet rules apply, case-insensitive).
    - `version` → semver, the workload version.
    - `title` → display name shown in `func workload list`.
    - `description` → one-line summary shown in `func workload list`.
    - `tags` → space-separated short aliases (e.g. `dotnet csharp`)
      the user can pass to `install` / `uninstall` instead of the full id.
    - `packageTypes` → **must** include a `FuncCliWorkload` entry. This
      is how the CLI (and the catalog) distinguish workload packages
      from arbitrary NuGets. Packages without this package type are
      rejected at install time and excluded from `func workload search`
      results. Modeled on the .NET CLI's `DotnetTool` package type
      (e.g. `?packageType=dotnettool` on the NuGet search API).
2. **Assembly attribute** — `[assembly: ExportCliWorkload<T>]` declares
   the entry-point type. The install pipeline scans for it once and
   records `(assembly path, type FQN)` in the global manifest.

The CLI persists everything it needs for startup in a single
**global manifest** at `~/.azure-functions/workloads.json` (see §6.1).
Workload authors never touch this file; it is owned by `func workload
install` / `uninstall`.

### 5.3 Project detection

- Pre-filter: the Func CLI applies project-marker globs (declared by
  the workload's `IProjectDetector` registration) before invoking the
  detector. Workloads with no markers are always candidates.
- Detection: the workload's `IProjectDetector` returns a confidence
  (`yes` / `no` / `maybe`) plus an optional reason string.
- Tie-breaking: if multiple workloads return `yes`, the Func CLI errors
  with the list and asks the user to disambiguate via `--stack`.

### 5.4 Versioning

- Workloads use **semver**. `func workload update` uses the same major
  version by default; major bumps require an explicit opt-in.
- The CLI declares a **minimum supported global-manifest schema version**
  (see §10) and rejects manifests it cannot parse.

## 6. Lifecycle

### 6.1 Install

1. Resolve `<id>` to a package via the catalog (NuGet by default),
   filtered to packages declaring the `FuncCliWorkload` package type.
2. Download to a staging directory.
3. Read NuGet metadata (`id`, `version`, `title`, `description`, `tags`,
   `packageTypes`). Reject the package if `FuncCliWorkload` is not
   among its declared package types.
4. Scan the package's top-level assemblies for `[assembly:
   ExportCliWorkload<T>]`. Exactly one assembly must declare it; zero
   or multiple is a fatal install error.
5. Atomically move into `~/.azure-functions/workloads/<id>/<version>/`.
6. Update the global manifest `~/.azure-functions/workloads.json`,
   adding an entry under `workloads[<id>][<version>]` with
   `displayName`, `description`, `aliases`, `installPath`, and
   `entryPoint: { assembly, type }`. The write is atomic (temp file +
   rename) so a crash mid-install cannot corrupt the manifest.
7. Emit an "installed" message including version and entry-point type.

### 6.2 Discovery

- On every Func CLI startup, the loader reads the global manifest at
  `~/.azure-functions/workloads.json`. Discovery is a **single JSON
  read + N assembly loads**, where N is the number of installed
  workloads — no filesystem walking of install directories.
- Discovery **must** complete in O(workload count) and **must not**
  invoke any workload code beyond instantiating the entry-point type
  (no scanning of all types, no running of static initializers beyond
  what the runtime triggers on first method dispatch).
- Each workload is loaded into its **own collectible
  `AssemblyLoadContext`** so two workloads can ship conflicting
  dependency versions without clashing.

### 6.3 Invocation

- The Func CLI dispatches each request (init/new/pack/etc.) to whichever
  workload(s) registered the corresponding contribution-point service
  (see §5.1). When more than one workload registers the same service,
  the dispatch rules of the consuming command apply (e.g. `--stack`
  disambiguation, project detection routing).
- Errors from a workload **must** surface to the user with the workload id
  prefixed (e.g. `[python] error: ...`).
- The Func CLI **must** distinguish between workload-protocol errors (bug, ask
  user to file an issue) and user-facing errors (display verbatim).

### 6.4 Update

- `func workload update <id>` resolves a newer version, installs it
  side-by-side, then atomically swaps the "active" version pointer.
- Old versions remain on disk until `--prune` is passed, allowing rollback.

### 6.5 Removal

- `func workload uninstall <id>` removes the active version's directory.
- `--all-versions` removes every installed version.
- Other installed workloads must be unaffected.

### 6.6 Background staleness check

- The Func CLI **may** periodically check the catalog for newer versions of
  installed workloads and surface a one-line hint (`A new version of
  python (1.2.3) is available. Run 'func workload update python' to
  upgrade.`).
- Frequency: at most once per 24h. Network failures must be silent.
- Disabled by `FUNCTIONS_DISABLE_WORKLOAD_UPDATE_CHECK=1`.

## 7. Error handling requirements

| Scenario                                    | Required behavior                                                                                  |
|---------------------------------------------|----------------------------------------------------------------------------------------------------|
| No workload installed for runtime           | Print exact install command, exit non-zero.                                                       |
| Global manifest unreadable / unknown schema | Throw a `GracefulException` with the manifest path and the action to take (update CLI). Do not crash silently. |
| Workload entry-point missing or unloadable  | Print the underlying error and the install path; suggest `func workload uninstall && install`.   |
| Workload fails during request               | Surface the error with `[<workload-id>]` prefix; exit non-zero with the workload's exit code (or 1). |
| Two workloads claim same project / template | Error listing all candidates, suggest `--stack` to disambiguate.                                  |
| Catalog unreachable                         | `install`/`update` must error clearly; `list` and offline operations must still work.             |

## 8. Performance requirements

- **CLI startup overhead** for workload discovery (no invocation) must
  remain under **100 ms** for up to 25 installed workloads on a warm cache.
- **First invocation** of a workload (cold) should complete the
  workload-side boot in under **300 ms** for AOT'd workloads and
  **800 ms** for JIT.
- **Subsequent invocations** within the same `func` invocation should not
  re-pay boot cost when the Func CLI can reuse the workload connection (an
  optimization, not a v1 requirement).

## 9. Security & trust

- v1 trusts NuGet package provenance. Workloads acquired from non-default
  feeds must be flagged in `func workload list` (`source: <feed>`).
- The Func CLI **must not** execute any workload code during discovery. Any
  code execution path must require explicit user action (install, init,
  new, pack, custom command).
- Workloads run with the user's privileges. We document this clearly in
  `func workload install --help`.

## 10. Compatibility & versioning

### 10.1 Global manifest schema

The global manifest at `~/.azure-functions/workloads.json` carries a
top-level `schemaVersion: <int>` field. The current schema is **v1**.

- The Func CLI **must** check `schemaVersion` on every read.
- A manifest with a `schemaVersion` higher than the CLI supports
  **must** be rejected with a `GracefulException` instructing the user
  to update the CLI. The CLI **must not** attempt a partial parse.
- A manifest with no `schemaVersion` field (legacy, written before the
  field existed) is treated as v1 and re-emitted with `schemaVersion: 1`
  on the next write.

### 10.2 Workload compatibility

- Workloads use **semver**; `func workload install` and `update` honour
  the standard NuGet resolution rules.
- A workload built against a newer Func CLI abstractions package than
  the running CLI supports must fail to load with a "Func CLI too old,
  please update" message — surfaced as an `[<workload-id>]`-prefixed
  error from the loader.
- A workload built against an older abstractions package must continue
  to work as long as the CLI can still satisfy the contract types it
  resolves; otherwise the CLI rejects with "workload too old, please
  run `func workload update`".

## 11. Telemetry expectations

- The Func CLI emits anonymous telemetry per command invocation including:
  `command`, `workload-id` (when one was selected), `outcome`
  (success / user-error / workload-error / cli-error), `duration`.
- Workloads **may** emit their own telemetry but **must not** propagate
  user-identifying data through the Func CLI.
- Telemetry is opt-out via the existing `FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT`
  environment variable.

## 12. Open questions

1. **Catalog source** — single NuGet feed by default, or multi-feed with
   precedence rules?
2. **Workload signing** — do we require signed packages for v1, or only
   warn?
3. **Cross-RID workloads** — does `func workload install python` resolve
   per-RID artifacts, or does the workload bundle all RIDs?
4. **Self-contained / single-file `func`** — `WorkloadPaths` already
   supports a configurable root, but a portable single-file `func`
   loses portability the moment a workload is installed unless we
   probe `<func-dir>/workloads` first. Decide before the install
   command lands.
5. **Bidirectional UX** (logs, progress, prompts) — CLI-rendered only in
   v1, or do we lock in a callback contract now to avoid breaking changes
   later?
6. **Built-in workload** — does the CLI ship with any workload pre-installed
   (e.g. seed the host-runtime workload on first run), or is the post-install
   state always "no workloads" and `func start` triggers an install prompt?
7. **Host-runtime version pinning** — where does the per-project pin live
   (`host.json`, `local.settings.json`, a new `func.json`, project file)?
8. **Host-runtime workload boundary** — does one workload ship all RIDs +
   all major host versions, or one workload per major (e.g. `…Workload.Host.v4`,
   `…Workload.Host.v5`)?
